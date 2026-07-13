using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Security;

namespace TradeRelay.Desktop.Services;

internal sealed class ExchangeConnectionManager(
    AppSettings settings,
    ApplicationSettingsStore settingsStore,
    CredentialStoreCoordinator credentialStore,
    IExchangeProviderFactory providerFactory,
    ILogger<ExchangeConnectionManager> logger) : IHostedService
{
    private static readonly ExchangeId BybitId = new("bybit");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ExchangeProviderSettings _providerSettings = settings.GetExchange(BybitId);
    private readonly IMarketDataProvider _marketData = providerFactory.CreateMarketDataProvider(settings.GetExchange(BybitId).Environment);
    private IExchangeProviderConnection? _connection;
    private ProviderConnectionSnapshot _snapshot = Empty(settings.GetExchange(BybitId).Environment);

    public event EventHandler<ProviderConnectionSnapshot>? StateChanged;
    public ProviderConnectionSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public IMarketDataProvider MarketData => _marketData;
    public ITradingAccountProvider? Account => Volatile.Read(ref _connection)?.Account;
    public IExchangeTradingProvider? Trading => Volatile.Read(ref _connection)?.Trading;
    public IExchangeStream? Stream => Volatile.Read(ref _connection)?.Stream;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_providerSettings.ShouldRemember(_providerSettings.Environment)) return;
        ExchangeCredentialSet? credentials = await credentialStore.LoadAsync(CredentialId(_providerSettings.Environment), true, cancellationToken).ConfigureAwait(false);
        if (credentials is not null) await ConnectAndKeepAsync(credentials, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        if (_marketData is IDisposable disposable) disposable.Dispose();
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (Account is not null) return;
        ExchangeCredentialSet? credentials = await credentialStore.LoadAsync(CredentialId(_providerSettings.Environment), _providerSettings.ShouldRemember(_providerSettings.Environment), cancellationToken).ConfigureAwait(false);
        if (credentials is not null) await ConnectAndKeepAsync(credentials, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExchangeConnectionResult> TestAsync(TradingEnvironment environment, string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        try
        {
            var credentials = new ExchangeCredentials(apiKey, apiSecret);
            await using IExchangeProviderConnection connection = providerFactory.CreateConnection(environment, credentials);
            ApiCredentialInfo info = await connection.Account.GetCredentialInfoAsync(cancellationToken).ConfigureAwait(false);
            if (info.HasWithdrawalPermission) return Rejected(info);
            ServiceHealthState stream = await TryConnectStreamAsync(connection.Stream, cancellationToken).ConfigureAwait(false);
            return new ExchangeConnectionResult(true, "OK", stream == ServiceHealthState.Healthy ? "Bybit credentials and private stream are valid." : "Bybit credentials are valid; the private stream is currently unavailable.", info, ServiceHealthState.Healthy, stream);
        }
        catch (ArgumentException)
        {
            return Failed("CREDENTIALS_INVALID", "Enter both a valid API key and API secret.");
        }
        catch (ProviderException exception)
        {
            return Failed(exception.Code, exception.Message);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Bybit connection test failed with {ErrorType}", exception.GetType().Name);
            return Failed("PROVIDER_UNAVAILABLE", "Bybit could not be reached.");
        }
    }

    public async Task<ExchangeConnectionResult> SaveAsync(TradingEnvironment environment, string apiKey, string apiSecret, bool remember, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credentials = new ExchangeCredentials(apiKey, apiSecret);
            IExchangeProviderConnection candidate = providerFactory.CreateConnection(environment, credentials);
            try
            {
                ApiCredentialInfo info = await candidate.Account.GetCredentialInfoAsync(cancellationToken).ConfigureAwait(false);
                if (info.HasWithdrawalPermission) { await candidate.DisposeAsync().ConfigureAwait(false); return Rejected(info); }
                ServiceHealthState stream = await TryConnectStreamAsync(candidate.Stream, cancellationToken).ConfigureAwait(false);
                int positionCount = (await candidate.Account.GetPositionsAsync(null, cancellationToken).ConfigureAwait(false)).Count;
                int orderCount = (await candidate.Account.GetOpenOrdersAsync(null, cancellationToken).ConfigureAwait(false)).Count;
                await DisposeConnectionAsync().ConfigureAwait(false);

                bool persisted = remember;
                string? storageWarning = null;
                try { await credentialStore.SaveAsync(CredentialId(environment), credentials, remember, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (remember && exception is not OperationCanceledException)
                {
                    persisted = false;
                    storageWarning = "Protected storage is unavailable; credentials are session-only.";
                }

                _providerSettings.Environment = environment;
                _providerSettings.SetRemember(environment, persisted);
                await settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
                _connection = candidate;
                candidate = null!;
                SetSnapshot(new ProviderConnectionSnapshot(providerFactory.ProviderName, environment, ServiceHealthState.Healthy, stream, true, info.Summary, Mask(apiKey), info, positionCount, orderCount, storageWarning, Guid.NewGuid()));
                return new ExchangeConnectionResult(true, "OK", storageWarning ?? "Bybit credentials were validated and saved.", info, ServiceHealthState.Healthy, stream);
            }
            finally
            {
                if (candidate is not null) await candidate.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (ArgumentException) { return Failed("CREDENTIALS_INVALID", "Enter both a valid API key and API secret."); }
        catch (ProviderException exception) { return Failed(exception.Code, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Saving Bybit credentials failed with {ErrorType}", exception.GetType().Name);
            return Failed("PROVIDER_UNAVAILABLE", "The credentials could not be validated or stored.");
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeConnectionAsync().ConfigureAwait(false);
            await credentialStore.DeleteAsync(CredentialId(_providerSettings.Environment), cancellationToken).ConfigureAwait(false);
            _providerSettings.SetRemember(_providerSettings.Environment, false);
            await settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            SetSnapshot(Empty(_providerSettings.Environment));
        }
        finally { _gate.Release(); }
    }

    public async Task ChangeEnvironmentAsync(TradingEnvironment environment, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeConnectionAsync().ConfigureAwait(false);
            _providerSettings.Environment = environment;
            await settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            SetSnapshot(Empty(environment));
            if (_providerSettings.ShouldRemember(environment))
            {
                ExchangeCredentialSet? credentials = await credentialStore.LoadAsync(CredentialId(environment), true, cancellationToken).ConfigureAwait(false);
                if (credentials is not null) await ConnectAndKeepCoreAsync(credentials, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) => DeleteConnectionOnlyAsync(cancellationToken);

    private async Task DeleteConnectionOnlyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await DisposeConnectionAsync().ConfigureAwait(false); SetSnapshot(Empty(_providerSettings.Environment)); }
        finally { _gate.Release(); }
    }

    private async Task ConnectAndKeepAsync(ExchangeCredentialSet credentials, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ConnectAndKeepCoreAsync(credentials, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task ConnectAndKeepCoreAsync(ExchangeCredentialSet credentials, CancellationToken cancellationToken)
    {
        IExchangeProviderConnection connection = providerFactory.CreateConnection(_providerSettings.Environment, credentials);
        try
        {
            ApiCredentialInfo info = await connection.Account.GetCredentialInfoAsync(cancellationToken).ConfigureAwait(false);
            if (info.HasWithdrawalPermission) { SetSnapshot(Empty(_providerSettings.Environment) with { LastError = "Saved credentials have withdrawal permission and were rejected." }); return; }
            ServiceHealthState stream = await TryConnectStreamAsync(connection.Stream, cancellationToken).ConfigureAwait(false);
            int positions = (await connection.Account.GetPositionsAsync(null, cancellationToken).ConfigureAwait(false)).Count;
            int orders = (await connection.Account.GetOpenOrdersAsync(null, cancellationToken).ConfigureAwait(false)).Count;
            _connection = connection;
            connection = null!;
            SetSnapshot(new ProviderConnectionSnapshot(providerFactory.ProviderName, _providerSettings.Environment, ServiceHealthState.Healthy, stream, true, info.Summary, Mask(credentials[ExchangeCredentials.ApiKeyField]), info, positions, orders, null, Guid.NewGuid()));
        }
        catch (Exception exception)
        {
            logger.LogWarning("Loading saved Bybit credentials failed with {ErrorType}", exception.GetType().Name);
            SetSnapshot(Empty(_providerSettings.Environment) with { RestHealth = ServiceHealthState.Unavailable, LastError = "Saved credentials could not connect to Bybit." });
        }
        finally { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); }
    }

    private static async Task<ServiceHealthState> TryConnectStreamAsync(IExchangeStream stream, CancellationToken cancellationToken)
    {
        try { await stream.ConnectAsync(cancellationToken).ConfigureAwait(false); return stream.IsConnected ? ServiceHealthState.Healthy : ServiceHealthState.Degraded; }
        catch (OperationCanceledException) { throw; }
        catch { return ServiceHealthState.Degraded; }
    }

    private async Task DisposeConnectionAsync()
    {
        IExchangeProviderConnection? connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void SetSnapshot(ProviderConnectionSnapshot snapshot) { Volatile.Write(ref _snapshot, snapshot); StateChanged?.Invoke(this, snapshot); }
    private static string CredentialId(TradingEnvironment environment) => new ExchangeProfileKey(BybitId, environment).CredentialId;
    private static string Mask(string key) => key.Length <= 4 ? "••••" : $"••••••{key[^4..]}";
    private static ProviderConnectionSnapshot Empty(TradingEnvironment environment) => new("Bybit", environment, ServiceHealthState.NotConfigured, ServiceHealthState.NotConfigured, false, "None", null, null, 0, 0, null, Guid.NewGuid());
    private static ExchangeConnectionResult Failed(string code, string message) => new(false, code, message, null, ServiceHealthState.Unavailable, ServiceHealthState.NotConfigured);
    private static ExchangeConnectionResult Rejected(ApiCredentialInfo info) => new(false, "UNSAFE_API_PERMISSION", "The API key has withdrawal permission and was rejected.", info, ServiceHealthState.Healthy, ServiceHealthState.NotConfigured);
}
