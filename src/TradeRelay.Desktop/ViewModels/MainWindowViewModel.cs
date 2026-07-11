using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Mcp;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

/// <summary>
/// Coordinates the Milestone 2 desktop dashboard and local MCP server controls.
/// </summary>
internal sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ActionMessageDuration = TimeSpan.FromSeconds(3);

    private readonly AppSettings _settings;
    private readonly LocalMcpServerHost _serverHost;
    private readonly LocalMcpTokenService _tokenService;
    private readonly IClipboardService _clipboardService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _messageClearCancellation;
    private McpServerSnapshot _serverSnapshot;

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
        ApplicationMetadata metadata,
        IClipboardService clipboardService,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(metadata);

        AppVersion = metadata.Version;
        _serverSnapshot = serverHost.Snapshot;
        _serverHost.StateChanged += OnServerStateChanged;
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
    public string DevelopmentStatus => "Milestone 2 · Local MCP control";

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
    public string LastError => ServerSnapshot.LastError ?? "None";

    /// <summary>
    /// Gets the selected environment label.
    /// </summary>
    public string EnvironmentStatus => _settings.Bybit.Environment.ToString();

    /// <summary>
    /// Gets the startup trading access mode.
    /// </summary>
    public string AccessStatus => TradingAccessMode.ReadOnly.ToString();

    /// <summary>
    /// Gets the live-trading session status.
    /// </summary>
    public string LiveTradingStatus => "Disabled";

    /// <summary>
    /// Gets the selected provider status.
    /// </summary>
    public string ProviderStatus => "Bybit · Not configured";

    /// <summary>
    /// Gets the current open-position count.
    /// </summary>
    public string OpenPositionCount => "0";

    /// <summary>
    /// Gets the current open-order count.
    /// </summary>
    public string OpenOrderCount => "0";

    /// <summary>
    /// Gets the current pending-approval count.
    /// </summary>
    public string PendingApprovalCount => "0";

    /// <summary>
    /// Gets the bearer token or its masked representation.
    /// </summary>
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
    private void RotateToken()
    {
        _tokenService.Rotate();
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
