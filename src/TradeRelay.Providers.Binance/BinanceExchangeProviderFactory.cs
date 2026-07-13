using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.Binance;

/// <summary>Creates Live-only, read-only Binance USD-M Futures services.</summary>
public sealed class BinanceExchangeProviderFactory(TimeProvider timeProvider) : IExchangeProviderFactory
{
    /// <inheritdoc />
    public string ProviderName => "Binance";

    /// <inheritdoc />
    public ExchangeProviderDescriptor Descriptor { get; } = new(
        new ExchangeId("binance"),
        "Binance",
        ProviderCapabilities.MarketData | ProviderCapabilities.AccountRead | ProviderCapabilities.PrivateStream | ProviderCapabilities.History,
        [TradingEnvironment.Live],
        [
            new CredentialFieldDescriptor(ExchangeCredentials.ApiKeyField, "API key", false),
            new CredentialFieldDescriptor(ExchangeCredentials.ApiSecretField, "API secret", true),
        ]);

    /// <inheritdoc />
    public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment)
    {
        RequireLive(environment);
        return new BinanceMarketDataProvider(new BinanceApi(null, timeProvider), timeProvider);
    }

    /// <inheritdoc />
    public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials)
    {
        RequireLive(environment);
        ArgumentNullException.ThrowIfNull(credentials);
        var api = new BinanceApi(credentials, timeProvider);
        return new BinanceExchangeConnection(api, timeProvider);
    }

    private static void RequireLive(TradingEnvironment environment)
    {
        if (environment != TradingEnvironment.Live) throw new ProviderException("CAPABILITY_NOT_SUPPORTED", "Binance supports one Live read-only profile.");
    }
}

internal interface IBinanceApi : IAsyncDisposable
{
    Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken);
    Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken);
}

internal sealed class BinanceApi : IBinanceApi
{
    private static readonly Uri RestBase = new("https://fapi.binance.com");
    private readonly HttpClient _http = new() { BaseAddress = RestBase, Timeout = TimeSpan.FromSeconds(15) };
    private readonly ExchangeCredentialSet? _credentials;
    private readonly TimeProvider _timeProvider;

    public BinanceApi(ExchangeCredentialSet? credentials, TimeProvider timeProvider) { _credentials = credentials; _timeProvider = timeProvider; }

    public async Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken)
    {
        var values = new List<KeyValuePair<string, string>>();
        if (parameters is not null) values.AddRange(parameters.Where(item => item.Value is not null).Select(item => new KeyValuePair<string, string>(item.Key, item.Value!)));
        if (authenticated)
        {
            if (_credentials is null) throw new ProviderException("CREDENTIALS_MISSING", "Binance credentials are required.");
            values.Add(new("recvWindow", "5000"));
            values.Add(new("timestamp", _timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
        }
        string query = string.Join('&', values.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        if (authenticated)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_credentials![ExchangeCredentials.ApiSecretField]));
            string signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
            query += "&signature=" + signature;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, path + (query.Length == 0 ? string.Empty : "?" + query));
        if (authenticated) request.Headers.Add("X-MBX-APIKEY", _credentials![ExchangeCredentials.ApiKeyField]);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw Error(response.StatusCode, payload);
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    public async Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken)
    {
        if (_credentials is null) throw new ProviderException("CREDENTIALS_MISSING", "Binance credentials are required.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/listenKey");
        request.Headers.Add("X-MBX-APIKEY", _credentials[ExchangeCredentials.ApiKeyField]);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new ProviderException(response.StatusCode == HttpStatusCode.TooManyRequests ? "RATE_LIMITED" : "PROVIDER_UNAVAILABLE", "Binance private stream could not be started.");
        using JsonDocument token = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        string listenKey = token.RootElement.GetProperty("listenKey").GetString() ?? throw new ProviderException("PROVIDER_UNAVAILABLE", "Binance returned no private-stream token.");
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri("wss://fstream.binance.com/ws/" + Uri.EscapeDataString(listenKey)), cancellationToken).ConfigureAwait(false);
        await onConnected().ConfigureAwait(false);
        using var keepAliveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task keepAlive = KeepListenKeyAliveAsync(socket, keepAliveCancellation.Token);
        try
        {
            byte[] buffer = new byte[32 * 1024];
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                using JsonDocument document = JsonDocument.Parse(message.ToArray());
                await onMessage(document.RootElement.Clone()).ConfigureAwait(false);
            }
        }
        finally
        {
            keepAliveCancellation.Cancel();
            try { await keepAlive.ConfigureAwait(false); }
            catch (OperationCanceledException) when (keepAliveCancellation.IsCancellationRequested) { }
        }
    }

    private async Task KeepListenKeyAliveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(30), cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/fapi/v1/listenKey");
            request.Headers.Add("X-MBX-APIKEY", _credentials![ExchangeCredentials.ApiKeyField]);
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) continue;
            socket.Abort();
            throw new ProviderException(response.StatusCode == HttpStatusCode.TooManyRequests ? "RATE_LIMITED" : "PROVIDER_UNAVAILABLE", "Binance private-stream renewal failed.");
        }
    }

    public ValueTask DisposeAsync() { _http.Dispose(); return ValueTask.CompletedTask; }

    private static ProviderException Error(HttpStatusCode status, string payload)
    {
        if (status == HttpStatusCode.TooManyRequests) return new ProviderException("RATE_LIMITED", "Binance rate-limited the read request.");
        string code = payload.Contains("-2015", StringComparison.Ordinal) || payload.Contains("-2014", StringComparison.Ordinal) ? "CREDENTIALS_INVALID" : "PROVIDER_UNAVAILABLE";
        return new ProviderException(code, code == "CREDENTIALS_INVALID" ? "Binance rejected the API credentials." : "Binance could not complete the read request.");
    }
}

internal sealed class BinanceExchangeConnection : IExchangeProviderConnection
{
    private readonly IBinanceApi _api;
    public BinanceExchangeConnection(IBinanceApi api, TimeProvider timeProvider)
    {
        _api = api;
        Account = new BinanceTradingAccountProvider(api, timeProvider);
        History = new BinanceHistoryProvider(api);
        Stream = new BinanceExchangeStream(api, timeProvider);
        Trading = new BinanceReadOnlyTradingProvider();
    }
    public ITradingAccountProvider Account { get; }
    public IExchangeTradingProvider Trading { get; }
    public IExchangeStream Stream { get; }
    public IExchangeHistoryProvider? History { get; }
    public async ValueTask DisposeAsync() { await Stream.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); await _api.DisposeAsync().ConfigureAwait(false); }
}

internal sealed class BinanceMarketDataProvider(IBinanceApi api, TimeProvider timeProvider) : IMarketDataProvider, IAsyncDisposable
{
    public async Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken)
    {
        symbol = Symbols.Normalize(symbol);
        JsonElement value = await api.GetAsync("/fapi/v1/ticker/24hr", Params(("symbol", symbol)), false, cancellationToken).ConfigureAwait(false);
        return new(symbol, value.Decimal("lastPrice"), value.NullableDecimal("bidPrice"), value.NullableDecimal("askPrice"), value.NullableDecimal("highPrice"), value.NullableDecimal("lowPrice"), value.NullableDecimal("volume"), timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken)
    {
        symbol = Symbols.Normalize(symbol); limit = Math.Clamp(limit, 1, 200);
        JsonElement values = await api.GetAsync("/fapi/v1/klines", Params(("symbol", symbol), ("interval", Interval(interval)), ("limit", limit.ToString(CultureInfo.InvariantCulture))), false, cancellationToken).ConfigureAwait(false);
        return values.EnumerateArray().Select(row => new Candle(symbol, interval, Time(row[0].GetInt64()), Time(row[6].GetInt64()), row[1].Decimal(), row[2].Decimal(), row[3].Decimal(), row[4].Decimal(), row[5].Decimal())).OrderBy(item => item.OpenTimeUtc).ToArray();
    }

    public async Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken)
    {
        symbol = Symbols.Normalize(symbol);
        JsonElement root = await api.GetAsync("/fapi/v1/exchangeInfo", Params(("symbol", symbol)), false, cancellationToken).ConfigureAwait(false);
        JsonElement item = root.GetProperty("symbols").EnumerateArray().Single(value => value.GetProperty("symbol").GetString() == symbol);
        JsonElement[] filters = item.GetProperty("filters").EnumerateArray().ToArray();
        JsonElement price = Filter(filters, "PRICE_FILTER"), lot = Filter(filters, "LOT_SIZE");
        JsonElement? market = filters.Where(value => value.GetProperty("filterType").GetString() == "MARKET_LOT_SIZE").Select(value => (JsonElement?)value).FirstOrDefault();
        JsonElement? notional = filters.Where(value => value.GetProperty("filterType").GetString() is "MIN_NOTIONAL" or "NOTIONAL").Select(value => (JsonElement?)value).FirstOrDefault();
        decimal? maximumMarket = market is { ValueKind: JsonValueKind.Object } marketValue ? marketValue.NullableDecimal("maxQty") : null;
        decimal? minimumNotional = notional is { ValueKind: JsonValueKind.Object } notionalValue ? notionalValue.NullableDecimal("notional") ?? notionalValue.NullableDecimal("minNotional") : null;
        return new(symbol, item.GetProperty("status").GetString() ?? "Unknown", price.Decimal("tickSize"), lot.Decimal("stepSize"), lot.Decimal("minQty"), lot.Decimal("maxQty"), maximumMarket, minimumNotional, null, "USDT linear perpetual");
    }

    public async Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken)
    {
        symbol = Symbols.Normalize(symbol); depth = Math.Clamp(depth, 1, 50);
        JsonElement value = await api.GetAsync("/fapi/v1/depth", Params(("symbol", symbol), ("limit", depth <= 20 ? "20" : "50")), false, cancellationToken).ConfigureAwait(false);
        return new(symbol, Levels(value.GetProperty("bids"), depth), Levels(value.GetProperty("asks"), depth), timeProvider.GetUtcNow());
    }

    private static IReadOnlyList<OrderBookLevel> Levels(JsonElement values, int depth) => values.EnumerateArray().Take(depth).Select(row => new OrderBookLevel(row[0].Decimal(), row[1].Decimal())).ToArray();
    private static JsonElement Filter(IEnumerable<JsonElement> filters, string type) => filters.Single(value => value.GetProperty("filterType").GetString() == type);
    private static string Interval(CandleInterval interval) => interval switch { CandleInterval.OneMinute => "1m", CandleInterval.ThreeMinutes => "3m", CandleInterval.FiveMinutes => "5m", CandleInterval.FifteenMinutes => "15m", CandleInterval.ThirtyMinutes => "30m", CandleInterval.OneHour => "1h", CandleInterval.TwoHours => "2h", CandleInterval.FourHours => "4h", CandleInterval.SixHours => "6h", CandleInterval.TwelveHours => "12h", CandleInterval.OneDay => "1d", CandleInterval.OneWeek => "1w", _ => throw new ProviderException("INVALID_INPUT", "Unsupported candle interval.") };
    private static DateTimeOffset Time(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    private static Dictionary<string, string?> Params(params (string Key, string? Value)[] values) => values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    public ValueTask DisposeAsync() => api.DisposeAsync();
}

internal sealed class BinanceTradingAccountProvider(IBinanceApi api, TimeProvider timeProvider) : ITradingAccountProvider
{
    private Task<JsonElement> Account(CancellationToken cancellationToken) => api.GetAsync("/fapi/v3/account", null, true, cancellationToken);

    public async Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await Account(cancellationToken).ConfigureAwait(false);
        decimal equity = value.Decimal("totalMarginBalance"), available = value.Decimal("availableBalance"), pnl = value.Decimal("totalUnrealizedProfit");
        decimal? margin = equity == 0 ? null : value.Decimal("totalMaintMargin") / equity * 100m;
        return new(equity, available, pnl, margin, TradingEnvironment.Live, timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await Account(cancellationToken).ConfigureAwait(false);
        return value.GetProperty("assets").EnumerateArray().Select(item => new WalletBalance(item.GetProperty("asset").GetString() ?? string.Empty, item.Decimal("marginBalance"), item.Decimal("walletBalance"), item.Decimal("availableBalance"), item.Decimal("unrealizedProfit"), item.GetProperty("asset").GetString() == "USDT" ? item.NullableDecimal("marginBalance") : null)).Where(item => item.Equity != 0m || item.WalletBalanceAmount != 0m).ToArray();
    }

    public async Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken)
    {
        string? normalized = string.IsNullOrWhiteSpace(symbol) ? null : Symbols.Normalize(symbol);
        JsonElement value = await api.GetAsync("/fapi/v3/positionRisk", normalized is null ? null : new Dictionary<string, string?> { ["symbol"] = normalized }, true, cancellationToken).ConfigureAwait(false);
        return value.EnumerateArray().Select(MapPosition).Where(item => item.Size > 0 && (normalized is null || item.Symbol == normalized)).ToArray();
    }

    public async Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken)
    {
        string? normalized = string.IsNullOrWhiteSpace(symbol) ? null : Symbols.Normalize(symbol);
        JsonElement value = await api.GetAsync("/fapi/v1/openOrders", normalized is null ? null : new Dictionary<string, string?> { ["symbol"] = normalized }, true, cancellationToken).ConfigureAwait(false);
        return value.EnumerateArray().Select(BinanceMappings.Order).ToArray();
    }

    public async Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await Account(cancellationToken).ConfigureAwait(false);
        bool canTrade = value.TryGetProperty("canTrade", out JsonElement trade) && trade.GetBoolean();
        return new(!canTrade, canTrade, true, false, false, null, null, true, TradingEnvironment.Live, ["Binance does not expose API-key IP binding or withdrawal scope through this futures read endpoint. Verify restrictions in Binance API Management."]);
    }

    internal static PositionSnapshot MapPosition(JsonElement item)
    {
        decimal amount = item.Decimal("positionAmt");
        string positionSide = item.String("positionSide") ?? "BOTH";
        TradeSide side = positionSide == "SHORT" || positionSide == "BOTH" && amount < 0 ? TradeSide.Sell : TradeSide.Buy;
        return new(item.String("symbol") ?? string.Empty, side, Math.Abs(amount), item.Decimal("entryPrice"), item.Decimal("markPrice"), item.Decimal("leverage"), item.Decimal("unRealizedProfit"), item.NullableDecimal("liquidationPrice"), null, null, positionSide);
    }
}

internal sealed class BinanceHistoryProvider(IBinanceApi api) : IExchangeHistoryProvider
{
    public async Task<IReadOnlyList<HistoricalOrder>> GetOrderHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken)
    {
        string[] symbols = await SymbolsForHistoryAsync(query.Symbol, cancellationToken).ConfigureAwait(false);
        var results = new List<HistoricalOrder>();
        foreach (string symbol in symbols)
        {
            JsonElement values = await api.GetAsync("/fapi/v1/allOrders", HistoryParams(query, symbol), true, cancellationToken).ConfigureAwait(false);
            results.AddRange(values.EnumerateArray().Select(BinanceMappings.HistoricalOrder));
        }
        return results.OrderByDescending(item => item.UpdatedTimeUtc).Take(Math.Clamp(query.Limit, 1, 200)).ToArray();
    }

    public async Task<IReadOnlyList<HistoricalExecution>> GetExecutionHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken)
    {
        string[] symbols = await SymbolsForHistoryAsync(query.Symbol, cancellationToken).ConfigureAwait(false);
        var results = new List<HistoricalExecution>();
        foreach (string symbol in symbols)
        {
            JsonElement values = await api.GetAsync("/fapi/v1/userTrades", HistoryParams(query, symbol), true, cancellationToken).ConfigureAwait(false);
            results.AddRange(values.EnumerateArray().Select(BinanceMappings.Execution));
        }
        return results.OrderByDescending(item => item.TimestampUtc).Take(Math.Clamp(query.Limit, 1, 200)).ToArray();
    }

    private async Task<string[]> SymbolsForHistoryAsync(string? symbol, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(symbol)) return [Symbols.Normalize(symbol)];
        JsonElement positions = await api.GetAsync("/fapi/v3/positionRisk", null, true, cancellationToken).ConfigureAwait(false);
        JsonElement orders = await api.GetAsync("/fapi/v1/openOrders", null, true, cancellationToken).ConfigureAwait(false);
        IEnumerable<string?> symbols = positions.EnumerateArray().Where(item => item.Decimal("positionAmt") != 0).Select(item => item.String("symbol"))
            .Concat(orders.EnumerateArray().Select(item => item.String("symbol")));
        try
        {
            JsonElement income = await api.GetAsync("/fapi/v1/income", new Dictionary<string, string?> { ["limit"] = "100" }, true, cancellationToken).ConfigureAwait(false);
            symbols = symbols.Concat(income.EnumerateArray().Select(item => item.String("symbol")));
        }
        catch (ProviderException) { }
        return symbols.Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().Distinct(StringComparer.Ordinal).Take(20).ToArray();
    }

    private static Dictionary<string, string?> HistoryParams(ExchangeHistoryQuery query, string symbol) => new(StringComparer.Ordinal) { ["symbol"] = symbol, ["startTime"] = query.FromUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ["endTime"] = query.ToUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ["limit"] = Math.Clamp(query.Limit, 1, 200).ToString(CultureInfo.InvariantCulture) };
}

internal static class BinanceMappings
{
    public static OrderSnapshot Order(JsonElement item) => new(item.String("orderId") ?? item.GetProperty("orderId").GetInt64().ToString(CultureInfo.InvariantCulture), item.String("clientOrderId"), item.String("symbol") ?? string.Empty, Side(item.String("side")), item.String("type") ?? "Unknown", item.NullableDecimal("price"), item.Decimal("origQty"), item.Decimal("executedQty"), item.String("status") ?? "Unknown", item.TryGetProperty("reduceOnly", out JsonElement reduce) && reduce.GetBoolean(), DateTimeOffset.FromUnixTimeMilliseconds(item.Int64("time")));
    public static HistoricalOrder HistoricalOrder(JsonElement item) { OrderSnapshot order = Order(item); return new(order.ExchangeOrderId, order.ClientOrderId, order.Symbol, order.Side, order.Type, order.Price, order.Quantity, order.FilledQuantity, Math.Max(0m, order.Quantity - order.FilledQuantity), order.Status, order.ReduceOnly, order.CreatedTimeUtc, DateTimeOffset.FromUnixTimeMilliseconds(item.Int64("updateTime"))); }
    public static HistoricalExecution Execution(JsonElement item) => new(item.String("id") ?? item.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture), item.String("orderId") ?? item.GetProperty("orderId").GetInt64().ToString(CultureInfo.InvariantCulture), null, item.String("symbol") ?? string.Empty, Side(item.String("side")), item.Decimal("price"), item.Decimal("qty"), item.NullableDecimal("commission"), item.String("commissionAsset"), item.TryGetProperty("maker", out JsonElement maker) ? maker.GetBoolean() : null, DateTimeOffset.FromUnixTimeMilliseconds(item.Int64("time")));
    private static TradeSide Side(string? value) => value?.Equals("SELL", StringComparison.OrdinalIgnoreCase) == true ? TradeSide.Sell : TradeSide.Buy;
}

internal sealed class BinanceExchangeStream(IBinanceApi api, TimeProvider timeProvider) : IExchangeStream
{
    private CancellationTokenSource? _cancellation;
    private Task? _pump;
    public bool IsConnected { get; private set; }
    public event EventHandler<OrderUpdate>? OrderUpdated;
    public event EventHandler<ExecutionUpdate>? ExecutionUpdated;
    public event EventHandler<PositionUpdate>? PositionUpdated;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_pump is not null) return;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pump = Task.Run(() => RunPumpAsync(ready, _cancellation.Token), CancellationToken.None);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? source = Interlocked.Exchange(ref _cancellation, null); source?.Cancel();
        Task? pump = Interlocked.Exchange(ref _pump, null);
        if (pump is not null) try { await pump.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); } catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { }
        source?.Dispose(); IsConnected = false;
    }

    private async Task RunPumpAsync(TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await api.RunPrivateStreamAsync(() => { IsConnected = true; ready.TrySetResult(); return Task.CompletedTask; }, HandleAsync, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) { ready.TrySetException(exception); }
            finally { IsConnected = false; }
            if (cancellationToken.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    private Task HandleAsync(JsonElement value)
    {
        string? type = value.String("e");
        DateTimeOffset timestamp = value.TryGetProperty("E", out JsonElement eventTime) ? DateTimeOffset.FromUnixTimeMilliseconds(eventTime.GetInt64()) : timeProvider.GetUtcNow();
        if (type == "ORDER_TRADE_UPDATE" && value.TryGetProperty("o", out JsonElement order))
        {
            string symbol = order.String("s") ?? string.Empty, id = order.String("i") ?? order.GetProperty("i").GetInt64().ToString(CultureInfo.InvariantCulture);
            OrderUpdated?.Invoke(this, new(symbol, id, order.String("X") ?? "Unknown", timestamp));
            decimal quantity = order.NullableDecimal("l") ?? 0m;
            if (quantity > 0m) ExecutionUpdated?.Invoke(this, new(symbol, id, order.NullableDecimal("L") ?? 0m, quantity, timestamp));
        }
        else if (type == "ACCOUNT_UPDATE" && value.TryGetProperty("a", out JsonElement account) && account.TryGetProperty("P", out JsonElement positions))
        {
            foreach (JsonElement position in positions.EnumerateArray()) { decimal amount = position.Decimal("pa"); PositionUpdated?.Invoke(this, new(position.String("s") ?? string.Empty, amount < 0 ? TradeSide.Sell : TradeSide.Buy, Math.Abs(amount), timestamp)); }
        }
        return Task.CompletedTask;
    }
}

internal sealed class BinanceReadOnlyTradingProvider : IExchangeTradingProvider
{
    private static ProviderException Error() => new("CAPABILITY_NOT_SUPPORTED", "Binance is a read-only TradeRelay adapter.");
    public Task<OrderSubmissionResult> PlaceOrderAsync(ExchangeOrderRequest order, CancellationToken cancellationToken) => Task.FromException<OrderSubmissionResult>(Error());
    public Task<OrderSubmissionResult?> GetOrderAsync(string symbol, string? exchangeOrderId, string? clientOrderId, CancellationToken cancellationToken) => Task.FromException<OrderSubmissionResult?>(Error());
    public Task<OperationResult> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken) => Task.FromException<OperationResult>(Error());
    public Task<OperationResult> CancelAllOrdersAsync(string? symbol, CancellationToken cancellationToken) => Task.FromException<OperationResult>(Error());
    public Task<OrderSubmissionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken) => Task.FromException<OrderSubmissionResult>(Error());
    public Task<OperationResult> SetTradingStopAsync(TradingStopRequest request, CancellationToken cancellationToken) => Task.FromException<OperationResult>(Error());
}

internal static class Symbols
{
    public static string Normalize(string symbol)
    {
        string normalized = symbol?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length < 5 || !normalized.EndsWith("USDT", StringComparison.Ordinal) || normalized.Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ProviderException("INVALID_INPUT", "A valid USDT futures symbol is required.");
        return normalized;
    }
}

internal static class JsonExtensions
{
    public static string? String(this JsonElement value, string property) => !value.TryGetProperty(property, out JsonElement item) ? null : item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
    public static decimal Decimal(this JsonElement value, string property) => value.TryGetProperty(property, out JsonElement item) ? item.Decimal() : 0m;
    public static decimal Decimal(this JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result) ? result : 0m;
    public static decimal? NullableDecimal(this JsonElement value, string property) => value.TryGetProperty(property, out JsonElement item) && item.ValueKind != JsonValueKind.Null && decimal.TryParse(item.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result) && result != 0m ? result : null;
    public static long Int64(this JsonElement value, string property) => value.TryGetProperty(property, out JsonElement item) && long.TryParse(item.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : 0L;
}
