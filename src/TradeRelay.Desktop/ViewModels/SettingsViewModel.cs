using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

internal sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly LocalMcpServerHost _serverHost;
    private readonly LocalMcpTokenService _tokenService;
    private readonly ApplicationDataPaths _paths;
    private readonly IDesktopShellService _shell;
    private readonly IDiagnosticsExporter _diagnostics;
    private readonly IUiDispatcher _dispatcher;
    private int _savedPort;
    private bool _savedStartAutomatically;

    [ObservableProperty] private string _mcpPort;
    [ObservableProperty] private bool _startMcpAutomatically;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string _statusMessage = "Settings are saved locally and never contain credentials or bearer tokens.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private McpServerState _serverState;

    public SettingsViewModel(
        AppSettings settings,
        ApplicationSettingsStore settingsStore,
        LocalMcpServerHost serverHost,
        LocalMcpTokenService tokenService,
        ApplicationDataPaths paths,
        ApplicationMetadata metadata,
        IDesktopShellService shell,
        IDiagnosticsExporter diagnostics,
        IUiDispatcher dispatcher)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _serverHost = serverHost;
        _tokenService = tokenService;
        _paths = paths;
        _shell = shell;
        _diagnostics = diagnostics;
        _dispatcher = dispatcher;
        _savedPort = settings.Server.Port;
        _savedStartAutomatically = settings.Server.StartAutomatically;
        _mcpPort = _savedPort.ToString(CultureInfo.InvariantCulture);
        _startMcpAutomatically = _savedStartAutomatically;
        _serverState = serverHost.Snapshot.State;
        Version = metadata.Version;
        License = metadata.License;
        Repository = metadata.Repository;
        SupportedPlatforms = metadata.Platforms;
        serverHost.StateChanged += OnServerStateChanged;
        Validate();
    }

    public string Version { get; }
    public string License { get; }
    public string Repository { get; }
    public string SupportedPlatforms { get; }
    public string ApplicationDataPath => _paths.Root;
    public string EndpointPreview => int.TryParse(McpPort, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1024 and <= 65535
        ? $"http://127.0.0.1:{port}/mcp"
        : "Enter a valid port to preview the endpoint.";
    public bool IsPortEditingEnabled => ServerState is McpServerState.Stopped or McpServerState.Faulted;
    public bool HasValidationError => ValidationError is not null;
    public bool IsDirty => McpPort != _savedPort.ToString(CultureInfo.InvariantCulture) || StartMcpAutomatically != _savedStartAutomatically;
    public bool IsPortDirty => McpPort != _savedPort.ToString(CultureInfo.InvariantCulture);
    public string DirtyState => IsDirty ? "Unsaved changes" : "All changes saved";

    partial void OnMcpPortChanged(string value) => OnEdited();
    partial void OnStartMcpAutomaticallyChanged(bool value) => OnEdited();
    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Validate();
        if (!CanSave()) return;

        IsBusy = true;
        try
        {
            int port = int.Parse(McpPort, CultureInfo.InvariantCulture);
            _settings.Server.Port = port;
            _settings.Server.StartAutomatically = StartMcpAutomatically;
            await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
            _serverHost.TryUpdateConfiguredPort(port);
            _savedPort = port;
            _savedStartAutomatically = StartMcpAutomatically;
            _dispatcher.Post(() =>
            {
                StatusMessage = "Settings saved. The endpoint will use these values on the next MCP start.";
                NotifyState();
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _dispatcher.Post(() => StatusMessage = "Settings could not be saved. Existing runtime safety state was not changed.");
        }
        finally
        {
            _dispatcher.Post(() => IsBusy = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard()
    {
        McpPort = _savedPort.ToString(CultureInfo.InvariantCulture);
        StartMcpAutomatically = _savedStartAutomatically;
        StatusMessage = "Unsaved changes discarded.";
        Validate();
    }

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private async Task RotateTokenAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await _tokenService.RotateAsync(cancellationToken).ConfigureAwait(false);
            _dispatcher.Post(() => StatusMessage = "MCP token rotated. The previous token is invalid immediately.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _dispatcher.Post(() => StatusMessage = "The MCP token could not be rotated safely.");
        }
        finally
        {
            _dispatcher.Post(() => IsBusy = false);
        }
    }

    [RelayCommand]
    private void OpenApplicationDataFolder() => OpenFolder(_paths.Root);

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(_paths.LogsDirectory);

    [RelayCommand(CanExecute = nameof(CanRunAction))]
    private async Task ExportDiagnosticsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            DiagnosticsExportResult result = await _diagnostics.ExportAsync(cancellationToken).ConfigureAwait(false);
            _dispatcher.Post(() => StatusMessage = result.Success && result.FilePath is not null
                ? $"Diagnostics exported to {result.FilePath}"
                : result.Message);
        }
        finally
        {
            _dispatcher.Post(() => IsBusy = false);
        }
    }

    public void Dispose() => _serverHost.StateChanged -= OnServerStateChanged;

    private bool CanSave() => !IsBusy && IsDirty && ValidationError is null && (!IsPortDirty || IsPortEditingEnabled);
    private bool CanDiscard() => !IsBusy && IsDirty;
    private bool CanRunAction() => !IsBusy;

    private void OnEdited()
    {
        Validate();
        NotifyState();
    }

    private void Validate()
    {
        ValidationError = !int.TryParse(McpPort, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1024 or > 65535
            ? "MCP port must be a whole number from 1024 through 65535."
            : null;
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(EndpointPreview));
    }

    private void OpenFolder(string path)
    {
        StatusMessage = _shell.TryOpenFolder(path, out string? error)
            ? $"Opened {path}"
            : error ?? "The folder could not be opened.";
    }

    private void OnServerStateChanged(object? sender, McpServerSnapshot snapshot) => _dispatcher.Post(() =>
    {
        ServerState = snapshot.State;
        OnPropertyChanged(nameof(IsPortEditingEnabled));
        NotifyCommands();
    });

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsPortDirty));
        OnPropertyChanged(nameof(DirtyState));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        RotateTokenCommand.NotifyCanExecuteChanged();
        ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    }
}
