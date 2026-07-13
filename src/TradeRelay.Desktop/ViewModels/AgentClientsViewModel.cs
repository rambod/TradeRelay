using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

internal sealed partial class AgentClientsViewModel : ObservableObject, IDisposable
{
    private readonly AgentClientInstaller _installer;
    private readonly OAuthPairingService _oauth;
    private readonly LocalMcpServerHost _server;
    private readonly IUiDispatcher _dispatcher;

    [ObservableProperty] private AgentClientStatus? _selectedClient;
    [ObservableProperty] private OAuthPairingSnapshot? _selectedPairing;
    [ObservableProperty] private OAuthClientSnapshot? _selectedOAuthClient;
    [ObservableProperty] private bool _grantTradeScope;
    [ObservableProperty] private bool _installationAcknowledged;
    [ObservableProperty] private string _preview = "Select a client and preview every command and target before installation.";
    [ObservableProperty] private string _status = "New clients pair with Read & Plan scopes. Trade scope requires explicit desktop approval.";
    [ObservableProperty] private bool _isBusy;

    public AgentClientsViewModel(AgentClientInstaller installer, OAuthPairingService oauth, LocalMcpServerHost server, IUiDispatcher dispatcher)
    {
        _installer = installer; _oauth = oauth; _server = server; _dispatcher = dispatcher;
        _oauth.PairingChanged += OnPairingChanged; _oauth.ClientsChanged += OnClientsChanged;
        RefreshOAuth();
        _ = DetectAsync();
    }

    public ObservableCollection<AgentClientStatus> Clients { get; } = [];
    public ObservableCollection<OAuthPairingSnapshot> PendingPairings { get; } = [];
    public ObservableCollection<OAuthClientSnapshot> PairedClients { get; } = [];
    public bool HasPendingPairing => PendingPairings.Count > 0;
    public bool CanApprovePairing => SelectedPairing?.State == OAuthPairingState.Pending;

    partial void OnSelectedClientChanged(AgentClientStatus? value) { InstallationAcknowledged = false; Preview = value is null ? "Select a client." : BuildPreview(_installer.PreviewInstall(value.Kind, _server.Snapshot.Url)); NotifyCommands(); }
    partial void OnSelectedPairingChanged(OAuthPairingSnapshot? value) { GrantTradeScope = false; OnPropertyChanged(nameof(CanApprovePairing)); ApprovePairingCommand.NotifyCanExecuteChanged(); RejectPairingCommand.NotifyCanExecuteChanged(); }
    partial void OnInstallationAcknowledgedChanged(bool value) => NotifyCommands();
    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    [RelayCommand]
    private async Task DetectAsync()
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<AgentClientStatus> detected = await _installer.DetectAsync(CancellationToken.None).ConfigureAwait(false);
            _dispatcher.Post(() => { Clients.Clear(); foreach (AgentClientStatus client in detected) Clients.Add(client); SelectedClient ??= Clients.FirstOrDefault(); });
        }
        finally { _dispatcher.Post(() => IsBusy = false); }
    }

    [RelayCommand]
    private void PreviewInstall()
    {
        if (SelectedClient is null) return;
        Preview = BuildPreview(_installer.PreviewInstall(SelectedClient.Kind, _server.Snapshot.Url));
        InstallationAcknowledged = false;
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        if (SelectedClient is null) return;
        IsBusy = true;
        try
        {
            ClientInstallationResult result = await _installer.InstallAsync(SelectedClient.Kind, _server.Snapshot.Url, cancellationToken).ConfigureAwait(false);
            _dispatcher.Post(() => { Status = result.Message; InstallationAcknowledged = false; });
            await DetectAsync().ConfigureAwait(false);
        }
        finally { _dispatcher.Post(() => IsBusy = false); }
    }

    [RelayCommand(CanExecute = nameof(CanSelectClient))]
    private async Task UninstallAsync(CancellationToken cancellationToken)
    {
        if (SelectedClient is null) return;
        IsBusy = true;
        try
        {
            ClientInstallationResult result = await _installer.UninstallAsync(SelectedClient.Kind, cancellationToken).ConfigureAwait(false);
            _dispatcher.Post(() => Status = result.Message);
            await DetectAsync().ConfigureAwait(false);
        }
        finally { _dispatcher.Post(() => IsBusy = false); }
    }

    [RelayCommand(CanExecute = nameof(CanApprovePairing))]
    private void ApprovePairing()
    {
        if (SelectedPairing is null) return;
        Status = _oauth.Approve(SelectedPairing.PairingId, GrantTradeScope) ? $"{SelectedPairing.ClientName} pairing approved with {(GrantTradeScope ? "Read, Plan & Trade" : "Read & Plan")} scopes." : "The pairing request expired or changed.";
        RefreshOAuth();
    }

    [RelayCommand(CanExecute = nameof(CanApprovePairing))]
    private void RejectPairing()
    {
        if (SelectedPairing is null) return;
        Status = _oauth.Reject(SelectedPairing.PairingId) ? "Pairing rejected." : "The pairing request expired or changed.";
        RefreshOAuth();
    }

    [RelayCommand]
    private async Task RevokeClientAsync(CancellationToken cancellationToken)
    {
        if (SelectedOAuthClient is null) return;
        bool revoked = await _oauth.RevokeClientAsync(SelectedOAuthClient.ClientId, cancellationToken).ConfigureAwait(false);
        _dispatcher.Post(() => { Status = revoked ? "Client tokens revoked. Re-pair to grant access again." : "The client was not found."; RefreshOAuth(); });
    }

    public void Dispose() { _oauth.PairingChanged -= OnPairingChanged; _oauth.ClientsChanged -= OnClientsChanged; }

    private bool CanInstall() => !IsBusy && SelectedClient is not null && InstallationAcknowledged && SelectedClient.State is AgentClientInstallationState.Available or AgentClientInstallationState.Installed;
    private bool CanSelectClient() => !IsBusy && SelectedClient is not null;
    private void NotifyCommands() { InstallCommand.NotifyCanExecuteChanged(); UninstallCommand.NotifyCanExecuteChanged(); }
    private void OnPairingChanged(object? sender, OAuthPairingSnapshot snapshot) => _dispatcher.Post(RefreshOAuth);
    private void OnClientsChanged(object? sender, EventArgs eventArgs) => _dispatcher.Post(RefreshOAuth);
    private void RefreshOAuth()
    {
        PendingPairings.Clear(); foreach (OAuthPairingSnapshot pairing in _oauth.GetPendingPairings()) PendingPairings.Add(pairing);
        PairedClients.Clear(); foreach (OAuthClientSnapshot client in _oauth.GetClients()) PairedClients.Add(client);
        SelectedPairing = PendingPairings.FirstOrDefault(); SelectedOAuthClient = PairedClients.FirstOrDefault(item => !item.Revoked) ?? PairedClients.FirstOrDefault();
        OnPropertyChanged(nameof(HasPendingPairing));
    }
    private static string BuildPreview(ClientInstallationPreview preview) => $"Commands:{Environment.NewLine}{string.Join(Environment.NewLine, preview.Commands.Select(command => $"• {command.Executable} {string.Join(' ', command.Arguments.Select(Quote))}"))}{Environment.NewLine}{Environment.NewLine}Targets:{Environment.NewLine}{string.Join(Environment.NewLine, preview.Targets.Select(target => $"• {target}"))}";
    private static string Quote(string value) => value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
}
