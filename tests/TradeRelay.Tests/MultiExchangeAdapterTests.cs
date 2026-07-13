using System.Text.Json;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Providers.Binance;
using TradeRelay.Providers.KuCoin;
using Xunit;

namespace TradeRelay.Tests;

public sealed class MultiExchangeAdapterTests
{
    private static readonly TimeProvider Time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 13, 20, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Binance_AdvertisesReadOnlyCapabilitiesAndMapsMarketAccountAndHistory()
    {
        var factory = new BinanceExchangeProviderFactory(Time);
        Assert.Equal([TradingEnvironment.Live], factory.Descriptor.Environments);
        Assert.False(factory.Descriptor.Capabilities.HasFlag(ProviderCapabilities.TradingWrite));
        var api = new FakeBinanceApi(new Dictionary<string, string>
        {
            ["/fapi/v1/ticker/24hr"] = """{"symbol":"BTCUSDT","lastPrice":"65000","bidPrice":"64999","askPrice":"65001","highPrice":"66000","lowPrice":"64000","volume":"123"}""",
            ["/fapi/v1/exchangeInfo"] = """{"symbols":[{"symbol":"BTCUSDT","status":"TRADING","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.10"},{"filterType":"LOT_SIZE","stepSize":"0.001","minQty":"0.001","maxQty":"100"},{"filterType":"MARKET_LOT_SIZE","maxQty":"20"},{"filterType":"MIN_NOTIONAL","notional":"5"}]}]}""",
            ["/fapi/v3/positionRisk"] = """[{"symbol":"BTCUSDT","positionAmt":"-0.25","entryPrice":"64000","markPrice":"65000","leverage":"3","unRealizedProfit":"-250","liquidationPrice":"80000","positionSide":"BOTH"}]""",
            ["/fapi/v1/openOrders"] = "[]",
            ["/fapi/v1/allOrders"] = """[{"orderId":42,"clientOrderId":"client-1","symbol":"BTCUSDT","side":"SELL","type":"LIMIT","price":"66000","origQty":"0.25","executedQty":"0.10","status":"PARTIALLY_FILLED","reduceOnly":false,"time":1000,"updateTime":2000}]""",
        });
        var market = new BinanceMarketDataProvider(api, Time);
        TickerSnapshot ticker = await market.GetTickerAsync("btcusdt", default);
        InstrumentInfo instrument = await market.GetInstrumentInfoAsync("BTCUSDT", default);
        var account = new BinanceTradingAccountProvider(api, Time);
        PositionSnapshot position = Assert.Single(await account.GetPositionsAsync(null, default));
        var history = new BinanceHistoryProvider(api);
        HistoricalOrder order = Assert.Single(await history.GetOrderHistoryAsync(new ExchangeHistoryQuery("BTCUSDT"), default));

        Assert.Equal(65000m, ticker.LastPrice);
        Assert.Equal(0.001m, instrument.QuantityStep);
        Assert.Equal(20m, instrument.MaximumMarketQuantity);
        Assert.Equal(TradeSide.Sell, position.Side);
        Assert.Equal(0.25m, position.Size);
        Assert.Equal(0.15m, order.RemainingQuantity);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => new BinanceReadOnlyTradingProvider().CancelAllOrdersAsync(null, default));
        Assert.Equal("CAPABILITY_NOT_SUPPORTED", error.Code);
    }

    [Fact]
    public async Task KuCoin_RequiresPassphraseMapsSymbolsAndCannotWrite()
    {
        var factory = new KuCoinExchangeProviderFactory(Time);
        Assert.Contains(factory.Descriptor.CredentialFields, field => field.Name == ExchangeCredentials.PassphraseField && field.IsSecret);
        Assert.False(factory.Descriptor.Capabilities.HasFlag(ProviderCapabilities.TradingWrite));
        Assert.Equal("XBTUSDTM", KuCoinSymbols.ToNative("BTCUSDT"));
        Assert.Equal("BTCUSDT", KuCoinSymbols.FromNative("XBTUSDTM"));

        var api = new FakeKuCoinApi(new Dictionary<string, string>
        {
            ["/api/v1/ticker"] = """{"symbol":"XBTUSDTM","price":"65000","bestBidPrice":"64999","bestAskPrice":"65001","size":"2","ts":1783972800000}""",
            ["/api/v1/positions"] = """[{"symbol":"XBTUSDTM","currentQty":"-2","avgEntryPrice":"64000","markPrice":"65000","realLeverage":"4","unrealisedPnl":"-20","liquidationPrice":"79000","positionSide":"BOTH"}]""",
            ["/api/v1/contracts/XBTUSDTM"] = """{"symbol":"XBTUSDTM","multiplier":"0.001","tickSize":"0.1","lotSize":"1","maxOrderQty":"100000","maxMarketOrderQty":"50000","maxLeverage":"100","status":"Open"}""",
        });
        var market = new KuCoinMarketDataProvider(api, Time);
        TickerSnapshot ticker = await market.GetTickerAsync("btcusdt", default);
        InstrumentInfo instrument = await market.GetInstrumentInfoAsync("BTCUSDT", default);
        var account = new KuCoinTradingAccountProvider(api, Time);
        PositionSnapshot position = Assert.Single(await account.GetPositionsAsync(null, default));

        Assert.Equal(65000m, ticker.LastPrice);
        Assert.Equal(0.001m, instrument.QuantityStep);
        Assert.Equal(TradeSide.Sell, position.Side);
        Assert.Equal(0.002m, position.Size);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => new KuCoinReadOnlyTradingProvider().ClosePositionAsync(new ClosePositionRequest("BTCUSDT", TradeSide.Buy, 0.01m, "BOTH"), default));
        Assert.Equal("CAPABILITY_NOT_SUPPORTED", error.Code);
    }

    [Fact]
    public async Task PrivateStreamsReconnectAfterTransportEnds()
    {
        var binanceApi = new ReconnectingBinanceApi();
        var kuCoinApi = new ReconnectingKuCoinApi();
        var binance = new BinanceExchangeStream(binanceApi, Time);
        var kuCoin = new KuCoinExchangeStream(kuCoinApi, Time);

        await Task.WhenAll(binance.ConnectAsync(default), kuCoin.ConnectAsync(default));
        await Task.WhenAll(
            binanceApi.Reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            kuCoinApi.Reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.True(binance.IsConnected);
        Assert.True(kuCoin.IsConnected);
        Assert.True(binanceApi.Attempts >= 2);
        Assert.True(kuCoinApi.Attempts >= 2);
        await Task.WhenAll(binance.DisconnectAsync(default), kuCoin.DisconnectAsync(default));
        Assert.False(binance.IsConnected);
        Assert.False(kuCoin.IsConnected);
    }

    [Fact]
    public async Task KuCoinPrivateStreamNormalizesContractQuantities()
    {
        var api = new EmittingKuCoinApi();
        var stream = new KuCoinExchangeStream(api, Time);
        var execution = new TaskCompletionSource<ExecutionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var position = new TaskCompletionSource<PositionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.ExecutionUpdated += (_, value) => execution.TrySetResult(value);
        stream.PositionUpdated += (_, value) => position.TrySetResult(value);

        await stream.ConnectAsync(default);
        ExecutionUpdate executionUpdate = await execution.Task.WaitAsync(TimeSpan.FromSeconds(2));
        PositionUpdate positionUpdate = await position.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("BTCUSDT", executionUpdate.Symbol);
        Assert.Equal(0.002m, executionUpdate.Quantity);
        Assert.Equal(TradeSide.Sell, positionUpdate.Side);
        Assert.Equal(0.003m, positionUpdate.Size);
        await stream.DisconnectAsync(default);
    }

    private sealed class FakeBinanceApi(IReadOnlyDictionary<string, string> payloads) : IBinanceApi
    {
        public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken) => Task.FromResult(Parse(payloads[path]));
        public Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken) => onConnected();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeKuCoinApi(IReadOnlyDictionary<string, string> payloads) : IKuCoinApi
    {
        public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken) => Task.FromResult(Parse(payloads[path]));
        public Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken) => onConnected();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ReconnectingBinanceApi : IBinanceApi
    {
        public int Attempts;
        public TaskCompletionSource Reconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref Attempts);
            await onConnected();
            if (attempt == 1) return;
            Reconnected.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ReconnectingKuCoinApi : IKuCoinApi
    {
        public int Attempts;
        public TaskCompletionSource Reconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref Attempts);
            await onConnected();
            if (attempt == 1) return;
            Reconnected.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class EmittingKuCoinApi : IKuCoinApi
    {
        public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken) =>
            Task.FromResult(Parse("""{"multiplier":"0.001"}"""));
        public async Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken)
        {
            await onConnected();
            await onMessage(Parse("""{"topic":"/contractMarket/tradeOrders","data":{"symbol":"XBTUSDTM","orderId":"order-1","status":"match","matchSize":"2","matchPrice":"65000","ts":1783972800000000000}}"""));
            await onMessage(Parse("""{"topic":"/contract/positionAll","data":{"symbol":"XBTUSDTM","currentQty":"-3","ts":1783972800000000000}}"""));
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static JsonElement Parse(string json) { using JsonDocument document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
