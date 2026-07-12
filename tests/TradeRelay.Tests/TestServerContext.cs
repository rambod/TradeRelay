using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;
using TradeRelay.Desktop.Security;
using TradeRelay.Core.Security;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Models;

namespace TradeRelay.Tests;

internal sealed class TestServerContext : IAsyncDisposable
{
    public AppSettings Settings { get; }

    public LocalMcpTokenService TokenService { get; }

    public ApplicationMetadata Metadata { get; }

    public TimeProvider TimeProvider { get; }

    public LocalMcpServerHost Host { get; }
    public ExchangeConnectionManager ConnectionManager { get; }
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
        ConnectionManager = new ExchangeConnectionManager(
            settings,
            new ApplicationSettingsStore(new ApplicationDataPaths(DataDirectory)),
            new CredentialStoreCoordinator(session, session),
            providerFactory,
            NullLogger<ExchangeConnectionManager>.Instance);
        Host = new LocalMcpServerHost(
            settings,
            tokenService,
            metadata,
            timeProvider,
            ConnectionManager,
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
