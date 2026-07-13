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
    private readonly LiveActionConfirmationStore _liveStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly AuditLogService? _audit;
    private readonly CancellationTokenSource _timerCancellation = new();
    private int _disposed;

    [ObservableProperty] private PreparedOrder? _selectedOrder;
    [ObservableProperty] private LiveActionConfirmation? _selectedLiveAction;
    [ObservableProperty] private bool _isPlansSelected = true;
    [ObservableProperty] private bool _isLiveActionsSelected;
    [ObservableProperty] private string _actionMessage = "Approval records intent; execution remains MCP-only.";

    public ApprovalsViewModel(PreparedOrderStore store, LiveActionConfirmationStore liveStore, IUiDispatcher dispatcher, TimeProvider timeProvider, AuditLogService? audit = null)
    {
        _store = store;
        _liveStore = liveStore;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _audit = audit;
        _store.Changed += OnStoreChanged;
        _liveStore.Changed += OnLiveStoreChanged;
        Refresh();
        _ = RefreshTimerAsync();
    }

    public ObservableCollection<PreparedOrder> Orders { get; } = [];
    public ObservableCollection<LiveActionConfirmation> LiveActions { get; } = [];
    public bool IsEmpty => Orders.Count == 0;
    public bool LiveActionsEmpty => LiveActions.Count == 0;
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

    public bool HasLiveActionSelection => SelectedLiveAction is not null;
    public string SelectedLiveActionTitle => SelectedLiveAction is null ? "Select a Live action" : SelectedLiveAction.Request.Action switch { LiveActionType.CancelAllOrders => "Cancel all orders", _ => "Close position" };
    public string SelectedLiveActionState => SelectedLiveAction?.State.ToString() ?? "No selection";
    public string SelectedLiveActionExpiry => SelectedLiveAction is null ? "—" : SelectedLiveAction.State == LiveActionConfirmationState.Expired ? "Expired" : $"{Math.Max(0, (int)Math.Ceiling((SelectedLiveAction.ExpiresAtUtc - _timeProvider.GetUtcNow()).TotalSeconds))} seconds";
    public string SelectedLiveActionScope => SelectedLiveAction?.Request.Scope ?? "—";
    public string SelectedLiveActionSnapshot => SelectedLiveAction is null ? "—" : $"Symbol {SelectedLiveAction.Request.Symbol ?? "All USDT-linear"} · Quantity {SelectedLiveAction.Request.Quantity?.ToString("G29") ?? "Full/all"} · Matching at request {SelectedLiveAction.Request.CurrentMatchingCount}";
    public string SelectedLiveActionIds => SelectedLiveAction is null ? "—" : $"Confirmation {SelectedLiveAction.ConfirmationId:N}{Environment.NewLine}Request {SelectedLiveAction.ClientRequestId}{Environment.NewLine}Hash {SelectedLiveAction.ImmutableHash[..16]}…";
    public string SelectedLiveActionResult => SelectedLiveAction?.ResultMessage ?? "Not executed";

    partial void OnSelectedOrderChanged(PreparedOrder? value)
    {
        NotifySelected();
        ApproveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLiveActionChanged(LiveActionConfirmation? value)
    {
        NotifySelectedLiveAction();
        ApproveLiveActionCommand.NotifyCanExecuteChanged();
        RejectLiveActionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowPlans() { IsPlansSelected = true; IsLiveActionsSelected = false; }

    [RelayCommand]
    private void ShowLiveActions() { IsPlansSelected = false; IsLiveActionsSelected = true; }

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

    [RelayCommand(CanExecute = nameof(CanDecideLive))]
    private void ApproveLiveAction()
    {
        if (SelectedLiveAction is null) return;
        LiveActionConfirmationResult result = _liveStore.Approve(SelectedLiveAction.ConfirmationId, SelectedLiveAction.ImmutableHash);
        ActionMessage = result.Message;
        SelectedLiveAction = result.Confirmation;
        AuditLiveDecision(result.Confirmation, "live_action_approval", result.Code);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanDecideLive))]
    private void RejectLiveAction()
    {
        if (SelectedLiveAction is null) return;
        LiveActionConfirmationResult result = _liveStore.Reject(SelectedLiveAction.ConfirmationId, SelectedLiveAction.ImmutableHash);
        ActionMessage = result.Message;
        SelectedLiveAction = result.Confirmation;
        AuditLiveDecision(result.Confirmation, "live_action_rejection", result.Code);
        Refresh();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _store.Changed -= OnStoreChanged;
        _liveStore.Changed -= OnLiveStoreChanged;
        _timerCancellation.Cancel();
        _timerCancellation.Dispose();
    }

    private bool CanDecide() => SelectedOrder?.State == PreparedOrderState.PendingApproval && _timeProvider.GetUtcNow() < SelectedOrder.ExpiresAtUtc;
    private bool CanDecideLive() => SelectedLiveAction?.State == LiveActionConfirmationState.Pending && _timeProvider.GetUtcNow() < SelectedLiveAction.ExpiresAtUtc;
    private void OnStoreChanged(object? sender, PreparedOrder order) => _dispatcher.Post(Refresh);
    private void OnLiveStoreChanged(object? sender, LiveActionConfirmation confirmation) => _dispatcher.Post(Refresh);

    private void Refresh()
    {
        Guid? selectedId = SelectedOrder?.PreparationId;
        Guid? selectedLiveId = SelectedLiveAction?.ConfirmationId;
        Orders.Clear();
        foreach (PreparedOrder order in _store.GetAll()) Orders.Add(order);
        SelectedOrder = selectedId is null ? Orders.FirstOrDefault() : Orders.FirstOrDefault(order => order.PreparationId == selectedId) ?? Orders.FirstOrDefault();
        LiveActions.Clear();
        foreach (LiveActionConfirmation confirmation in _liveStore.GetAll()) LiveActions.Add(confirmation);
        SelectedLiveAction = selectedLiveId is null ? LiveActions.FirstOrDefault() : LiveActions.FirstOrDefault(item => item.ConfirmationId == selectedLiveId) ?? LiveActions.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(LiveActionsEmpty));
        NotifySelected();
        NotifySelectedLiveAction();
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
                    if (Orders.Count > 0 || LiveActions.Count > 0) Refresh();
                    else OnPropertyChanged(nameof(SelectedExpiry));
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private void NotifySelectedLiveAction()
    {
        OnPropertyChanged(nameof(HasLiveActionSelection)); OnPropertyChanged(nameof(SelectedLiveActionTitle));
        OnPropertyChanged(nameof(SelectedLiveActionState)); OnPropertyChanged(nameof(SelectedLiveActionExpiry));
        OnPropertyChanged(nameof(SelectedLiveActionScope)); OnPropertyChanged(nameof(SelectedLiveActionSnapshot));
        OnPropertyChanged(nameof(SelectedLiveActionIds)); OnPropertyChanged(nameof(SelectedLiveActionResult));
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

    private void AuditLiveDecision(LiveActionConfirmation? confirmation, string action, string result)
    {
        if (_audit is null || confirmation is null) return;
        _ = _audit.TryWriteAsync(_audit.Create("desktop", action, result, TradingEnvironment.Live, Guid.NewGuid().ToString("N"), confirmation.Request.Symbol, approvalState: confirmation.State.ToString()), CancellationToken.None);
    }

    private static string FormatNullable(decimal? value, string prefix, string fallback, string suffix = "") => value is null ? fallback : $"{prefix}{value:G29}{suffix}";
}
