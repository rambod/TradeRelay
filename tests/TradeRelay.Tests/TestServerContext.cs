using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;
using TradeRelay.Desktop.Security;
using TradeRelay.Core.Security;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;

namespace TradeRelay.Tests;

internal sealed class TestServerContext : IAsyncDisposable
{
    public AppSettings Settings { get; }

    public LocalMcpTokenService TokenService { get; }

    public ApplicationMetadata Metadata { get; }

    public TimeProvider TimeProvider { get; }

    public LocalMcpServerHost Host { get; }
    public ExchangeConnectionManager ConnectionManager { get; }
    public PreparedOrderStore PreparedOrderStore { get; }
    public LiveActionConfirmationStore LiveConfirmations { get; }
    public OrderPreparationService OrderPreparationService { get; }
    public ApplicationSettingsStore SettingsStore { get; }
    public RiskEngine RiskEngine { get; }
    public AuditLogService AuditLog { get; }
    public TradingControlService TradingControl { get; }
    public TradingGate TradingGate { get; }
    public OrderExecutionService OrderExecutionService { get; }
    public ApplicationDataPaths Paths { get; }
    public SensitiveDataRedactor Redactor { get; }
    public SafeLogService SafeLog { get; }
    public OAuthPairingService OAuth { get; }
    public ExchangeSessionCoordinator Sessions { get; }
    private string DataDirectory { get; set; } = string.Empty;

    public static TestServerContext Create(
        int port = 0,
        TimeProvider? timeProvider = null,
        ILogger<LocalMcpServerHost>? logger = null,
        IExchangeProviderFactory? providerFactory = null,
        IReadOnlyList<IExchangeProviderFactory>? additionalProviders = null)
    {
        var settings = new AppSettings
        {
            Server = new ServerSettings
            {
                Port = port,
                StartAutomatically = false
            }
        };

        return new TestServerContext(
            settings,
            new LocalMcpTokenService(),
            new ApplicationMetadata(),
            timeProvider ?? TimeProvider.System,
            logger ?? NullLogger<LocalMcpServerHost>.Instance,
            providerFactory ?? new UnavailableProviderFactory(),
            additionalProviders ?? []);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopServerAsync();
        TradingControl.Dispose();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true);
    }

    private TestServerContext(
        AppSettings settings,
        LocalMcpTokenService tokenService,
        ApplicationMetadata metadata,
        TimeProvider timeProvider,
        ILogger<LocalMcpServerHost> logger,
        IExchangeProviderFactory providerFactory,
        IReadOnlyList<IExchangeProviderFactory> additionalProviders)
    {
        Settings = settings;
        TokenService = tokenService;
        Metadata = metadata;
        TimeProvider = timeProvider;
        var session = new SessionCredentialStore();
        DataDirectory = Path.Combine(Path.GetTempPath(), "TradeRelay.Tests", Guid.NewGuid().ToString("N"));
        Paths = new ApplicationDataPaths(DataDirectory);
        Redactor = new SensitiveDataRedactor();
        SafeLog = new SafeLogService(Paths, timeProvider, Redactor);
        OAuth = new OAuthPairingService(Paths, new InMemoryProtectedSecretStore(), timeProvider);
        SettingsStore = new ApplicationSettingsStore(Paths);
        var credentialCoordinator = new CredentialStoreCoordinator(session, session);
        ConnectionManager = new ExchangeConnectionManager(
            settings,
            SettingsStore,
            credentialCoordinator,
            providerFactory,
            NullLogger<ExchangeConnectionManager>.Instance);
        RiskEngine = new RiskEngine();
        PreparedOrderStore = new PreparedOrderStore(timeProvider);
        LiveConfirmations = new LiveActionConfirmationStore(timeProvider);
        OrderPreparationService = new OrderPreparationService(ConnectionManager, RiskEngine, PreparedOrderStore, settings);
        AuditLog = new AuditLogService(Paths, timeProvider, Redactor);
        TradingControl = new TradingControlService(ConnectionManager, RiskEngine, settings, AuditLog, LiveConfirmations, timeProvider);
        TradingGate = new TradingGate(TradingControl, ConnectionManager, AuditLog, settings, RiskEngine);
        OrderExecutionService = new OrderExecutionService(ConnectionManager, TradingControl, TradingGate, OrderPreparationService, PreparedOrderStore, LiveConfirmations, AuditLog, timeProvider);
        var registry = new ExchangeProviderRegistry([providerFactory, .. additionalProviders]);
        Sessions = new ExchangeSessionCoordinator(ConnectionManager, registry, settings, SettingsStore, credentialCoordinator, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExchangeSessionCoordinator>.Instance);
        Host = new LocalMcpServerHost(
            settings,
            tokenService,
            metadata,
            timeProvider,
            ConnectionManager,
            OrderPreparationService,
            PreparedOrderStore,
            LiveConfirmations,
            OrderExecutionService,
            TradingControl,
            AuditLog,
            SafeLog,
            registry,
            Sessions,
            OAuth,
            logger);
    }

    private sealed class UnavailableProviderFactory : IExchangeProviderFactory
    {
        public string ProviderName => "Bybit";
        public ExchangeProviderDescriptor Descriptor { get; } = new(new ExchangeId("bybit"), "Bybit", ProviderCapabilities.MarketData | ProviderCapabilities.AccountRead | ProviderCapabilities.PrivateStream | ProviderCapabilities.History | ProviderCapabilities.TradingWrite, [TradingEnvironment.Demo, TradingEnvironment.Live], []);
        public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment) => new UnavailableMarketData();
        public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials) => throw new NotSupportedException();
    }

    private sealed class UnavailableMarketData : IMarketDataProvider
    {
        public Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
        public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
        public Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
        public Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
    }

    private sealed class InMemoryProtectedSecretStore : IProtectedSecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public bool CanPersist => true;
        public Task SaveAsync(string id, string value, CancellationToken cancellationToken) { _values[id] = value; return Task.CompletedTask; }
        public Task<string?> LoadAsync(string id, CancellationToken cancellationToken) => Task.FromResult(_values.GetValueOrDefault(id));
        public Task DeleteAsync(string id, CancellationToken cancellationToken) { _values.Remove(id); return Task.CompletedTask; }
    }
}
