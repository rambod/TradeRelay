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
    private IReadOnlyList<AuditEvent> _all = [];

    [ObservableProperty] private string _environmentFilter = "All";
    [ObservableProperty] private string _actionFilter = string.Empty;
    [ObservableProperty] private string _symbolFilter = string.Empty;
    [ObservableProperty] private string _resultFilter = "All";
    [ObservableProperty] private DateTimeOffset? _dateFilter;
    [ObservableProperty] private AuditEvent? _selectedEvent;
    [ObservableProperty] private string? _warning;

    public ActivityViewModel(AuditLogService audit, IUiDispatcher dispatcher, IDesktopShellService shell)
    {
        _audit = audit; _dispatcher = dispatcher; _shell = shell; _audit.EventWritten += OnEventWritten;
        _ = RefreshAsync();
    }

    public ObservableCollection<AuditEvent> Events { get; } = [];
    public IReadOnlyList<string> Environments { get; } = ["All", "Demo", "Live"];
    public IReadOnlyList<string> Results { get; } = ["All", "OK", "STARTED", "ACKNOWLEDGED", "DENIED", "FAILED", "UNKNOWN"];
    public bool IsEmpty => Events.Count == 0;
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var loaded = await _audit.LoadRecentAsync(CancellationToken.None).ConfigureAwait(false);
        _dispatcher.Post(() => { _all = loaded.Events; Warning = loaded.Warning; ApplyFilters(); });
    }

    [RelayCommand]
    private void ClearFilters() { EnvironmentFilter = "All"; ResultFilter = "All"; ActionFilter = string.Empty; SymbolFilter = string.Empty; DateFilter = null; }

    [RelayCommand]
    private void OpenAuditFolder()
    {
        if (!_shell.TryOpenFolder(_audit.DirectoryPath, out string? error)) Warning = error;
    }

    public void Dispose() => _audit.EventWritten -= OnEventWritten;
    private void OnEventWritten(object? sender, AuditEvent auditEvent) => _dispatcher.Post(() => { _all = [auditEvent, .. _all.Where(item => item.EventId != auditEvent.EventId)]; ApplyFilters(); });
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
