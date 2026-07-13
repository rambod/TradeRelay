using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Core.Risk;
using TradeRelay.Desktop.Mcp;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

/// <summary>
/// Coordinates the desktop dashboard, credentials, and local MCP server controls.
/// </summary>
internal sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ActionMessageDuration = TimeSpan.FromSeconds(3);

    private readonly AppSettings _settings;
    private readonly LocalMcpServerHost _serverHost;
    private readonly LocalMcpTokenService _tokenService;
    private readonly ExchangeConnectionManager _connectionManager;
    private readonly IClipboardService _clipboardService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly PreparedOrderStore _preparedOrderStore;
    private readonly LiveActionConfirmationStore _liveConfirmations;
    private readonly TradingControlService _tradingControl;
    private readonly AuditLogService _auditLog;
    private CancellationTokenSource? _messageClearCancellation;
    private McpServerSnapshot _serverSnapshot;
    private ProviderConnectionSnapshot _providerSnapshot;

    [ObservableProperty] private bool _isDashboardSelected = true;
    [ObservableProperty] private bool _isCredentialsSelected;
    [ObservableProperty] private bool _isRiskSelected;
    [ObservableProperty] private bool _isApprovalsSelected;
    [ObservableProperty] private bool _isActivitySelected;
    [ObservableProperty] private bool _tradingAcknowledged;
    [ObservableProperty] private bool _isLiveEnableDialogOpen;
    [ObservableProperty] private string _liveConfirmationText = string.Empty;
    [ObservableProperty] private TradingEnvironment _selectedEnvironment;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _apiSecret = string.Empty;
    [ObservableProperty] private bool _rememberCredentials;
    [ObservableProperty] private string _credentialActionStatus = "Enter Bybit Demo credentials to test the read-only connection.";
    [ObservableProperty] private string _permissionSummary = "Not connected";
    [ObservableProperty] private string _credentialWarnings = "None";

    [ObservableProperty]
    private bool _isTokenRevealed;

    [ObservableProperty]
    private string? _actionMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    public MainWindowViewModel(
        AppSettings settings,
        LocalMcpServerHost serverHost,
        LocalMcpTokenService tokenService,
        ExchangeConnectionManager connectionManager,
        PreparedOrderStore preparedOrderStore,
        LiveActionConfirmationStore liveConfirmations,
        RiskViewModel risk,
        ApprovalsViewModel approvals,
        ActivityViewModel activity,
        TradingControlService tradingControl,
        AuditLogService auditLog,
        ApplicationMetadata metadata,
        IClipboardService clipboardService,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _preparedOrderStore = preparedOrderStore ?? throw new ArgumentNullException(nameof(preparedOrderStore));
        _liveConfirmations = liveConfirmations ?? throw new ArgumentNullException(nameof(liveConfirmations));
        _tradingControl = tradingControl ?? throw new ArgumentNullException(nameof(tradingControl));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        ArgumentNullException.ThrowIfNull(metadata);

        AppVersion = metadata.Version;
        _serverSnapshot = serverHost.Snapshot;
        _providerSnapshot = connectionManager.Snapshot;
        _selectedEnvironment = settings.Bybit.Environment;
        _rememberCredentials = settings.Bybit.RememberCredentials;
        _serverHost.StateChanged += OnServerStateChanged;
        _connectionManager.StateChanged += OnProviderStateChanged;
        _preparedOrderStore.Changed += OnPreparedOrderChanged;
        _liveConfirmations.Changed += OnLiveConfirmationChanged;
        _tradingControl.StateChanged += OnTradingStateChanged;
        Risk = risk;
        Approvals = approvals;
        Activity = activity;
    }

    /// <summary>
    /// Gets the application name.
    /// </summary>
    public string ApplicationName => "TradeRelay";

    /// <summary>
    /// Gets the current product version.
    /// </summary>
    public string AppVersion { get; }

    /// <summary>
    /// Gets the version label displayed in the application header.
    /// </summary>
    public string VersionLabel => $"v{AppVersion}";

    /// <summary>
    /// Gets the current development milestone label.
    /// </summary>
    public string DevelopmentStatus => "Milestone 6 · Session-gated Bybit Demo and Live execution";

    public RiskViewModel Risk { get; }
    public ApprovalsViewModel Approvals { get; }
    public ActivityViewModel Activity { get; }

    public IReadOnlyList<TradingEnvironment> Environments { get; } = Enum.GetValues<TradingEnvironment>();

    /// <summary>
    /// Gets the current server snapshot.
    /// </summary>
    public McpServerSnapshot ServerSnapshot
    {
        get => _serverSnapshot;
        private set => SetProperty(ref _serverSnapshot, value);
    }

    /// <summary>
    /// Gets the current server-state label.
    /// </summary>
    public string ServerState => ServerSnapshot.State.ToString();

    /// <summary>
    /// Gets the configured or active MCP endpoint.
    /// </summary>
    public string Endpoint => ServerSnapshot.Url;

    /// <summary>
    /// Gets the configured or active MCP port.
    /// </summary>
    public string Port => ServerSnapshot.Port.ToString();

    /// <summary>
    /// Gets the MCP authentication status.
    /// </summary>
    public string AuthenticationStatus => ServerSnapshot.AuthenticationEnabled ? "Enabled" : "Disabled";

    /// <summary>
    /// Gets the session-count status for the stateless transport.
    /// </summary>
    public string SessionStatus => ServerSnapshot.ConnectedSessionCount?.ToString() ?? "Not tracked (stateless)";

    /// <summary>
    /// Gets the most recent safe server error.
    /// </summary>
    public string LastError => ServerSnapshot.LastError ?? _providerSnapshot.LastError ?? "None";

    /// <summary>
    /// Gets the selected environment label.
    /// </summary>
    public string EnvironmentStatus => _providerSnapshot.Environment.ToString();

    /// <summary>
    /// Gets the startup trading access mode.
    /// </summary>
    public string AccessStatus => (_tradingControl.Snapshot.Enabled ? TradingAccessMode.TradingEnabled : TradingAccessMode.TradingDisabled).ToString();

    public string TradingState => _tradingControl.Snapshot.StateLabel;
    public string TradingDetail => _tradingControl.Snapshot.LastError ?? $"{SelectedEnvironment} write tools are available for this session.";
    public bool IsTradingEnabled => _tradingControl.Snapshot.Enabled;
    public bool IsTradingDisabled => !_tradingControl.Snapshot.Enabled;
    public bool IsDemoTradingEnabled => IsDemoEnvironment && IsTradingEnabled;
    public bool IsDemoTradingDisabled => IsDemoEnvironment && IsTradingDisabled;
    public bool IsLiveEnvironment => SelectedEnvironment == TradingEnvironment.Live;
    public bool IsDemoEnvironment => SelectedEnvironment == TradingEnvironment.Demo;
    public bool ShowDemoEnable => IsDemoEnvironment && IsTradingDisabled;
    public bool ShowLiveEnable => IsLiveEnvironment && IsTradingDisabled;
    public string EnvironmentBadge => IsLiveEnvironment ? "LIVE" : "DEMO";
    public string HeaderSafetyState => $"{SelectedEnvironment.ToString().ToUpperInvariant()} · {(IsTradingEnabled ? "TRADING ENABLED" : "TRADING DISABLED")}";
    public string LiveRiskSummary => $"Risk/trade {_settings.Risk.MaxRiskPerTradePercent}% · Notional ${_settings.Risk.MaxOrderNotionalUsd} · Positions {_settings.Risk.MaxOpenPositions} · Leverage {_settings.Risk.MaxLeverage}× · Market drift {_settings.Risk.MaxMarketPriceDeviationPercent}% · Manual approval {(_settings.Risk.RequireManualApprovalForLive ? "required" : "disabled by operator")}";
    public string LiveEnableWarnings => _providerSnapshot.CredentialInfo?.Warnings.Count > 0 ? string.Join(Environment.NewLine, _providerSnapshot.CredentialInfo.Warnings.Select(item => $"• {item}")) : "No additional credential warnings reported.";

    /// <summary>
    /// Gets the live-trading session status.
    /// </summary>
    public string LiveTradingStatus => IsLiveEnvironment ? (IsTradingEnabled ? "Enabled for this session" : "Disabled") : "Not selected";

    /// <summary>
    /// Gets the selected provider status.
    /// </summary>
    public string ProviderStatus => $"Bybit · {_providerSnapshot.RestHealth}";

    /// <summary>
    /// Gets the current open-position count.
    /// </summary>
    public string OpenPositionCount => _providerSnapshot.OpenPositionCount.ToString();

    /// <summary>
    /// Gets the current open-order count.
    /// </summary>
    public string OpenOrderCount => _providerSnapshot.OpenOrderCount.ToString();

    /// <summary>
    /// Gets the current pending-approval count.
    /// </summary>
    public string PendingApprovalCount => (_preparedOrderStore.GetPending().Count + _liveConfirmations.GetPending().Count).ToString();

    public string RestHealth => _providerSnapshot.RestHealth.ToString();
    public string StreamHealth => _providerSnapshot.StreamHealth.ToString();
    public string SavedKeyPreview => _providerSnapshot.SavedKeyPreview ?? "None";
    public string CredentialStorageStatus => _connectionManager.Snapshot.CredentialLoaded
        ? (_settings.Bybit.RememberCredentials ? "Protected on this device" : "Session only")
        : (_connectionManager.Snapshot.CredentialInfo is null ? "Not saved" : "Session only");

    /// <summary>
    /// Gets the bearer token or its masked representation.
    public string DisplayedToken => IsTokenRevealed
        ? _tokenService.CurrentToken
        : LocalMcpTokenService.MaskedToken;

    /// <summary>
    /// Gets the label for the token visibility action.
    /// </summary>
    public string TokenVisibilityAction => IsTokenRevealed ? "Hide token" : "Reveal token";

    partial void OnIsTokenRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedToken));
        OnPropertyChanged(nameof(TokenVisibilityAction));
    }

    partial void OnSelectedEnvironmentChanged(TradingEnvironment value)
    {
        IsLiveEnableDialogOpen = false;
        LiveConfirmationText = string.Empty;
        NotifyEnvironmentState();
        if (value != _connectionManager.Snapshot.Environment) _ = ChangeEnvironmentSafeAsync(value);
    }

    partial void OnLiveConfirmationTextChanged(string value) => ConfirmLiveEnableCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void ShowDashboard() => SelectPage(dashboard: true);

    [RelayCommand]
    private void ShowCredentials() => SelectPage(credentials: true);

    [RelayCommand]
    private void ShowRisk() => SelectPage(risk: true);

    [RelayCommand]
    private void ShowApprovals() => SelectPage(approvals: true);

    [RelayCommand]
    private void ShowActivity() => SelectPage(activity: true);

    [RelayCommand]
    private async Task EnableDemoTradingAsync(CancellationToken cancellationToken)
    {
        if (!_auditLog.Health.Healthy) { ShowActionMessage("Activity auditing is unavailable; Demo trading cannot be enabled."); return; }
        TradingGateResult result = await _tradingControl.EnableAsync(ServerSnapshot.State == McpServerState.Running, TradingAcknowledged, null, cancellationToken);
        if (result.Allowed)
        {
            bool audited = await _auditLog.TryWriteAsync(_auditLog.Create("desktop", "demo_trading_enabled", "OK", TradingEnvironment.Demo, ToolResponse.NewCorrelationId()), cancellationToken);
            if (!audited) { _tradingControl.Disable("Activity auditing failed; Demo trading was disabled."); result = new(false, "AUDIT_UNAVAILABLE", "Activity auditing failed; Demo trading was disabled."); }
        }
        ShowActionMessage(result.Message);
    }

    [RelayCommand]
    private void OpenLiveEnableDialog()
    {
        LiveConfirmationText = string.Empty;
        IsLiveEnableDialogOpen = true;
    }

    [RelayCommand]
    private void CancelLiveEnable()
    {
        LiveConfirmationText = string.Empty;
        IsLiveEnableDialogOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmLiveEnable))]
    private async Task ConfirmLiveEnableAsync(CancellationToken cancellationToken)
    {
        TradingGateResult result = await _tradingControl.EnableAsync(ServerSnapshot.State == McpServerState.Running, false, LiveConfirmationText, cancellationToken);
        if (result.Allowed)
        {
            bool audited = await _auditLog.TryWriteAsync(_auditLog.Create("desktop", "live_trading_enabled", "OK", TradingEnvironment.Live, ToolResponse.NewCorrelationId(), providerResult: LiveRiskSummary), cancellationToken);
            if (!audited)
            {
                _tradingControl.Disable("Activity auditing failed; Live trading was disabled.", emergency: true);
                result = new(false, "AUDIT_UNAVAILABLE", "Activity auditing failed; Live trading was disabled.");
            }
        }
        if (result.Allowed) CancelLiveEnable();
        ShowActionMessage(result.Message);
    }

    [RelayCommand]
    private async Task DisableTradingAsync(CancellationToken cancellationToken)
    {
        TradingEnvironment environment = SelectedEnvironment;
        _tradingControl.Disable($"{environment} trading was disabled by the desktop operator.", emergency: true);
        await _auditLog.TryWriteAsync(_auditLog.Create("desktop", "trading_emergency_disabled", "OK", environment, ToolResponse.NewCorrelationId()), cancellationToken);
        TradingAcknowledged = false;
        ShowActionMessage($"{environment} trading disabled. Existing exchange orders and positions were not changed.");
    }

    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        CredentialActionStatus = "Testing Bybit connection…";
        ExchangeConnectionResult result = await _connectionManager.TestAsync(SelectedEnvironment, ApiKey, ApiSecret, cancellationToken);
        ApplyConnectionResult(result);
    }

    [RelayCommand]
    private async Task SaveCredentialsAsync(CancellationToken cancellationToken)
    {
        CredentialActionStatus = "Validating and saving credentials…";
        ExchangeConnectionResult result = await _connectionManager.SaveAsync(SelectedEnvironment, ApiKey, ApiSecret, RememberCredentials, cancellationToken);
        ApplyConnectionResult(result);
        if (result.Success) { ApiKey = string.Empty; ApiSecret = string.Empty; }
    }

    [RelayCommand]
    private async Task DeleteCredentialsAsync(CancellationToken cancellationToken)
    {
        await _connectionManager.DeleteAsync(cancellationToken);
        ApiKey = string.Empty; ApiSecret = string.Empty; RememberCredentials = false;
        PermissionSummary = "Not connected"; CredentialWarnings = "None";
        CredentialActionStatus = "Credentials deleted and the Bybit connection was closed.";
    }

    [RelayCommand(CanExecute = nameof(CanStartServer))]
    private async Task StartServerAsync(CancellationToken cancellationToken)
    {
        await _serverHost.StartServerAsync(cancellationToken);
        ShowActionMessage(_serverHost.Snapshot.State == McpServerState.Running
            ? "Local MCP server started."
            : _serverHost.Snapshot.LastError ?? "The local MCP server could not start.");
    }

    [RelayCommand(CanExecute = nameof(CanStopServer))]
    private async Task StopServerAsync(CancellationToken cancellationToken)
    {
        await _serverHost.StopServerAsync(cancellationToken);
        ShowActionMessage(_serverHost.Snapshot.State == McpServerState.Stopped
            ? "Local MCP server stopped."
            : _serverHost.Snapshot.LastError ?? "The local MCP server could not stop cleanly.");
    }

    [RelayCommand]
    private void ToggleTokenVisibility()
    {
        IsTokenRevealed = !IsTokenRevealed;
    }

    [RelayCommand]
    private async Task RotateTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenService.RotateAsync(cancellationToken);
        OnPropertyChanged(nameof(DisplayedToken));
        ShowActionMessage("MCP token rotated. Previous token invalidated.");
    }

    [RelayCommand]
    private Task CopyTokenAsync(CancellationToken cancellationToken) =>
        CopyAsync(_tokenService.CurrentToken, "MCP token copied.", cancellationToken);

    [RelayCommand]
    private Task CopyCodexConfigAsync(CancellationToken cancellationToken) =>
        CopyAsync(
            ClientConfigurationTemplates.CreateCodex(Endpoint),
            "Codex configuration copied.",
            cancellationToken);

    [RelayCommand]
    private Task CopyClaudeCodeAsync(CancellationToken cancellationToken) =>
        CopyAsync(
            ClientConfigurationTemplates.CreateClaudeCodeCommand(Endpoint),
            "Claude Code command copied.",
            cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _serverHost.StateChanged -= OnServerStateChanged;
        _connectionManager.StateChanged -= OnProviderStateChanged;
        _preparedOrderStore.Changed -= OnPreparedOrderChanged;
        _liveConfirmations.Changed -= OnLiveConfirmationChanged;
        _tradingControl.StateChanged -= OnTradingStateChanged;
        Approvals.Dispose();
        Activity.Dispose();
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref _messageClearCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private bool CanStartServer() =>
        ServerSnapshot.State is McpServerState.Stopped or McpServerState.Faulted;

    private bool CanStopServer() =>
        ServerSnapshot.State is McpServerState.Starting or McpServerState.Running or McpServerState.Faulted;

    private async Task CopyAsync(string value, string successMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _clipboardService.SetTextAsync(value, cancellationToken);
            ShowActionMessage(successMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            ShowActionMessage("The requested text could not be copied.");
        }
    }

    private void OnServerStateChanged(object? sender, McpServerSnapshot snapshot) =>
        _uiDispatcher.Post(() => ApplyServerSnapshot(snapshot));

    private void OnProviderStateChanged(object? sender, ProviderConnectionSnapshot snapshot) =>
        _uiDispatcher.Post(() => ApplyProviderSnapshot(snapshot));

    private void OnPreparedOrderChanged(object? sender, PreparedOrder order) =>
        _uiDispatcher.Post(() => OnPropertyChanged(nameof(PendingApprovalCount)));

    private void OnLiveConfirmationChanged(object? sender, LiveActionConfirmation confirmation) =>
        _uiDispatcher.Post(() => OnPropertyChanged(nameof(PendingApprovalCount)));

    private void OnTradingStateChanged(object? sender, TradingSessionSnapshot snapshot) => _uiDispatcher.Post(NotifyTradingState);

    private void SelectPage(bool dashboard = false, bool credentials = false, bool risk = false, bool approvals = false, bool activity = false)
    {
        IsDashboardSelected = dashboard;
        IsCredentialsSelected = credentials;
        IsRiskSelected = risk;
        IsApprovalsSelected = approvals;
        IsActivitySelected = activity;
    }

    private void NotifyTradingState()
    {
        OnPropertyChanged(nameof(AccessStatus)); OnPropertyChanged(nameof(TradingState)); OnPropertyChanged(nameof(TradingDetail));
        OnPropertyChanged(nameof(IsTradingEnabled)); OnPropertyChanged(nameof(IsTradingDisabled)); OnPropertyChanged(nameof(HeaderSafetyState));
        OnPropertyChanged(nameof(IsDemoTradingEnabled)); OnPropertyChanged(nameof(IsDemoTradingDisabled));
        OnPropertyChanged(nameof(ShowDemoEnable)); OnPropertyChanged(nameof(ShowLiveEnable));
        OnPropertyChanged(nameof(LiveTradingStatus));
    }

    private void ApplyProviderSnapshot(ProviderConnectionSnapshot snapshot)
    {
        _providerSnapshot = snapshot;
        OnPropertyChanged(nameof(EnvironmentStatus)); OnPropertyChanged(nameof(ProviderStatus));
        OnPropertyChanged(nameof(OpenPositionCount)); OnPropertyChanged(nameof(OpenOrderCount));
        OnPropertyChanged(nameof(RestHealth)); OnPropertyChanged(nameof(StreamHealth));
        OnPropertyChanged(nameof(SavedKeyPreview)); OnPropertyChanged(nameof(CredentialStorageStatus));
        OnPropertyChanged(nameof(LiveEnableWarnings));
        OnPropertyChanged(nameof(LastError));
        if (snapshot.CredentialInfo is not null)
        {
            PermissionSummary = BuildPermissionSummary(snapshot.CredentialInfo);
            CredentialWarnings = snapshot.CredentialInfo.Warnings.Count == 0 ? "None" : string.Join(Environment.NewLine, snapshot.CredentialInfo.Warnings.Select(x => $"• {x}"));
        }
    }

    private void ApplyConnectionResult(ExchangeConnectionResult result)
    {
        CredentialActionStatus = result.Message;
        if (result.CredentialInfo is not null)
        {
            PermissionSummary = BuildPermissionSummary(result.CredentialInfo);
            CredentialWarnings = result.CredentialInfo.Warnings.Count == 0 ? "None" : string.Join(Environment.NewLine, result.CredentialInfo.Warnings.Select(x => $"• {x}"));
        }
    }

    private async Task ChangeEnvironmentSafeAsync(TradingEnvironment environment)
    {
        try { await _connectionManager.ChangeEnvironmentAsync(environment, CancellationToken.None); CredentialActionStatus = $"Switched to {environment}. Previous connection closed."; }
        catch { CredentialActionStatus = "The environment could not be changed."; }
    }

    private static string BuildPermissionSummary(ApiCredentialInfo info) =>
        $"{info.Summary} · Trading: {(info.HasTradingPermission ? "Yes" : "No")} · Wallet: {(info.HasWalletPermission ? "Yes" : "No")} · Withdrawal: {(info.HasWithdrawalPermission ? "Detected" : "No")} · IP-bound: {(info.IsIpBound ? "Yes" : "No")} · {(info.IsMasterAccount ? "Master account" : "Subaccount")}";

    private bool CanConfirmLiveEnable() => string.Equals(LiveConfirmationText, TradingControlService.LiveConfirmationPhrase, StringComparison.Ordinal);

    private void NotifyEnvironmentState()
    {
        OnPropertyChanged(nameof(IsLiveEnvironment)); OnPropertyChanged(nameof(IsDemoEnvironment));
        OnPropertyChanged(nameof(ShowDemoEnable)); OnPropertyChanged(nameof(ShowLiveEnable));
        OnPropertyChanged(nameof(EnvironmentBadge)); OnPropertyChanged(nameof(HeaderSafetyState));
        OnPropertyChanged(nameof(LiveTradingStatus)); OnPropertyChanged(nameof(LiveRiskSummary));
        OnPropertyChanged(nameof(TradingState)); OnPropertyChanged(nameof(TradingDetail));
    }

    private void ApplyServerSnapshot(McpServerSnapshot snapshot)
    {
        ServerSnapshot = snapshot;
        OnPropertyChanged(nameof(ServerState));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(AuthenticationStatus));
        OnPropertyChanged(nameof(SessionStatus));
        OnPropertyChanged(nameof(LastError));
        StartServerCommand.NotifyCanExecuteChanged();
        StopServerCommand.NotifyCanExecuteChanged();
    }

    private void ShowActionMessage(string message)
    {
        var current = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(
            ref _messageClearCancellation,
            current);
        previous?.Cancel();
        previous?.Dispose();

        ActionMessage = message;
        _ = ClearActionMessageAsync(current);
    }

    private async Task ClearActionMessageAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(ActionMessageDuration, _timeProvider, cancellation.Token);
            _uiDispatcher.Post(() =>
            {
                if (ReferenceEquals(_messageClearCancellation, cancellation))
                {
                    ActionMessage = null;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }
}
