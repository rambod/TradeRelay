using Microsoft.Extensions.Hosting;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Desktop.Services;

internal sealed class TradingLifecycleService(
    ExchangeConnectionManager connections,
    AuditLogService audit,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly ExchangeId Bybit = new("bybit");
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private readonly Dictionary<string, string> _positionFingerprints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _orderFingerprints = new(StringComparer.Ordinal);
    private IExchangeStream? _attachedStream;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        connections.StateChanged += OnConnectionChanged;
        AttachStream();
        if (connections.Account is not null) await ReconcileAsync("baseline", stoppingToken).ConfigureAwait(false);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), timeProvider, stoppingToken).ConfigureAwait(false);
                if (connections.Account is not null) await ReconcileAsync("scheduled", stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            connections.StateChanged -= OnConnectionChanged;
            DetachStream();
        }
    }

    internal async Task ReconcileNowAsync(CancellationToken cancellationToken) => await ReconcileAsync("manual", cancellationToken).ConfigureAwait(false);

    private void OnConnectionChanged(object? sender, ProviderConnectionSnapshot snapshot)
    {
        AttachStream();
        if (snapshot.CredentialLoaded) _ = ReconcileAsync("connection", CancellationToken.None);
        else { _positionFingerprints.Clear(); _orderFingerprints.Clear(); }
    }

    private void AttachStream()
    {
        IExchangeStream? current = connections.Stream;
        if (ReferenceEquals(current, _attachedStream)) return;
        DetachStream();
        _attachedStream = current;
        if (current is null) return;
        current.OrderUpdated += OnOrderUpdated;
        current.ExecutionUpdated += OnExecutionUpdated;
        current.PositionUpdated += OnPositionUpdated;
    }

    private void DetachStream()
    {
        if (_attachedStream is null) return;
        _attachedStream.OrderUpdated -= OnOrderUpdated;
        _attachedStream.ExecutionUpdated -= OnExecutionUpdated;
        _attachedStream.PositionUpdated -= OnPositionUpdated;
        _attachedStream = null;
    }

    private void OnOrderUpdated(object? sender, OrderUpdate update) => _ = WriteAsync(TradingLifecycleKind.Order, "private_stream_order", "OBSERVED", update.Symbol, null, update.ExchangeOrderId, null, null, null, update.Status);
    private void OnExecutionUpdated(object? sender, ExecutionUpdate update) => _ = WriteAsync(TradingLifecycleKind.Execution, "private_stream_execution", "OBSERVED", update.Symbol, null, update.ExchangeOrderId, null, update.Quantity, update.Price, "Filled");
    private void OnPositionUpdated(object? sender, PositionUpdate update) => _ = WriteAsync(TradingLifecycleKind.Position, "private_stream_position", "OBSERVED", update.Symbol, update.Side, null, null, update.Size, null, update.Size == 0m ? "Closed" : "Open");

    private async Task ReconcileAsync(string reason, CancellationToken cancellationToken)
    {
        if (!await _reconcileLock.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            ITradingAccountProvider? account = connections.Account;
            if (account is null) return;
            IReadOnlyList<PositionSnapshot> positions = await account.GetPositionsAsync(null, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<OrderSnapshot> orders = await account.GetOpenOrdersAsync(null, cancellationToken).ConfigureAwait(false);
            await EmitPositionChangesAsync(positions, reason, cancellationToken).ConfigureAwait(false);
            await EmitOrderChangesAsync(orders, reason, cancellationToken).ConfigureAwait(false);
            await WriteAsync(TradingLifecycleKind.Reconciliation, $"{reason}_reconciliation", "OK", state: $"{positions.Count} positions; {orders.Count} open orders", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            await WriteAsync(TradingLifecycleKind.SafeError, "reconciliation_failed", "FAILED", state: "Current exchange state could not be reconciled.", errorCode: "RECONCILIATION_UNAVAILABLE", cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        finally { _reconcileLock.Release(); }
    }

    private async Task EmitPositionChangesAsync(IReadOnlyList<PositionSnapshot> current, string reason, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PositionSnapshot position in current)
        {
            string key = $"{position.Symbol}:{position.Side}";
            string fingerprint = $"{position.Size:G29}|{position.EntryPrice:G29}|{position.MarkPrice:G29}|{position.StopLoss:G29}|{position.TakeProfit:G29}";
            seen.Add(key);
            if (_positionFingerprints.TryGetValue(key, out string? previous) && previous == fingerprint) continue;
            _positionFingerprints[key] = fingerprint;
            await WriteAsync(TradingLifecycleKind.Position, $"{reason}_position", "CHANGED", position.Symbol, position.Side, quantity: position.Size, price: position.MarkPrice, state: "Open", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (string removed in _positionFingerprints.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _positionFingerprints.Remove(removed);
            string symbol = removed.Split(':', 2)[0];
            await WriteAsync(TradingLifecycleKind.Position, $"{reason}_position", "CHANGED", symbol, quantity: 0m, state: "Closed or absent", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EmitOrderChangesAsync(IReadOnlyList<OrderSnapshot> current, string reason, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (OrderSnapshot order in current)
        {
            string fingerprint = $"{order.Status}|{order.FilledQuantity:G29}|{order.Quantity:G29}";
            seen.Add(order.ExchangeOrderId);
            if (_orderFingerprints.TryGetValue(order.ExchangeOrderId, out string? previous) && previous == fingerprint) continue;
            _orderFingerprints[order.ExchangeOrderId] = fingerprint;
            await WriteAsync(TradingLifecycleKind.Order, $"{reason}_order", "CHANGED", order.Symbol, order.Side, order.ExchangeOrderId, order.ClientOrderId, order.Quantity, order.Price, order.Status, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (string removed in _orderFingerprints.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _orderFingerprints.Remove(removed);
            await WriteAsync(TradingLifecycleKind.Order, $"{reason}_order", "CHANGED", exchangeOrderId: removed, state: "No longer open", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<bool> WriteAsync(
        TradingLifecycleKind kind,
        string action,
        string result,
        string? symbol = null,
        TradeSide? side = null,
        string? exchangeOrderId = null,
        string? clientOrderId = null,
        decimal? quantity = null,
        decimal? price = null,
        string? state = null,
        string? errorCode = null,
        CancellationToken cancellationToken = default) => audit.TryWriteLifecycleAsync(new TradingLifecycleEvent(
            2,
            Guid.NewGuid(),
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow(),
            Bybit,
            connections.Snapshot.Environment,
            kind,
            action,
            result,
            symbol,
            side,
            exchangeOrderId,
            clientOrderId,
            quantity,
            price,
            state,
            errorCode), cancellationToken);
}
