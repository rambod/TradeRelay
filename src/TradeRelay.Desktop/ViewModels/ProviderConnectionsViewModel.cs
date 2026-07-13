using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

internal sealed partial class ProviderCredentialFieldViewModel(CredentialFieldDescriptor descriptor) : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;
    public string Name => descriptor.Name;
    public string Label => descriptor.Label;
    public bool IsSecret => descriptor.IsSecret;
    public char PasswordChar => '●';
}

internal sealed partial class ProviderConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly IExchangeProviderRegistry _registry;
    private readonly IExchangeSessionCoordinator _sessions;
    private readonly IUiDispatcher _dispatcher;
    [ObservableProperty] private string _selectedExchange = "binance";
    [ObservableProperty] private bool _remember;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Binance and KuCoin adapters are Live account inspection only. They cannot trade.";

    public ProviderConnectionsViewModel(IExchangeProviderRegistry registry, IExchangeSessionCoordinator sessions, IUiDispatcher dispatcher)
    {
        _registry = registry; _sessions = sessions; _dispatcher = dispatcher;
        foreach (ExchangeProviderDescriptor descriptor in registry.Descriptors.Where(item => item.Id.Value is "binance" or "kucoin")) Exchanges.Add(descriptor.Id.Value);
        if (Exchanges.Count > 0) _selectedExchange = Exchanges[0];
        PopulateFields();
        _sessions.StateChanged += OnStateChanged;
    }

    public ObservableCollection<string> Exchanges { get; } = [];
    public ObservableCollection<ProviderCredentialFieldViewModel> CredentialFields { get; } = [];
    public ExchangeProviderDescriptor? Descriptor => _registry.Descriptors.FirstOrDefault(item => item.Id.Value == SelectedExchange);
    public string Capabilities => Descriptor is null ? "Unavailable" : $"{Descriptor.Capabilities} · Live only · No writes";
    public string RestHealth => Snapshot?.RestHealth.ToString() ?? "NotConfigured";
    public string StreamHealth => Snapshot?.StreamHealth.ToString() ?? "NotConfigured";
    public string SavedKeyPreview => Snapshot?.SavedKeyPreview ?? "None";
    private ProviderConnectionSnapshot? Snapshot => _sessions.Sessions.FirstOrDefault(item => item.Descriptor.Id.Value == SelectedExchange)?.Snapshot;

    partial void OnSelectedExchangeChanged(string value)
    {
        PopulateFields(); Remember = false;
        OnPropertyChanged(nameof(Descriptor)); OnPropertyChanged(nameof(Capabilities)); NotifySnapshot();
        _ = SelectAsync(value);
    }

    private async Task SelectAsync(string value)
    {
        try { await _sessions.SelectAsync(new ExchangeId(value), CancellationToken.None).ConfigureAwait(false); }
        catch (ProviderException exception) { _dispatcher.Post(() => Status = $"{exception.Code}: {exception.Message}"); }
        catch { _dispatcher.Post(() => Status = "SETTINGS_UNAVAILABLE: The selected exchange could not be saved."); }
    }
    partial void OnIsBusyChanged(bool value) { TestCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged(); }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task TestAsync(CancellationToken cancellationToken) => await RunAsync(save: false, cancellationToken).ConfigureAwait(false);

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SaveAsync(CancellationToken cancellationToken) => await RunAsync(save: true, cancellationToken).ConfigureAwait(false);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try { await _sessions.DeleteAsync(new ExchangeId(SelectedExchange), cancellationToken).ConfigureAwait(false); _dispatcher.Post(() => { foreach (ProviderCredentialFieldViewModel field in CredentialFields) field.Value = string.Empty; Remember = false; Status = "Credentials and the active read-only session were removed."; NotifySnapshot(); }); }
        finally { _dispatcher.Post(() => IsBusy = false); }
    }

    private async Task RunAsync(bool save, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            ExchangeCredentialSet credentials = BuildCredentials(); ExchangeId exchange = new(SelectedExchange);
            ExchangeConnectionResult result = save ? await _sessions.SaveAsync(exchange, credentials, Remember, cancellationToken).ConfigureAwait(false) : await _sessions.TestAsync(exchange, credentials, cancellationToken).ConfigureAwait(false);
            _dispatcher.Post(() => { Status = $"{result.Code}: {result.Message}"; if (result.Success && save) foreach (ProviderCredentialFieldViewModel field in CredentialFields.Where(item => item.IsSecret)) field.Value = string.Empty; NotifySnapshot(); });
        }
        catch (ArgumentException) { _dispatcher.Post(() => Status = "CREDENTIALS_INVALID: Complete every required credential field."); }
        catch (KeyNotFoundException) { _dispatcher.Post(() => Status = "CREDENTIALS_INVALID: Complete every required credential field."); }
        finally { _dispatcher.Post(() => IsBusy = false); }
    }

    private ExchangeCredentialSet BuildCredentials()
    {
        return new ExchangeCredentialSet(CredentialFields.ToDictionary(field => field.Name, field => field.Value, StringComparer.Ordinal));
    }
    private bool CanSubmit() => !IsBusy && Descriptor is not null;
    private bool CanDelete() => !IsBusy && Descriptor is not null;
    private void OnStateChanged(object? sender, ProviderSessionAccess session) { if (session.Descriptor.Id.Value == SelectedExchange) _dispatcher.Post(NotifySnapshot); }
    private void NotifySnapshot() { OnPropertyChanged(nameof(RestHealth)); OnPropertyChanged(nameof(StreamHealth)); OnPropertyChanged(nameof(SavedKeyPreview)); }
    private void PopulateFields() { CredentialFields.Clear(); foreach (CredentialFieldDescriptor field in Descriptor?.CredentialFields ?? []) CredentialFields.Add(new ProviderCredentialFieldViewModel(field)); }
    public void Dispose() => _sessions.StateChanged -= OnStateChanged;
}
