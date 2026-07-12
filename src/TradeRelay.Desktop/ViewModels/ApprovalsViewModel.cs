using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

internal sealed partial class ApprovalsViewModel : ObservableObject, IDisposable
{
    private readonly PreparedOrderStore _store;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly AuditLogService? _audit;
    private readonly CancellationTokenSource _timerCancellation = new();
    private int _disposed;

    [ObservableProperty] private PreparedOrder? _selectedOrder;
    [ObservableProperty] private string _actionMessage = "Approval records intent; execution remains MCP-only.";

    public ApprovalsViewModel(PreparedOrderStore store, IUiDispatcher dispatcher, TimeProvider timeProvider, AuditLogService? audit = null)
    {
        _store = store;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _audit = audit;
        _store.Changed += OnStoreChanged;
        Refresh();
        _ = RefreshTimerAsync();
    }

    public ObservableCollection<PreparedOrder> Orders { get; } = [];
    public bool IsEmpty => Orders.Count == 0;
    public bool HasSelection => SelectedOrder is not null;
    public string SelectedTitle => SelectedOrder is null ? "Select a plan" : $"{SelectedOrder.Order.Side} {SelectedOrder.Order.Symbol}";
    public string SelectedState => SelectedOrder?.State.ToString() ?? "No selection";
    public string SelectedEnvironment => SelectedOrder is null ? "—" : $"{SelectedOrder.Environment} Simulation";
    public string SelectedExpiry => SelectedOrder is null ? "—" : SelectedOrder.State == PreparedOrderState.Expired ? "Expired" : $"{Math.Max(0, (int)Math.Ceiling((SelectedOrder.ExpiresAtUtc - _timeProvider.GetUtcNow()).TotalSeconds))} seconds";
    public string SelectedRequested => SelectedOrder is null ? "—" : $"Qty {SelectedOrder.Order.RequestedQuantity} · Entry {SelectedOrder.Order.RequestedLimitPrice?.ToString() ?? "Market"}";
    public string SelectedNormalized => SelectedOrder is null ? "—" : $"Qty {SelectedOrder.Order.Quantity} · Entry {SelectedOrder.Order.EstimatedEntryPrice}";
    public string SelectedProtection => SelectedOrder is null ? "—" : $"Stop {SelectedOrder.Order.StopLoss?.ToString() ?? "None"} · Take profit {SelectedOrder.Order.TakeProfit?.ToString() ?? "None"}";
    public string SelectedRisk => SelectedOrder is null ? "—" : $"Notional ${SelectedOrder.Order.Risk.EstimatedNotionalUsd} · Risk {FormatNullable(SelectedOrder.Order.Risk.EstimatedRiskUsd, "$", "Unknown")} · Account {FormatNullable(SelectedOrder.Order.Risk.AccountRiskPercent, string.Empty, "Unknown", "%")}";
    public string SelectedIds => SelectedOrder is null ? "—" : $"Preparation {SelectedOrder.PreparationId:N}{Environment.NewLine}Client {SelectedOrder.ClientOrderId}{Environment.NewLine}Hash {SelectedOrder.ImmutableHash[..16]}…";
    public string SelectedWarnings => SelectedOrder is null || SelectedOrder.Warnings.Count == 0 ? "None" : string.Join(Environment.NewLine, SelectedOrder.Warnings.Select(warning => $"• {warning}"));
    public string SelectedExecution => SelectedOrder?.Submission is null ? "Not submitted" : $"{SelectedOrder.Submission.Status} · Filled {SelectedOrder.Submission.FilledQuantity}/{SelectedOrder.Submission.OriginalQuantity} · Remaining {SelectedOrder.Submission.RemainingQuantity} · Average {SelectedOrder.Submission.AverageFillPrice?.ToString() ?? "—"}{Environment.NewLine}{SelectedOrder.Submission.Message}";

    partial void OnSelectedOrderChanged(PreparedOrder? value)
    {
        NotifySelected();
        ApproveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDecide))]
    private void Approve()
    {
        if (SelectedOrder is null) return;
        PreparedOrderResult result = _store.Approve(SelectedOrder.PreparationId, SelectedOrder.ImmutableHash);
        ActionMessage = result.Message;
        SelectedOrder = result.Order;
        AuditDecision(result.Order, "approval", result.Code);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanDecide))]
    private void Reject()
    {
        if (SelectedOrder is null) return;
        PreparedOrderResult result = _store.Reject(SelectedOrder.PreparationId, SelectedOrder.ImmutableHash);
        ActionMessage = result.Message;
        SelectedOrder = result.Order;
        AuditDecision(result.Order, "rejection", result.Code);
        Refresh();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _store.Changed -= OnStoreChanged;
        _timerCancellation.Cancel();
        _timerCancellation.Dispose();
    }

    private bool CanDecide() => SelectedOrder?.State == PreparedOrderState.PendingApproval && _timeProvider.GetUtcNow() < SelectedOrder.ExpiresAtUtc;
    private void OnStoreChanged(object? sender, PreparedOrder order) => _dispatcher.Post(Refresh);

    private void Refresh()
    {
        Guid? selectedId = SelectedOrder?.PreparationId;
        Orders.Clear();
        foreach (PreparedOrder order in _store.GetAll()) Orders.Add(order);
        SelectedOrder = selectedId is null ? Orders.FirstOrDefault() : Orders.FirstOrDefault(order => order.PreparationId == selectedId) ?? Orders.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        NotifySelected();
    }

    private async Task RefreshTimerAsync()
    {
        try
        {
            while (!_timerCancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, _timerCancellation.Token);
                _dispatcher.Post(() =>
                {
                    if (Orders.Count > 0) Refresh();
                    else OnPropertyChanged(nameof(SelectedExpiry));
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private void NotifySelected()
    {
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectedTitle)); OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(SelectedEnvironment)); OnPropertyChanged(nameof(SelectedExpiry)); OnPropertyChanged(nameof(SelectedRequested));
        OnPropertyChanged(nameof(SelectedNormalized)); OnPropertyChanged(nameof(SelectedProtection)); OnPropertyChanged(nameof(SelectedRisk));
        OnPropertyChanged(nameof(SelectedIds)); OnPropertyChanged(nameof(SelectedWarnings));
        OnPropertyChanged(nameof(SelectedExecution));
    }

    private void AuditDecision(PreparedOrder? order, string action, string result)
    {
        if (_audit is null || order is null) return;
        _ = _audit.TryWriteAsync(_audit.Create("desktop", action, result, order.Environment, Guid.NewGuid().ToString("N"), order.Order.Symbol, order.PreparationId, order.ClientOrderId, approvalState: order.State.ToString()), CancellationToken.None);
    }

    private static string FormatNullable(decimal? value, string prefix, string fallback, string suffix = "") => value is null ? fallback : $"{prefix}{value:G29}{suffix}";
}
