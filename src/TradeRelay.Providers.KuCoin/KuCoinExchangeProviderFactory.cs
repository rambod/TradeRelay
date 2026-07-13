using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.KuCoin;

/// <summary>Creates Live-only, read-only KuCoin USDT Futures services.</summary>
public sealed class KuCoinExchangeProviderFactory(TimeProvider timeProvider) : IExchangeProviderFactory
{
    /// <inheritdoc />
    public string ProviderName => "KuCoin";

    /// <inheritdoc />
    public ExchangeProviderDescriptor Descriptor { get; } = new(
        new ExchangeId("kucoin"),
        "KuCoin",
        ProviderCapabilities.MarketData | ProviderCapabilities.AccountRead | ProviderCapabilities.PrivateStream | ProviderCapabilities.History,
        [TradingEnvironment.Live],
        [
            new CredentialFieldDescriptor(ExchangeCredentials.ApiKeyField, "API key", false),
            new CredentialFieldDescriptor(ExchangeCredentials.ApiSecretField, "API secret", true),
            new CredentialFieldDescriptor(ExchangeCredentials.PassphraseField, "API passphrase", true),
        ]);

    /// <inheritdoc />
    public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment)
    {
        RequireLive(environment);
        return new KuCoinMarketDataProvider(new KuCoinApi(null, timeProvider), timeProvider);
    }

    /// <inheritdoc />
    public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials)
    {
        RequireLive(environment);
        ArgumentNullException.ThrowIfNull(credentials);
        _ = credentials[ExchangeCredentials.PassphraseField];
        var api = new KuCoinApi(credentials, timeProvider);
        return new KuCoinExchangeConnection(api, timeProvider);
    }

    private static void RequireLive(TradingEnvironment environment)
    {
        if (environment != TradingEnvironment.Live) throw new ProviderException("CAPABILITY_NOT_SUPPORTED", "KuCoin supports one Live read-only profile.");
    }
}

internal interface IKuCoinApi : IAsyncDisposable
{
    Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken);
    Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken);
}

internal sealed class KuCoinApi : IKuCoinApi
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api-futures.kucoin.com"), Timeout = TimeSpan.FromSeconds(15) };
    private readonly ExchangeCredentialSet? _credentials;
    private readonly TimeProvider _timeProvider;
    public KuCoinApi(ExchangeCredentialSet? credentials, TimeProvider timeProvider) { _credentials = credentials; _timeProvider = timeProvider; }

    public Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken) => SendAsync(HttpMethod.Get, path, parameters, authenticated, cancellationToken);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, IReadOnlyDictionary<string, string?>? parameters, bool authenticated, CancellationToken cancellationToken)
    {
        string query = parameters is null ? string.Empty : string.Join('&', parameters.Where(item => item.Value is not null).Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
        string endpoint = path + (query.Length == 0 ? string.Empty : "?" + query);
        using var request = new HttpRequestMessage(method, endpoint);
        if (authenticated)
        {
            if (_credentials is null) throw new ProviderException("CREDENTIALS_MISSING", "KuCoin credentials are required.");
            string timestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            string secret = _credentials[ExchangeCredentials.ApiSecretField];
            request.Headers.Add("KC-API-KEY", _credentials[ExchangeCredentials.ApiKeyField]);
            request.Headers.Add("KC-API-TIMESTAMP", timestamp);
            request.Headers.Add("KC-API-SIGN", Base64Hmac(secret, timestamp + method.Method.ToUpperInvariant() + endpoint));
            request.Headers.Add("KC-API-PASSPHRASE", Base64Hmac(secret, _credentials[ExchangeCredentials.PassphraseField]));
            request.Headers.Add("KC-API-KEY-VERSION", "2");
        }
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw Error(response.StatusCode, payload);
        using JsonDocument document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("code", out JsonElement code) || code.GetString() != "200000") throw Error(response.StatusCode, payload);
        return document.RootElement.TryGetProperty("data", out JsonElement data) ? data.Clone() : default;
    }

    public async Task RunPrivateStreamAsync(Func<Task> onConnected, Func<JsonElement, Task> onMessage, CancellationToken cancellationToken)
    {
        JsonElement tokenData = await SendAsync(HttpMethod.Post, "/api/v1/bullet-private", null, true, cancellationToken).ConfigureAwait(false);
        string token = tokenData.GetProperty("token").GetString() ?? throw new ProviderException("PROVIDER_UNAVAILABLE", "KuCoin returned no private-stream token.");
        JsonElement server = tokenData.GetProperty("instanceServers")[0];
        string endpoint = server.GetProperty("endpoint").GetString() ?? throw new ProviderException("PROVIDER_UNAVAILABLE", "KuCoin returned no private-stream endpoint.");
        var uri = new Uri($"{endpoint}?token={Uri.EscapeDataString(token)}&connectId={Guid.NewGuid():N}");
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        foreach (string topic in new[] { "/contractMarket/tradeOrders", "/contract/positionAll" })
        {
            byte[] subscribe = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), type = "subscribe", topic, privateChannel = true, response = true }));
            await socket.SendAsync(subscribe, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        await onConnected().ConfigureAwait(false);
        int pingInterval = server.TryGetProperty("pingInterval", out JsonElement interval) && interval.TryGetInt32(out int milliseconds) ? Math.Max(5_000, milliseconds - 1_000) : 15_000;
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeat = SendHeartbeatsAsync(socket, pingInterval, heartbeatCancellation.Token);
        try
        {
            byte[] buffer = new byte[32 * 1024];
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream(); WebSocketReceiveResult result;
                do { result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false); if (result.MessageType == WebSocketMessageType.Close) return; message.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
                using JsonDocument document = JsonDocument.Parse(message.ToArray());
                await onMessage(document.RootElement.Clone()).ConfigureAwait(false);
            }
        }
        finally
        {
            heartbeatCancellation.Cancel();
            try { await heartbeat.ConfigureAwait(false); }
            catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested) { }
        }
    }

    private static async Task SendHeartbeatsAsync(ClientWebSocket socket, int intervalMilliseconds, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(intervalMilliseconds), cancellationToken).ConfigureAwait(false);
            byte[] ping = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), type = "ping" }));
            try { await socket.SendAsync(ping, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
            catch { socket.Abort(); throw; }
        }
    }

    public ValueTask DisposeAsync() { _http.Dispose(); return ValueTask.CompletedTask; }
    private static string Base64Hmac(string secret, string value) { using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)); return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))); }
    private static ProviderException Error(HttpStatusCode status, string payload)
    {
        if (status == HttpStatusCode.TooManyRequests || payload.Contains("429000", StringComparison.Ordinal)) return new("RATE_LIMITED", "KuCoin rate-limited the read request.");
        string code = payload.Contains("400003", StringComparison.Ordinal) || payload.Contains("400004", StringComparison.Ordinal) || payload.Contains("400005", StringComparison.Ordinal) ? "CREDENTIALS_INVALID" : "PROVIDER_UNAVAILABLE";
        return new(code, code == "CREDENTIALS_INVALID" ? "KuCoin rejected the API credentials." : "KuCoin could not complete the read request.");
    }
}

internal sealed class KuCoinExchangeConnection : IExchangeProviderConnection
{
    private readonly IKuCoinApi _api;
    public KuCoinExchangeConnection(IKuCoinApi api, TimeProvider timeProvider)
    {
        _api = api; Account = new KuCoinTradingAccountProvider(api, timeProvider); History = new KuCoinHistoryProvider(api); Stream = new KuCoinExchangeStream(api, timeProvider); Trading = new KuCoinReadOnlyTradingProvider();
    }
    public ITradingAccountProvider Account { get; }
    public IExchangeTradingProvider Trading { get; }
    public IExchangeStream Stream { get; }
    public IExchangeHistoryProvider? History { get; }
    public async ValueTask DisposeAsync() { await Stream.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); await _api.DisposeAsync().ConfigureAwait(false); }
}

internal sealed class KuCoinMarketDataProvider(IKuCoinApi api, TimeProvider timeProvider) : IMarketDataProvider, IAsyncDisposable
{
    public async Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken)
    {
        string normalized = KuCoinSymbols.Normalize(symbol), native = KuCoinSymbols.ToNative(normalized);
        JsonElement value = await api.GetAsync("/api/v1/ticker", P(("symbol", native)), false, cancellationToken).ConfigureAwait(false);
        DateTimeOffset timestamp = value.TryGetProperty("ts", out JsonElement ts) ? KuCoinJson.Time(ts) : timeProvider.GetUtcNow();
        return new(normalized, value.Decimal("price"), value.NullableDecimal("bestBidPrice"), value.NullableDecimal("bestAskPrice"), null, null, value.NullableDecimal("size"), timestamp);
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken)
    {
        string normalized = KuCoinSymbols.Normalize(symbol), native = KuCoinSymbols.ToNative(normalized); limit = Math.Clamp(limit, 1, 200); int seconds = Seconds(interval); long to = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        JsonElement values = await api.GetAsync("/api/v1/kline/query", P(("symbol", native), ("granularity", seconds.ToString(CultureInfo.InvariantCulture)), ("from", (to - (long)seconds * limit).ToString(CultureInfo.InvariantCulture)), ("to", to.ToString(CultureInfo.InvariantCulture))), false, cancellationToken).ConfigureAwait(false);
        return values.EnumerateArray().Select(row => new Candle(normalized, interval, DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()), DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()).AddSeconds(seconds), row[1].Decimal(), row[2].Decimal(), row[3].Decimal(), row[4].Decimal(), row[5].Decimal())).OrderBy(item => item.OpenTimeUtc).TakeLast(limit).ToArray();
    }

    public async Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken)
    {
        string normalized = KuCoinSymbols.Normalize(symbol);
        JsonElement value = await api.GetAsync("/api/v1/contracts/" + Uri.EscapeDataString(KuCoinSymbols.ToNative(normalized)), null, false, cancellationToken).ConfigureAwait(false);
        decimal multiplier = value.NullableDecimal("multiplier") ?? 1m;
        return new(normalized, value.TryGetProperty("status", out JsonElement status) ? status.ToString() : value.TryGetProperty("isOpen", out JsonElement open) && open.GetBoolean() ? "Trading" : "Unavailable", value.Decimal("tickSize"), value.Decimal("lotSize") * multiplier, value.Decimal("lotSize") * multiplier, value.Decimal("maxOrderQty") * multiplier, value.NullableDecimal("maxMarketOrderQty") * multiplier, null, value.NullableDecimal("maxLeverage"), "USDT linear perpetual");
    }

    public async Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken)
    {
        string normalized = KuCoinSymbols.Normalize(symbol); depth = Math.Clamp(depth, 1, 50);
        JsonElement value = await api.GetAsync("/api/v1/level2/depth50", P(("symbol", KuCoinSymbols.ToNative(normalized))), false, cancellationToken).ConfigureAwait(false);
        return new(normalized, Levels(value.GetProperty("bids"), depth), Levels(value.GetProperty("asks"), depth), timeProvider.GetUtcNow());
    }

    private static IReadOnlyList<OrderBookLevel> Levels(JsonElement values, int depth) => values.EnumerateArray().Take(depth).Select(row => new OrderBookLevel(row[0].Decimal(), row[1].Decimal())).ToArray();
    private static int Seconds(CandleInterval value) => value switch { CandleInterval.OneMinute => 60, CandleInterval.ThreeMinutes => 180, CandleInterval.FiveMinutes => 300, CandleInterval.FifteenMinutes => 900, CandleInterval.ThirtyMinutes => 1800, CandleInterval.OneHour => 3600, CandleInterval.TwoHours => 7200, CandleInterval.FourHours => 14400, CandleInterval.SixHours => 21600, CandleInterval.TwelveHours => 43200, CandleInterval.OneDay => 86400, CandleInterval.OneWeek => 604800, _ => throw new ProviderException("INVALID_INPUT", "Unsupported candle interval.") };
    private static Dictionary<string, string?> P(params (string Key, string? Value)[] values) => values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    public ValueTask DisposeAsync() => api.DisposeAsync();
}

internal sealed class KuCoinTradingAccountProvider(IKuCoinApi api, TimeProvider timeProvider) : ITradingAccountProvider
{
    private Task<JsonElement> Overview(CancellationToken cancellationToken) => api.GetAsync("/api/v1/account-overview", new Dictionary<string, string?> { ["currency"] = "USDT" }, true, cancellationToken);

    public async Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await Overview(cancellationToken).ConfigureAwait(false); decimal equity = value.Decimal("accountEquity"), positionMargin = value.Decimal("positionMargin"), orderMargin = value.Decimal("orderMargin");
        return new(equity, value.Decimal("availableBalance"), value.Decimal("unrealisedPNL"), equity == 0 ? null : (positionMargin + orderMargin) / equity * 100m, TradingEnvironment.Live, timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await Overview(cancellationToken).ConfigureAwait(false);
        return [new("USDT", value.Decimal("accountEquity"), value.Decimal("marginBalance"), value.Decimal("availableBalance"), value.Decimal("unrealisedPNL"), value.NullableDecimal("accountEquity"))];
    }

    public async Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken)
    {
        string? normalized = string.IsNullOrWhiteSpace(symbol) ? null : KuCoinSymbols.Normalize(symbol);
        JsonElement values = await api.GetAsync("/api/v1/positions", null, true, cancellationToken).ConfigureAwait(false); var results = new List<PositionSnapshot>();
        foreach (JsonElement item in values.EnumerateArray())
        {
            string itemSymbol = KuCoinSymbols.FromNative(item.String("symbol") ?? string.Empty); if (normalized is not null && itemSymbol != normalized) continue;
            decimal quantity = item.Decimal("currentQty"); if (quantity == 0) continue;
            decimal multiplier = await MultiplierAsync(item.String("symbol") ?? string.Empty, cancellationToken).ConfigureAwait(false);
            results.Add(new(itemSymbol, quantity < 0 ? TradeSide.Sell : TradeSide.Buy, Math.Abs(quantity) * multiplier, item.Decimal("avgEntryPrice"), item.Decimal("markPrice"), item.NullableDecimal("realLeverage") ?? item.Decimal("leverage"), item.Decimal("unrealisedPnl"), item.NullableDecimal("liquidationPrice"), null, null, item.String("positionSide") ?? "BOTH"));
        }
        return results;
    }

    public async Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken)
    {
        string? native = string.IsNullOrWhiteSpace(symbol) ? null : KuCoinSymbols.ToNative(KuCoinSymbols.Normalize(symbol));
        JsonElement data = await api.GetAsync("/api/v1/orders", new Dictionary<string, string?> { ["status"] = "active", ["symbol"] = native, ["pageSize"] = "100" }, true, cancellationToken).ConfigureAwait(false);
        JsonElement items = data.ValueKind == JsonValueKind.Array ? data : data.GetProperty("items"); var results = new List<OrderSnapshot>();
        foreach (JsonElement item in items.EnumerateArray()) { decimal multiplier = await MultiplierAsync(item.String("symbol") ?? string.Empty, cancellationToken).ConfigureAwait(false); results.Add(KuCoinMappings.Order(item, multiplier)); }
        return results;
    }

    public async Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken)
    {
        _ = await Overview(cancellationToken).ConfigureAwait(false);
        return new(false, true, true, false, false, null, null, true, TradingEnvironment.Live, ["KuCoin Futures account reads do not report the key's IP binding or withdrawal scope. Verify General-only restrictions in KuCoin API Management; this adapter never performs writes."]);
    }

    private async Task<decimal> MultiplierAsync(string native, CancellationToken cancellationToken) { JsonElement contract = await api.GetAsync("/api/v1/contracts/" + Uri.EscapeDataString(native), null, false, cancellationToken).ConfigureAwait(false); return contract.NullableDecimal("multiplier") ?? 1m; }
}

internal sealed class KuCoinHistoryProvider(IKuCoinApi api) : IExchangeHistoryProvider
{
    public async Task<IReadOnlyList<HistoricalOrder>> GetOrderHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken)
    {
        JsonElement data = await api.GetAsync("/api/v1/orders", Query(query, true), true, cancellationToken).ConfigureAwait(false); JsonElement items = data.ValueKind == JsonValueKind.Array ? data : data.GetProperty("items");
        var results = new List<HistoricalOrder>();
        foreach (JsonElement item in items.EnumerateArray()) results.Add(KuCoinMappings.HistoricalOrder(item, await MultiplierAsync(item.String("symbol") ?? string.Empty, cancellationToken).ConfigureAwait(false)));
        return results.OrderByDescending(item => item.UpdatedTimeUtc).Take(Math.Clamp(query.Limit, 1, 200)).ToArray();
    }
    public async Task<IReadOnlyList<HistoricalExecution>> GetExecutionHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken)
    {
        JsonElement data = await api.GetAsync("/api/v1/fills", Query(query, false), true, cancellationToken).ConfigureAwait(false); JsonElement items = data.ValueKind == JsonValueKind.Array ? data : data.GetProperty("items");
        var results = new List<HistoricalExecution>();
        foreach (JsonElement item in items.EnumerateArray()) results.Add(KuCoinMappings.Execution(item, await MultiplierAsync(item.String("symbol") ?? string.Empty, cancellationToken).ConfigureAwait(false)));
        return results.OrderByDescending(item => item.TimestampUtc).Take(Math.Clamp(query.Limit, 1, 200)).ToArray();
    }
    private async Task<decimal> MultiplierAsync(string native, CancellationToken cancellationToken) { JsonElement contract = await api.GetAsync("/api/v1/contracts/" + Uri.EscapeDataString(native), null, false, cancellationToken).ConfigureAwait(false); return contract.NullableDecimal("multiplier") ?? 1m; }
    private static Dictionary<string, string?> Query(ExchangeHistoryQuery query, bool orders) => new(StringComparer.Ordinal) { ["status"] = orders ? "done" : null, ["symbol"] = string.IsNullOrWhiteSpace(query.Symbol) ? null : KuCoinSymbols.ToNative(KuCoinSymbols.Normalize(query.Symbol)), ["startAt"] = query.FromUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ["endAt"] = query.ToUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), ["pageSize"] = Math.Clamp(query.Limit, 1, 200).ToString(CultureInfo.InvariantCulture) };
}

internal static class KuCoinMappings
{
    public static OrderSnapshot Order(JsonElement item, decimal multiplier) { decimal quantity = item.Decimal("size") * multiplier, filled = item.Decimal("dealSize") * multiplier; return new(item.String("id") ?? item.String("orderId") ?? string.Empty, item.String("clientOid"), KuCoinSymbols.FromNative(item.String("symbol") ?? string.Empty), Side(item.String("side")), item.String("type") ?? item.String("orderType") ?? "Unknown", item.NullableDecimal("price"), quantity, filled, item.TryGetProperty("isActive", out JsonElement active) && active.GetBoolean() ? "Active" : item.String("status") ?? "Done", item.TryGetProperty("reduceOnly", out JsonElement reduce) && reduce.GetBoolean(), KuCoinJson.Time(item.TryGetProperty("createdAt", out JsonElement created) ? created : default)); }
    public static HistoricalOrder HistoricalOrder(JsonElement item, decimal multiplier) { OrderSnapshot order = Order(item, multiplier); DateTimeOffset updated = item.TryGetProperty("updatedAt", out JsonElement update) ? KuCoinJson.Time(update) : order.CreatedTimeUtc; return new(order.ExchangeOrderId, order.ClientOrderId, order.Symbol, order.Side, order.Type, order.Price, order.Quantity, order.FilledQuantity, Math.Max(0m, order.Quantity - order.FilledQuantity), order.Status, order.ReduceOnly, order.CreatedTimeUtc, updated); }
    public static HistoricalExecution Execution(JsonElement item, decimal multiplier) => new(item.String("tradeId") ?? string.Empty, item.String("orderId") ?? string.Empty, item.String("clientOid"), KuCoinSymbols.FromNative(item.String("symbol") ?? string.Empty), Side(item.String("side")), item.Decimal("price"), item.Decimal("size") * multiplier, item.NullableDecimal("fee"), item.String("feeCurrency"), item.String("liquidity") is string liquidity ? liquidity.Equals("maker", StringComparison.OrdinalIgnoreCase) : null, item.TryGetProperty("createdAt", out JsonElement created) ? KuCoinJson.Time(created) : KuCoinJson.Time(item.GetProperty("tradeTime")));
    private static TradeSide Side(string? side) => side?.Equals("sell", StringComparison.OrdinalIgnoreCase) == true ? TradeSide.Sell : TradeSide.Buy;
}

internal sealed class KuCoinExchangeStream(IKuCoinApi api, TimeProvider timeProvider) : IExchangeStream
{
    private CancellationTokenSource? _cancellation; private Task? _pump;
    private readonly ConcurrentDictionary<string, decimal> _multipliers = new(StringComparer.Ordinal);
    public bool IsConnected { get; private set; }
    public event EventHandler<OrderUpdate>? OrderUpdated; public event EventHandler<ExecutionUpdate>? ExecutionUpdated; public event EventHandler<PositionUpdate>? PositionUpdated;
    public async Task ConnectAsync(CancellationToken cancellationToken) { if (_pump is not null) return; _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _pump = Task.Run(() => RunPumpAsync(ready, _cancellation.Token), CancellationToken.None); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
    public async Task DisconnectAsync(CancellationToken cancellationToken) { CancellationTokenSource? source = Interlocked.Exchange(ref _cancellation, null); source?.Cancel(); Task? pump = Interlocked.Exchange(ref _pump, null); if (pump is not null) try { await pump.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); } catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { } source?.Dispose(); IsConnected = false; }
    private async Task RunPumpAsync(TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await api.RunPrivateStreamAsync(() => { IsConnected = true; ready.TrySetResult(); return Task.CompletedTask; }, HandleAsync, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) { ready.TrySetException(exception); }
            finally { IsConnected = false; }
            if (cancellationToken.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }
    private async Task HandleAsync(JsonElement message)
    {
        if (!message.TryGetProperty("data", out JsonElement data)) return;
        string topic = message.String("topic") ?? string.Empty;
        bool isOrder = topic.Contains("tradeOrders", StringComparison.Ordinal);
        bool isPosition = topic.Contains("position", StringComparison.OrdinalIgnoreCase);
        if (!isOrder && !isPosition) return;
        string native = data.String("symbol") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(native)) return;
        string symbol = KuCoinSymbols.FromNative(native);
        DateTimeOffset timestamp = data.TryGetProperty("ts", out JsonElement ts) ? KuCoinJson.Time(ts) : timeProvider.GetUtcNow();
        decimal multiplier = await GetMultiplierAsync(native).ConfigureAwait(false);
        if (isOrder)
        {
            string orderId = data.String("orderId") ?? string.Empty;
            OrderUpdated?.Invoke(this, new(symbol, orderId, data.String("status") ?? data.String("type") ?? "Updated", timestamp));
            decimal quantity = (data.NullableDecimal("matchSize") ?? 0m) * multiplier;
            if (quantity > 0) ExecutionUpdated?.Invoke(this, new(symbol, orderId, data.NullableDecimal("matchPrice") ?? 0m, quantity, timestamp));
        }
        else if (isPosition)
        {
            decimal quantity = data.Decimal("currentQty");
            PositionUpdated?.Invoke(this, new(symbol, quantity < 0 ? TradeSide.Sell : TradeSide.Buy, Math.Abs(quantity) * multiplier, timestamp));
        }
    }

    private async Task<decimal> GetMultiplierAsync(string native)
    {
        if (_multipliers.TryGetValue(native, out decimal multiplier)) return multiplier;
        JsonElement contract = await api.GetAsync("/api/v1/contracts/" + Uri.EscapeDataString(native), null, false, CancellationToken.None).ConfigureAwait(false);
        multiplier = contract.NullableDecimal("multiplier") ?? 1m;
        _multipliers[native] = multiplier;
        return multiplier;
    }
}

internal sealed class KuCoinReadOnlyTradingProvider : IExchangeTradingProvider
{
    private static ProviderException Error() => new("CAPABILITY_NOT_SUPPORTED", "KuCoin is a read-only TradeRelay adapter.");
    public Task<OrderSubmissionResult> PlaceOrderAsync(ExchangeOrderRequest order, CancellationToken cancellationToken) => Task.FromException<OrderSubmissionResult>(Error());
    public Task<OrderSubmissionResult?> GetOrderAsync(string symbol, string? exchangeOrderId, string? clientOrderId, CancellationToken cancellationToken) => Task.FromException<OrderSubmissionResult?>(Error());
    public Task<OperationResult> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken) => Task.FromException<OperationResult>(Error());
    public Task<OperationResult> CancelAllOrdersAsync(string? symbol, CancellationToken cancellationToken) => Task.FromException<OperationResult>(Error());
    public Task<OrderSubmissionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken) => Task.FromException<OrderSubmissionResult>(Error());
    public Task<OperationResult> SetTradingStopAsync(TradingStopRequest request, CancellationToken cancellationToken) => Task.FromException<OperationResult>(Error());
}

internal static class KuCoinSymbols
{
    public static string Normalize(string symbol) { string value = symbol?.Trim().ToUpperInvariant() ?? string.Empty; if (value.Length < 5 || !value.EndsWith("USDT", StringComparison.Ordinal) || value.Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ProviderException("INVALID_INPUT", "A valid USDT futures symbol is required."); return value; }
    public static string ToNative(string normalized) { string baseAsset = normalized[..^4]; if (baseAsset == "BTC") baseAsset = "XBT"; return baseAsset + "USDTM"; }
    public static string FromNative(string native) { string value = native.ToUpperInvariant(); if (value.EndsWith("M", StringComparison.Ordinal)) value = value[..^1]; if (value.StartsWith("XBT", StringComparison.Ordinal)) value = "BTC" + value[3..]; return value; }
}

internal static class KuCoinJson
{
    public static string? String(this JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) ? item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString() : null;
    public static decimal Decimal(this JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) ? item.Decimal() : 0m;
    public static decimal Decimal(this JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result) ? result : 0m;
    public static decimal? NullableDecimal(this JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) && decimal.TryParse(item.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal result) && result != 0m ? result : null;
    public static DateTimeOffset Time(JsonElement value) { if (!long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw) || raw <= 0) return DateTimeOffset.UnixEpoch; while (raw > 253402300799999L) raw /= 1000; return raw > 253402300799L ? DateTimeOffset.FromUnixTimeMilliseconds(raw) : DateTimeOffset.FromUnixTimeSeconds(raw); }
}
