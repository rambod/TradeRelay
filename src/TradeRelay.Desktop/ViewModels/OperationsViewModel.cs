using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

internal sealed record PositionOperationRow(
    string Symbol,
    string Direction,
    decimal Quantity,
    decimal EntryPrice,
    decimal MarkPrice,
    decimal Exposure,
    decimal UnrealizedPnl,
    decimal Leverage,
    string LiquidationDistance,
    string Protection,
    string Reconciliation);

internal sealed partial class OperationsViewModel : ObservableObject, IDisposable
{
    private readonly ExchangeConnectionManager _connections;
    private readonly AuditLogService _audit;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty] private bool _isPositionsSelected = true;
    [ObservableProperty] private bool _isOrdersSelected;
    [ObservableProperty] private bool _isFillsSelected;
    [ObservableProperty] private bool _isProtectionSelected;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _lastRefresh = "Not refreshed";

    public OperationsViewModel(ExchangeConnectionManager connections, AuditLogService audit, IUiDispatcher dispatcher, TimeProvider timeProvider)
    {
        _connections = connections;
        _audit = audit;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _connections.StateChanged += OnConnectionChanged;
        _audit.LifecycleWritten += OnLifecycleWritten;
        _ = RefreshAsync();
    }

    public ObservableCollection<PositionOperationRow> Positions { get; } = [];
    public ObservableCollection<OrderSnapshot> Orders { get; } = [];
    public ObservableCollection<HistoricalOrder> OrderHistory { get; } = [];
    public ObservableCollection<HistoricalExecution> Fills { get; } = [];
    public ObservableCollection<AuditEvent> ObservedPositionHistory { get; } = [];
    public string ProviderLabel => $"Bybit · {_connections.Snapshot.Environment}";
    public string GrossExposure => Positions.Sum(item => item.Exposure).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    public string NetExposure => Positions.Sum(item => item.Direction == "Long" ? item.Exposure : -item.Exposure).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    public bool IsEmpty => Positions.Count == 0 && Orders.Count == 0 && Fills.Count == 0;

    [RelayCommand]
    private void ShowPositions() => Select("positions");
    [RelayCommand]
    private void ShowOrders() => Select("orders");
    [RelayCommand]
    private void ShowFills() => Select("fills");
    [RelayCommand]
    private void ShowProtection() => Select("protection");

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        Error = null;
        try
        {
            ITradingAccountProvider? account = _connections.Account;
            if (account is null)
            {
                Apply([], [], [], [], []);
                Error = "Connect an exchange account to inspect current operations.";
                return;
            }
            Task<IReadOnlyList<PositionSnapshot>> positionsTask = account.GetPositionsAsync(null, CancellationToken.None);
            Task<IReadOnlyList<OrderSnapshot>> ordersTask = account.GetOpenOrdersAsync(null, CancellationToken.None);
            IExchangeHistoryProvider? history = _connections.History;
            Task<IReadOnlyList<HistoricalOrder>> orderHistoryTask = history?.GetOrderHistoryAsync(new ExchangeHistoryQuery(Limit: 100), CancellationToken.None) ?? Task.FromResult<IReadOnlyList<HistoricalOrder>>([]);
            Task<IReadOnlyList<HistoricalExecution>> fillsTask = history?.GetExecutionHistoryAsync(new ExchangeHistoryQuery(Limit: 100), CancellationToken.None) ?? Task.FromResult<IReadOnlyList<HistoricalExecution>>([]);
            Task<AuditHistoryPage> observedTask = _audit.QueryAsync(DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime.AddDays(-30)), null, 1, 200, CancellationToken.None);
            await Task.WhenAll(positionsTask, ordersTask, orderHistoryTask, fillsTask, observedTask).ConfigureAwait(false);
            Apply(await positionsTask, await ordersTask, await orderHistoryTask, await fillsTask, (await observedTask).Events.Where(item => item.Tool.Equals(nameof(TradingLifecycleKind.Position), StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch (ProviderException exception)
        {
            Error = $"{exception.Code}: Current exchange operations could not be loaded.";
        }
        catch
        {
            Error = "Current exchange operations could not be loaded.";
        }
        finally { IsLoading = false; }
    }

    public void Dispose()
    {
        _connections.StateChanged -= OnConnectionChanged;
        _audit.LifecycleWritten -= OnLifecycleWritten;
    }

    private void Apply(IReadOnlyList<PositionSnapshot> positions, IReadOnlyList<OrderSnapshot> orders, IReadOnlyList<HistoricalOrder> history, IReadOnlyList<HistoricalExecution> fills, IReadOnlyList<AuditEvent> observed)
    {
        _dispatcher.Post(() =>
        {
            Positions.Clear();
            foreach (PositionSnapshot position in positions.OrderBy(item => item.Symbol, StringComparer.Ordinal))
            {
                decimal exposure = Math.Abs(position.Size * position.MarkPrice);
                decimal? liquidationDistance = position.LiquidationPrice is null || position.MarkPrice == 0m ? null : Math.Abs(position.MarkPrice - position.LiquidationPrice.Value) / position.MarkPrice * 100m;
                Positions.Add(new PositionOperationRow(position.Symbol, position.Side == TradeSide.Buy ? "Long" : "Short", position.Size, position.EntryPrice, position.MarkPrice, exposure, position.UnrealizedPnl, position.Leverage, liquidationDistance?.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + "%" ?? "Unknown", position.StopLoss is null && position.TakeProfit is null ? "Unprotected" : $"SL {position.StopLoss?.ToString() ?? "—"} · TP {position.TakeProfit?.ToString() ?? "—"}", _connections.Snapshot.StreamHealth == ServiceHealthState.Healthy ? "Stream observed" : "REST reconciled"));
            }
            Replace(Orders, orders.OrderByDescending(item => item.CreatedTimeUtc));
            Replace(OrderHistory, history.OrderByDescending(item => item.UpdatedTimeUtc));
            Replace(Fills, fills.OrderByDescending(item => item.TimestampUtc));
            Replace(ObservedPositionHistory, observed.OrderByDescending(item => item.TimestampUtc));
            LastRefresh = $"Updated {_timeProvider.GetUtcNow():HH:mm:ss} UTC";
            OnPropertyChanged(nameof(ProviderLabel)); OnPropertyChanged(nameof(GrossExposure)); OnPropertyChanged(nameof(NetExposure)); OnPropertyChanged(nameof(IsEmpty));
        });
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (T value in values) target.Add(value); }
    private void OnConnectionChanged(object? sender, ProviderConnectionSnapshot snapshot) => _ = RefreshAsync();
    private void OnLifecycleWritten(object? sender, TradingLifecycleEvent lifecycleEvent) { if (lifecycleEvent.Kind is TradingLifecycleKind.Position or TradingLifecycleKind.Order or TradingLifecycleKind.Execution or TradingLifecycleKind.Protection) _ = RefreshAsync(); }
    private void Select(string tab)
    {
        IsPositionsSelected = tab == "positions"; IsOrdersSelected = tab == "orders"; IsFillsSelected = tab == "fills"; IsProtectionSelected = tab == "protection";
    }
}
