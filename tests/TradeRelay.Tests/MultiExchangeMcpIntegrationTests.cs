using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using Xunit;

namespace TradeRelay.Tests;

public sealed class MultiExchangeMcpIntegrationTests
{
    [Fact]
    public async Task OfficialClientReadsExplicitExchangeAndNonBybitWriteStopsBeforeProvider()
    {
        var binance = new ReadOnlyProviderFactory();
        await using TestServerContext context = TestServerContext.Create(additionalProviders: [binance]);
        var credentials = new ExchangeCredentialSet(new Dictionary<string, string> { [ExchangeCredentials.ApiKeyField] = "sentinel-key", [ExchangeCredentials.ApiSecretField] = "sentinel-secret" });
        Assert.True((await context.Sessions.SaveAsync(new ExchangeId("binance"), credentials, false, default)).Success);
        await context.Sessions.SelectAsync(new ExchangeId("binance"), default);
        await context.Host.StartServerAsync();
        await using McpClient client = await CreateClientAsync(context.Host.Snapshot.Url, context.TokenService.CurrentToken);
        IList<McpClientTool> tools = await client.ListToolsAsync();

        CallToolResult ticker = await tools.Single(item => item.Name == "get_ticker").CallAsync(new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["exchange"] = "binance" });
        CallToolResult account = await tools.Single(item => item.Name == "get_account_summary").CallAsync(new Dictionary<string, object?> { ["exchange"] = "binance" });
        CallToolResult history = await tools.Single(item => item.Name == "get_order_history").CallAsync(new Dictionary<string, object?> { ["exchange"] = "binance" });
        CallToolResult status = await tools.Single(item => item.Name == "get_system_status").CallAsync();
        CallToolResult write = await tools.Single(item => item.Name == "cancel_all_orders").CallAsync(new Dictionary<string, object?> { ["confirm"] = true, ["exchange"] = "binance" });

        Assert.Contains("\"success\":true", ticker.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"success\":true", account.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"success\":true", history.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"selectedProvider\":\"Binance\"", status.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CAPABILITY_NOT_SUPPORTED", write.StructuredContent?.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0, binance.Trading.Calls);
        string all = string.Join(string.Empty, new[] { ticker, account, history, status, write }.Select(item => item.StructuredContent?.GetRawText()));
        Assert.DoesNotContain("sentinel-key", all, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", all, StringComparison.Ordinal);
    }

    private static async Task<McpClient> CreateClientAsync(string endpoint, string token) => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(endpoint), TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token } }));

    private sealed class ReadOnlyProviderFactory : IExchangeProviderFactory
    {
        public CountingTradingProvider Trading { get; } = new();
        public string ProviderName => "Binance";
        public ExchangeProviderDescriptor Descriptor { get; } = new(new ExchangeId("binance"), "Binance", ProviderCapabilities.MarketData | ProviderCapabilities.AccountRead | ProviderCapabilities.PrivateStream | ProviderCapabilities.History, [TradingEnvironment.Live], [new(ExchangeCredentials.ApiKeyField, "API key", false), new(ExchangeCredentials.ApiSecretField, "API secret", true)]);
        public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment) => new Market();
        public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials) { _ = credentials[ExchangeCredentials.ApiKeyField]; _ = credentials[ExchangeCredentials.ApiSecretField]; return new Connection(Trading); }
    }
    private sealed class Connection(IExchangeTradingProvider trading) : IExchangeProviderConnection
    {
        public ITradingAccountProvider Account { get; } = new Account();
        public IExchangeTradingProvider Trading { get; } = trading;
        public IExchangeStream Stream { get; } = new Stream();
        public IExchangeHistoryProvider? History { get; } = new History();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Market : IMarketDataProvider
    {
        public Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(new TickerSnapshot(symbol.ToUpperInvariant(), 65000m, 64999m, 65001m, null, null, 1m, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Candle>>([]);
        public Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(new InstrumentInfo(symbol, "Trading", .1m, .001m, .001m, 100m, 20m, 5m, 100m, "USDT linear perpetual"));
        public Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) => Task.FromResult(new OrderBookSnapshot(symbol, [], [], DateTimeOffset.UtcNow));
    }
    private sealed class Account : ITradingAccountProvider
    {
        public Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(new AccountSummary(1000m, 900m, 0m, 10m, TradingEnvironment.Live, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WalletBalance>>([]);
        public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PositionSnapshot>>([]);
        public Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderSnapshot>>([]);
        public Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new ApiCredentialInfo(true, false, true, false, true, null, null, false, TradingEnvironment.Live, []));
    }
    private sealed class History : IExchangeHistoryProvider
    {
        public Task<IReadOnlyList<HistoricalOrder>> GetOrderHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoricalOrder>>([]);
        public Task<IReadOnlyList<HistoricalExecution>> GetExecutionHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoricalExecution>>([]);
    }
    private sealed class Stream : IExchangeStream
    {
        public bool IsConnected { get; private set; }
        public event EventHandler<OrderUpdate>? OrderUpdated { add { } remove { } }
        public event EventHandler<ExecutionUpdate>? ExecutionUpdated { add { } remove { } }
        public event EventHandler<PositionUpdate>? PositionUpdated { add { } remove { } }
        public Task ConnectAsync(CancellationToken cancellationToken) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }
    }
    private sealed class CountingTradingProvider : IExchangeTradingProvider
    {
        public int Calls { get; private set; }
        private Task<T> Unsupported<T>() { Calls++; return Task.FromException<T>(new ProviderException("CAPABILITY_NOT_SUPPORTED", "Read only.")); }
        public Task<OrderSubmissionResult> PlaceOrderAsync(ExchangeOrderRequest order, CancellationToken cancellationToken) => Unsupported<OrderSubmissionResult>();
        public Task<OrderSubmissionResult?> GetOrderAsync(string symbol, string? exchangeOrderId, string? clientOrderId, CancellationToken cancellationToken) => Unsupported<OrderSubmissionResult?>();
        public Task<OperationResult> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken) => Unsupported<OperationResult>();
        public Task<OperationResult> CancelAllOrdersAsync(string? symbol, CancellationToken cancellationToken) => Unsupported<OperationResult>();
        public Task<OrderSubmissionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken) => Unsupported<OrderSubmissionResult>();
        public Task<OperationResult> SetTradingStopAsync(TradingStopRequest request, CancellationToken cancellationToken) => Unsupported<OperationResult>();
    }
}
