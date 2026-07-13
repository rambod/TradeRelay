using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

internal sealed partial class ActivityViewModel : ObservableObject, IDisposable
{
    private readonly AuditLogService _audit;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDesktopShellService _shell;
    private readonly SafeLogService? _safeLog;
    private IReadOnlyList<AuditEvent> _all = [];

    [ObservableProperty] private string _environmentFilter = "All";
    [ObservableProperty] private string _actionFilter = string.Empty;
    [ObservableProperty] private string _symbolFilter = string.Empty;
    [ObservableProperty] private string _resultFilter = "All";
    [ObservableProperty] private DateTimeOffset? _dateFilter;
    [ObservableProperty] private AuditEvent? _selectedEvent;
    [ObservableProperty] private string? _warning;
    [ObservableProperty] private bool _isErrorCenterSelected;

    public ActivityViewModel(AuditLogService audit, IUiDispatcher dispatcher, IDesktopShellService shell, SafeLogService? safeLog = null)
    {
        _audit = audit; _dispatcher = dispatcher; _shell = shell; _safeLog = safeLog; _audit.EventWritten += OnEventWritten;
        if (_safeLog is not null) _safeLog.ErrorWritten += OnErrorWritten;
        _ = RefreshAsync();
    }

    public ObservableCollection<AuditEvent> Events { get; } = [];
    public ObservableCollection<RuntimeErrorSummary> RuntimeErrors { get; } = [];
    public IReadOnlyList<string> Environments { get; } = ["All", "Demo", "Live"];
    public IReadOnlyList<string> Results { get; } = ["All", "OK", "STARTED", "ACKNOWLEDGED", "DENIED", "FAILED", "UNKNOWN"];
    public bool IsEmpty => Events.Count == 0;
    public bool IsActivitySelected => !IsErrorCenterSelected;
    public bool IsErrorCenterEmpty => RuntimeErrors.Count == 0;
    public bool HasSelection => SelectedEvent is not null;
    public string SelectedTitle => SelectedEvent is null ? "Select an activity" : $"{SelectedEvent.Action} · {SelectedEvent.Result}";
    public string SelectedIds => SelectedEvent is null ? "—" : $"Correlation {SelectedEvent.CorrelationId}{Environment.NewLine}Preparation {SelectedEvent.PreparationId?.ToString("N") ?? "—"}{Environment.NewLine}Client {SelectedEvent.ClientOrderId ?? "—"}{Environment.NewLine}Exchange {SelectedEvent.ExchangeOrderId ?? "—"}";
    public string SelectedDetails => SelectedEvent is null ? "—" : $"Approval {SelectedEvent.ApprovalState ?? "—"}{Environment.NewLine}Risk {SelectedEvent.RiskSummary ?? "—"}{Environment.NewLine}Provider {SelectedEvent.ProviderResult ?? "—"}{Environment.NewLine}Final state {SelectedEvent.FinalStatus ?? "—"}{Environment.NewLine}Error {SelectedEvent.ErrorCode ?? "None"}";

    partial void OnEnvironmentFilterChanged(string value) => ApplyFilters();
    partial void OnActionFilterChanged(string value) => ApplyFilters();
    partial void OnSymbolFilterChanged(string value) => ApplyFilters();
    partial void OnResultFilterChanged(string value) => ApplyFilters();
    partial void OnDateFilterChanged(DateTimeOffset? value) => ApplyFilters();
    partial void OnSelectedEventChanged(AuditEvent? value) { OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectedTitle)); OnPropertyChanged(nameof(SelectedIds)); OnPropertyChanged(nameof(SelectedDetails)); }
    partial void OnIsErrorCenterSelectedChanged(bool value) => OnPropertyChanged(nameof(IsActivitySelected));

    [RelayCommand]
    private void ShowActivity() => IsErrorCenterSelected = false;

    [RelayCommand]
    private void ShowErrorCenter() => IsErrorCenterSelected = true;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var loaded = await _audit.LoadRecentAsync(CancellationToken.None).ConfigureAwait(false);
        _dispatcher.Post(() => { _all = loaded.Events; Warning = loaded.Warning; ApplyFilters(); RefreshErrors(); });
    }

    [RelayCommand]
    private void ClearFilters() { EnvironmentFilter = "All"; ResultFilter = "All"; ActionFilter = string.Empty; SymbolFilter = string.Empty; DateFilter = null; }

    [RelayCommand]
    private void OpenAuditFolder()
    {
        if (!_shell.TryOpenFolder(_audit.DirectoryPath, out string? error)) Warning = error;
    }

    public void Dispose()
    {
        _audit.EventWritten -= OnEventWritten;
        if (_safeLog is not null) _safeLog.ErrorWritten -= OnErrorWritten;
    }
    private void OnEventWritten(object? sender, AuditEvent auditEvent) => _dispatcher.Post(() => { _all = [auditEvent, .. _all.Where(item => item.EventId != auditEvent.EventId)]; ApplyFilters(); });
    private void OnErrorWritten(object? sender, SafeDiagnosticError error) => _dispatcher.Post(RefreshErrors);
    private void RefreshErrors()
    {
        RuntimeErrors.Clear();
        if (_safeLog is null) { OnPropertyChanged(nameof(IsErrorCenterEmpty)); return; }
        foreach (IGrouping<string, SafeDiagnosticError> group in _safeLog.GetRecentErrors(100).GroupBy(item => $"{item.Code}|{item.Category}|{item.ExceptionType}", StringComparer.Ordinal))
        {
            SafeDiagnosticError[] entries = group.OrderBy(item => item.TimestampUtc).ToArray();
            SafeDiagnosticError latest = entries[^1];
            string provider = latest.Properties.GetValueOrDefault("provider") ?? "TradeRelay";
            string action = latest.Properties.GetValueOrDefault("action") ?? latest.Category;
            string[] correlations = entries.Select(item => item.Properties.GetValueOrDefault("correlationId")).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().Distinct(StringComparer.Ordinal).Take(10).ToArray();
            string[] related = entries.SelectMany(item => new[] { item.Properties.GetValueOrDefault("orderId"), item.Properties.GetValueOrDefault("positionId") }).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().Distinct(StringComparer.Ordinal).Take(10).ToArray();
            RuntimeErrors.Add(new RuntimeErrorSummary(latest.Code, latest.Category, provider, action, latest.ExceptionType, entries.Length, entries[0].TimestampUtc, latest.TimestampUtc, correlations, related, Guidance(latest.Code)));
        }
        OnPropertyChanged(nameof(IsErrorCenterEmpty));
    }

    private static string Guidance(string code) => code switch
    {
        "MCP_START_FAILED" => "Check the configured loopback port, stop the conflicting process, then start MCP again.",
        "AUDIT_UNAVAILABLE" => "Restore write access to the application-data audit folder. Trading remains blocked until audit health recovers.",
        "RECONCILIATION_UNAVAILABLE" => "Check provider REST and private-stream health, then refresh Operations before any further write.",
        _ => "Review the affected provider and action state, then verify current exchange status before retrying.",
    };
    private void ApplyFilters()
    {
        IEnumerable<AuditEvent> query = _all;
        if (EnvironmentFilter != "All" && Enum.TryParse(EnvironmentFilter, out TradingEnvironment environment)) query = query.Where(item => item.Environment == environment);
        if (ResultFilter != "All") query = query.Where(item => item.Result.Equals(ResultFilter, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(ActionFilter)) query = query.Where(item => item.Action.Contains(ActionFilter.Trim(), StringComparison.OrdinalIgnoreCase) || item.Tool.Contains(ActionFilter.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(SymbolFilter)) query = query.Where(item => item.Symbol?.Contains(SymbolFilter.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        if (DateFilter is not null) query = query.Where(item => item.TimestampUtc.UtcDateTime.Date == DateFilter.Value.UtcDateTime.Date);
        Events.Clear(); foreach (AuditEvent item in query.OrderByDescending(item => item.TimestampUtc)) Events.Add(item);
        SelectedEvent = SelectedEvent is null ? Events.FirstOrDefault() : Events.FirstOrDefault(item => item.EventId == SelectedEvent.EventId) ?? Events.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }
}
