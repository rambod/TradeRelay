using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Security;

namespace TradeRelay.Desktop.Services;

internal sealed class ExchangeSessionCoordinator : IExchangeSessionCoordinator, IHostedService
{
    private static readonly ExchangeId BybitId = new("bybit");
    private readonly ExchangeConnectionManager _bybit;
    private readonly IExchangeProviderRegistry _registry;
    private readonly AppSettings _settings;
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly CredentialStoreCoordinator _credentials;
    private readonly ILogger<ExchangeSessionCoordinator> _logger;
    private readonly ConcurrentDictionary<ExchangeId, ReadSession> _sessions = new();
    private readonly ConcurrentDictionary<ExchangeId, SemaphoreSlim> _gates = new();

    public ExchangeSessionCoordinator(ExchangeConnectionManager bybit, IExchangeProviderRegistry registry, AppSettings settings, ApplicationSettingsStore settingsStore, CredentialStoreCoordinator credentials, ILogger<ExchangeSessionCoordinator> logger)
    {
        _bybit = bybit; _registry = registry; _settings = settings; _settingsStore = settingsStore; _credentials = credentials; _logger = logger;
        _bybit.StateChanged += OnBybitStateChanged;
        foreach (ExchangeProviderDescriptor descriptor in registry.Descriptors.Where(item => item.Id != BybitId))
        {
            if (!registry.TryGetFactory(descriptor.Id, out IExchangeProviderFactory? factory) || factory is null) continue;
            TradingEnvironment environment = descriptor.Environments.Contains(TradingEnvironment.Live) ? TradingEnvironment.Live : descriptor.Environments[0];
            IMarketDataProvider market = factory.CreateMarketDataProvider(environment);
            _sessions[descriptor.Id] = new ReadSession(descriptor, environment, market, null, Empty(descriptor, environment));
        }
    }

    public event EventHandler<ProviderSessionAccess>? StateChanged;

    public IReadOnlyList<ExchangeProfileKey> ConnectedProfiles => Sessions.Where(item => item.Account is not null).Select(item => new ExchangeProfileKey(item.Descriptor.Id, item.Environment)).ToArray();

    public IReadOnlyList<ProviderSessionAccess> Sessions
    {
        get
        {
            var values = new List<ProviderSessionAccess> { BybitAccess() };
            values.AddRange(_sessions.Values.Select(Access));
            return values.OrderBy(item => item.Descriptor.DisplayName, StringComparer.Ordinal).ToArray();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (ReadSession session in _sessions.Values)
        {
            ExchangeProviderSettings settings = _settings.GetExchange(session.Descriptor.Id);
            settings.Environment = TradingEnvironment.Live;
            if (!settings.ShouldRemember(TradingEnvironment.Live)) continue;
            ExchangeCredentialSet? credential = await _credentials.LoadAsync(new ExchangeProfileKey(session.Descriptor.Id, TradingEnvironment.Live).CredentialId, true, cancellationToken).ConfigureAwait(false);
            if (credential is not null) await ConnectAsync(session.Descriptor.Id, credential, persist: false, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _bybit.StateChanged -= OnBybitStateChanged;
        foreach (ReadSession session in _sessions.Values)
        {
            if (session.Connection is not null) await session.Connection.DisposeAsync().ConfigureAwait(false);
            if (session.MarketData is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (session.MarketData is IDisposable disposable) disposable.Dispose();
        }
    }

    public bool TryResolve(string? exchange, out ProviderSessionAccess? session, out string code, out string message)
    {
        session = null; code = "OK"; message = string.Empty;
        string value = string.IsNullOrWhiteSpace(exchange) ? _settings.SelectedExchange : exchange.Trim();
        if (string.IsNullOrWhiteSpace(value)) { code = "AMBIGUOUS_EXCHANGE"; message = "Select an exchange or pass the exchange parameter."; return false; }
        ExchangeId id;
        try { id = new ExchangeId(value); }
        catch (ArgumentException) { code = "EXCHANGE_NOT_FOUND"; message = "The requested exchange is not registered."; return false; }
        if (id == BybitId) { session = BybitAccess(); return true; }
        if (!_sessions.TryGetValue(id, out ReadSession? read)) { code = "EXCHANGE_NOT_FOUND"; message = "The requested exchange is not registered."; return false; }
        session = Access(read); return true;
    }

    public async Task<ExchangeConnectionResult> TestAsync(ExchangeId exchange, ExchangeCredentialSet credentials, CancellationToken cancellationToken)
    {
        if (exchange == BybitId) return await _bybit.TestAsync(_bybit.Snapshot.Environment, credentials[ExchangeCredentials.ApiKeyField], credentials[ExchangeCredentials.ApiSecretField], cancellationToken).ConfigureAwait(false);
        if (!_sessions.TryGetValue(exchange, out ReadSession? session) || !_registry.TryGetFactory(exchange, out IExchangeProviderFactory? factory) || factory is null) return Failed("EXCHANGE_NOT_FOUND", "The requested exchange is not registered.");
        try
        {
            await using IExchangeProviderConnection connection = factory.CreateConnection(session.Environment, credentials);
            ApiCredentialInfo info = await connection.Account.GetCredentialInfoAsync(cancellationToken).ConfigureAwait(false);
            ServiceHealthState stream = await TryStreamAsync(connection.Stream, cancellationToken).ConfigureAwait(false);
            return new(true, "OK", $"{session.Descriptor.DisplayName} credentials are valid. This adapter is read-only.", info, ServiceHealthState.Healthy, stream);
        }
        catch (ProviderException exception) { return Failed(exception.Code, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { _logger.LogWarning("{Provider} connection test failed with {ErrorType}", session.Descriptor.Id, exception.GetType().Name); return Failed("PROVIDER_UNAVAILABLE", $"{session.Descriptor.DisplayName} could not be reached."); }
    }

    public Task<ExchangeConnectionResult> SaveAsync(ExchangeId exchange, ExchangeCredentialSet credentials, bool remember, CancellationToken cancellationToken)
    {
        if (exchange == BybitId) return _bybit.SaveAsync(_bybit.Snapshot.Environment, credentials[ExchangeCredentials.ApiKeyField], credentials[ExchangeCredentials.ApiSecretField], remember, cancellationToken);
        return ConnectAsync(exchange, credentials, remember, cancellationToken);
    }

    public async Task DeleteAsync(ExchangeId exchange, CancellationToken cancellationToken)
    {
        if (exchange == BybitId) { await _bybit.DeleteAsync(cancellationToken).ConfigureAwait(false); return; }
        if (!_sessions.TryGetValue(exchange, out ReadSession? session)) return;
        SemaphoreSlim gate = _gates.GetOrAdd(exchange, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (session.Connection is not null) await session.Connection.DisposeAsync().ConfigureAwait(false);
            await _credentials.DeleteAsync(new ExchangeProfileKey(exchange, session.Environment).CredentialId, cancellationToken).ConfigureAwait(false);
            _settings.GetExchange(exchange).SetRemember(session.Environment, false); await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
            Update(session with { Connection = null, Snapshot = Empty(session.Descriptor, session.Environment) });
        }
        finally { gate.Release(); }
    }

    public async Task SelectAsync(ExchangeId exchange, CancellationToken cancellationToken)
    {
        if (!_registry.Descriptors.Any(item => item.Id == exchange)) throw new ProviderException("EXCHANGE_NOT_FOUND", "The requested exchange is not registered.");
        _settings.SelectedExchange = exchange.Value; await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
        if (TryResolve(exchange.Value, out ProviderSessionAccess? session, out _, out _) && session is not null) StateChanged?.Invoke(this, session);
    }

    private async Task<ExchangeConnectionResult> ConnectAsync(ExchangeId exchange, ExchangeCredentialSet credentials, bool persist, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(exchange, out ReadSession? session) || !_registry.TryGetFactory(exchange, out IExchangeProviderFactory? factory) || factory is null) return Failed("EXCHANGE_NOT_FOUND", "The requested exchange is not registered.");
        SemaphoreSlim gate = _gates.GetOrAdd(exchange, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IExchangeProviderConnection? candidate = null;
        try
        {
            candidate = factory.CreateConnection(session.Environment, credentials);
            ApiCredentialInfo info = await candidate.Account.GetCredentialInfoAsync(cancellationToken).ConfigureAwait(false);
            ServiceHealthState stream = await TryStreamAsync(candidate.Stream, cancellationToken).ConfigureAwait(false);
            int positions = (await candidate.Account.GetPositionsAsync(null, cancellationToken).ConfigureAwait(false)).Count;
            int orders = (await candidate.Account.GetOpenOrdersAsync(null, cancellationToken).ConfigureAwait(false)).Count;
            if (session.Connection is not null) await session.Connection.DisposeAsync().ConfigureAwait(false);
            bool remembered = persist; string? warning = null;
            try { await _credentials.SaveAsync(new ExchangeProfileKey(exchange, session.Environment).CredentialId, credentials, persist, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (persist && exception is not OperationCanceledException) { remembered = false; warning = "Protected storage is unavailable; credentials are session-only."; }
            ExchangeProviderSettings providerSettings = _settings.GetExchange(exchange); providerSettings.Environment = session.Environment; providerSettings.SetRemember(session.Environment, remembered); _settings.SelectedExchange = exchange.Value; await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
            var snapshot = new ProviderConnectionSnapshot(session.Descriptor.DisplayName, session.Environment, ServiceHealthState.Healthy, stream, true, info.Summary, Mask(credentials[ExchangeCredentials.ApiKeyField]), info, positions, orders, warning, Guid.NewGuid());
            Update(session with { Connection = candidate, Snapshot = snapshot }); candidate = null;
            return new(true, "OK", warning ?? $"{session.Descriptor.DisplayName} connected in read-only adapter mode.", info, ServiceHealthState.Healthy, stream);
        }
        catch (ProviderException exception) { return Failed(exception.Code, exception.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { _logger.LogWarning("Saving {Provider} credentials failed with {ErrorType}", exchange, exception.GetType().Name); return Failed("PROVIDER_UNAVAILABLE", "The credentials could not be validated or stored."); }
        finally { if (candidate is not null) await candidate.DisposeAsync().ConfigureAwait(false); gate.Release(); }
    }

    private static async Task<ServiceHealthState> TryStreamAsync(IExchangeStream stream, CancellationToken cancellationToken)
    {
        try { await stream.ConnectAsync(cancellationToken).ConfigureAwait(false); await Task.Yield(); return stream.IsConnected ? ServiceHealthState.Healthy : ServiceHealthState.Degraded; }
        catch (OperationCanceledException) { throw; }
        catch { return ServiceHealthState.Degraded; }
    }
    private ProviderSessionAccess BybitAccess()
    {
        ExchangeProviderDescriptor descriptor = _registry.Descriptors.Single(item => item.Id == BybitId);
        return new(descriptor, _bybit.Snapshot.Environment, _bybit.MarketData, _bybit.Account, _bybit.History, _bybit.Stream, _bybit.Snapshot);
    }
    private static ProviderSessionAccess Access(ReadSession session)
    {
        ProviderConnectionSnapshot snapshot = session.Connection is null
            ? session.Snapshot
            : session.Snapshot with { StreamHealth = session.Connection.Stream.IsConnected ? ServiceHealthState.Healthy : ServiceHealthState.Degraded };
        return new(session.Descriptor, session.Environment, session.MarketData, session.Connection?.Account, session.Connection?.History, session.Connection?.Stream, snapshot);
    }
    private void Update(ReadSession session) { _sessions[session.Descriptor.Id] = session; StateChanged?.Invoke(this, Access(session)); }
    private void OnBybitStateChanged(object? sender, ProviderConnectionSnapshot snapshot) => StateChanged?.Invoke(this, BybitAccess());
    private static ProviderConnectionSnapshot Empty(ExchangeProviderDescriptor descriptor, TradingEnvironment environment) => new(descriptor.DisplayName, environment, ServiceHealthState.NotConfigured, ServiceHealthState.NotConfigured, false, "None", null, null, 0, 0, null, Guid.NewGuid());
    private static ExchangeConnectionResult Failed(string code, string message) => new(false, code, message, null, ServiceHealthState.Unavailable, ServiceHealthState.NotConfigured);
    private static string Mask(string key) => key.Length <= 4 ? "••••" : $"••••••{key[^4..]}";
    private sealed record ReadSession(ExchangeProviderDescriptor Descriptor, TradingEnvironment Environment, IMarketDataProvider MarketData, IExchangeProviderConnection? Connection, ProviderConnectionSnapshot Snapshot);
}
