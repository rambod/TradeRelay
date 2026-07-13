using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Desktop.Services;

internal sealed class TradingLifecycleService(
    IExchangeSessionCoordinator sessions,
    AuditLogService audit,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private readonly Dictionary<string, string> _positionFingerprints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _orderFingerprints = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<IExchangeStream, ProviderSessionAccess> _attachedStreams = new(ReferenceEqualityComparer.Instance);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        sessions.StateChanged += OnSessionChanged;
        AttachStreams();
        await ReconcileAllAsync("baseline", stoppingToken).ConfigureAwait(false);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), timeProvider, stoppingToken).ConfigureAwait(false);
                await ReconcileAllAsync("scheduled", stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { sessions.StateChanged -= OnSessionChanged; DetachStreams(); }
    }

    internal Task ReconcileNowAsync(CancellationToken cancellationToken) => ReconcileAllAsync("manual", cancellationToken);

    private void OnSessionChanged(object? sender, ProviderSessionAccess session)
    {
        AttachStreams();
        if (session.Account is not null) _ = ReconcileAsync(session, "connection", CancellationToken.None);
        else
        {
            string prefix = session.Descriptor.Id.Value + ":";
            foreach (string key in _positionFingerprints.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) _positionFingerprints.Remove(key);
            foreach (string key in _orderFingerprints.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) _orderFingerprints.Remove(key);
        }
    }

    private void AttachStreams()
    {
        IExchangeStream[] active = sessions.Sessions.Where(item => item.Stream is not null).Select(item => item.Stream!).ToArray();
        foreach (IExchangeStream removed in _attachedStreams.Keys.Where(stream => !active.Contains(stream, ReferenceEqualityComparer.Instance)).ToArray()) Detach(removed);
        foreach (ProviderSessionAccess session in sessions.Sessions.Where(item => item.Stream is not null))
        {
            IExchangeStream stream = session.Stream!;
            if (!_attachedStreams.TryAdd(stream, session)) { _attachedStreams[stream] = session; continue; }
            stream.OrderUpdated += OnOrderUpdated; stream.ExecutionUpdated += OnExecutionUpdated; stream.PositionUpdated += OnPositionUpdated;
        }
    }
    private void DetachStreams() { foreach (IExchangeStream stream in _attachedStreams.Keys.ToArray()) Detach(stream); }
    private void Detach(IExchangeStream stream) { stream.OrderUpdated -= OnOrderUpdated; stream.ExecutionUpdated -= OnExecutionUpdated; stream.PositionUpdated -= OnPositionUpdated; _attachedStreams.TryRemove(stream, out _); }

    private void OnOrderUpdated(object? sender, OrderUpdate update) { if (sender is IExchangeStream stream && _attachedStreams.TryGetValue(stream, out ProviderSessionAccess? session)) _ = WriteAsync(session, TradingLifecycleKind.Order, "private_stream_order", "OBSERVED", update.Symbol, exchangeOrderId: update.ExchangeOrderId, state: update.Status); }
    private void OnExecutionUpdated(object? sender, ExecutionUpdate update) { if (sender is IExchangeStream stream && _attachedStreams.TryGetValue(stream, out ProviderSessionAccess? session)) _ = WriteAsync(session, TradingLifecycleKind.Execution, "private_stream_execution", "OBSERVED", update.Symbol, exchangeOrderId: update.ExchangeOrderId, quantity: update.Quantity, price: update.Price, state: "Filled"); }
    private void OnPositionUpdated(object? sender, PositionUpdate update) { if (sender is IExchangeStream stream && _attachedStreams.TryGetValue(stream, out ProviderSessionAccess? session)) _ = WriteAsync(session, TradingLifecycleKind.Position, "private_stream_position", "OBSERVED", update.Symbol, update.Side, quantity: update.Size, state: update.Size == 0m ? "Closed" : "Open"); }

    private async Task ReconcileAllAsync(string reason, CancellationToken cancellationToken)
    {
        foreach (ProviderSessionAccess session in sessions.Sessions.Where(item => item.Account is not null)) await ReconcileAsync(session, reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReconcileAsync(ProviderSessionAccess session, string reason, CancellationToken cancellationToken)
    {
        await _reconcileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Account is null) return;
            IReadOnlyList<PositionSnapshot> positions = await session.Account.GetPositionsAsync(null, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<OrderSnapshot> orders = await session.Account.GetOpenOrdersAsync(null, cancellationToken).ConfigureAwait(false);
            await EmitPositionChangesAsync(session, positions, reason, cancellationToken).ConfigureAwait(false);
            await EmitOrderChangesAsync(session, orders, reason, cancellationToken).ConfigureAwait(false);
            await WriteAsync(session, TradingLifecycleKind.Reconciliation, $"{reason}_reconciliation", "OK", state: $"{positions.Count} positions; {orders.Count} open orders", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { await WriteAsync(session, TradingLifecycleKind.SafeError, "reconciliation_failed", "FAILED", state: "Current exchange state could not be reconciled.", errorCode: "RECONCILIATION_UNAVAILABLE", cancellationToken: CancellationToken.None).ConfigureAwait(false); }
        finally { _reconcileLock.Release(); }
    }

    private async Task EmitPositionChangesAsync(ProviderSessionAccess session, IReadOnlyList<PositionSnapshot> current, string reason, CancellationToken cancellationToken)
    {
        string prefix = session.Descriptor.Id.Value + ":"; var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PositionSnapshot position in current)
        {
            string key = $"{prefix}{position.Symbol}:{position.Side}", fingerprint = $"{position.Size:G29}|{position.EntryPrice:G29}|{position.MarkPrice:G29}|{position.StopLoss:G29}|{position.TakeProfit:G29}"; seen.Add(key);
            if (_positionFingerprints.TryGetValue(key, out string? previous) && previous == fingerprint) continue;
            _positionFingerprints[key] = fingerprint;
            await WriteAsync(session, TradingLifecycleKind.Position, $"{reason}_position", "CHANGED", position.Symbol, position.Side, quantity: position.Size, price: position.MarkPrice, state: "Open", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (string removed in _positionFingerprints.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal) && !seen.Contains(key)).ToArray()) { _positionFingerprints.Remove(removed); string symbol = removed[prefix.Length..].Split(':', 2)[0]; await WriteAsync(session, TradingLifecycleKind.Position, $"{reason}_position", "CHANGED", symbol, quantity: 0m, state: "Closed or absent", cancellationToken: cancellationToken).ConfigureAwait(false); }
    }

    private async Task EmitOrderChangesAsync(ProviderSessionAccess session, IReadOnlyList<OrderSnapshot> current, string reason, CancellationToken cancellationToken)
    {
        string prefix = session.Descriptor.Id.Value + ":"; var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (OrderSnapshot order in current)
        {
            string key = prefix + order.ExchangeOrderId, fingerprint = $"{order.Status}|{order.FilledQuantity:G29}|{order.Quantity:G29}"; seen.Add(key);
            if (_orderFingerprints.TryGetValue(key, out string? previous) && previous == fingerprint) continue;
            _orderFingerprints[key] = fingerprint;
            await WriteAsync(session, TradingLifecycleKind.Order, $"{reason}_order", "CHANGED", order.Symbol, order.Side, order.ExchangeOrderId, order.ClientOrderId, order.Quantity, order.Price, order.Status, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (string removed in _orderFingerprints.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal) && !seen.Contains(key)).ToArray()) { _orderFingerprints.Remove(removed); await WriteAsync(session, TradingLifecycleKind.Order, $"{reason}_order", "CHANGED", exchangeOrderId: removed[prefix.Length..], state: "No longer open", cancellationToken: cancellationToken).ConfigureAwait(false); }
    }

    private Task<bool> WriteAsync(ProviderSessionAccess session, TradingLifecycleKind kind, string action, string result, string? symbol = null, TradeSide? side = null, string? exchangeOrderId = null, string? clientOrderId = null, decimal? quantity = null, decimal? price = null, string? state = null, string? errorCode = null, CancellationToken cancellationToken = default) => audit.TryWriteLifecycleAsync(new TradingLifecycleEvent(2, Guid.NewGuid(), Guid.NewGuid().ToString("N"), timeProvider.GetUtcNow(), session.Descriptor.Id, session.Environment, kind, action, result, symbol, side, exchangeOrderId, clientOrderId, quantity, price, state, errorCode), cancellationToken);
}
