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
    private string DataDirectory { get; set; } = string.Empty;

    public static TestServerContext Create(
        int port = 0,
        TimeProvider? timeProvider = null,
        ILogger<LocalMcpServerHost>? logger = null,
        IExchangeProviderFactory? providerFactory = null)
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
            providerFactory ?? new UnavailableProviderFactory());
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
        IExchangeProviderFactory providerFactory)
    {
        Settings = settings;
        TokenService = tokenService;
        Metadata = metadata;
        TimeProvider = timeProvider;
        var session = new SessionCredentialStore();
        DataDirectory = Path.Combine(Path.GetTempPath(), "TradeRelay.Tests", Guid.NewGuid().ToString("N"));
        var paths = new ApplicationDataPaths(DataDirectory);
        SettingsStore = new ApplicationSettingsStore(paths);
        ConnectionManager = new ExchangeConnectionManager(
            settings,
            SettingsStore,
            new CredentialStoreCoordinator(session, session),
            providerFactory,
            NullLogger<ExchangeConnectionManager>.Instance);
        RiskEngine = new RiskEngine();
        PreparedOrderStore = new PreparedOrderStore(timeProvider);
        LiveConfirmations = new LiveActionConfirmationStore(timeProvider);
        OrderPreparationService = new OrderPreparationService(ConnectionManager, RiskEngine, PreparedOrderStore, settings);
        AuditLog = new AuditLogService(paths, timeProvider);
        TradingControl = new TradingControlService(ConnectionManager, RiskEngine, settings, AuditLog, LiveConfirmations, timeProvider);
        TradingGate = new TradingGate(TradingControl, ConnectionManager, AuditLog, settings, RiskEngine);
        OrderExecutionService = new OrderExecutionService(ConnectionManager, TradingControl, TradingGate, OrderPreparationService, PreparedOrderStore, LiveConfirmations, AuditLog, timeProvider);
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
            logger);
    }

    private sealed class UnavailableProviderFactory : IExchangeProviderFactory
    {
        public string ProviderName => "Bybit";
        public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment) => new UnavailableMarketData();
        public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentials credentials) => throw new NotSupportedException();
    }

    private sealed class UnavailableMarketData : IMarketDataProvider
    {
        public Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
        public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
        public Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
        public Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) => throw new ProviderException("PROVIDER_UNAVAILABLE", "Unavailable in test.");
    }
}
