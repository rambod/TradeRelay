using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class MarketTools(IExchangeSessionCoordinator sessions, AppSettings settings, TimeProvider timeProvider)
{
    [McpServerTool(Name = "get_ticker", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets a current USDT perpetual ticker from the selected or explicit exchange.")]
    public Task<ToolResult<TickerSnapshot>> GetTickerAsync([Description("USDT perpetual symbol, for example BTCUSDT")] string symbol, string? exchange = null, CancellationToken cancellationToken = default) =>
        RunAsync(session => session.MarketData.GetTickerAsync(symbol, cancellationToken), "Ticker retrieved.", exchange);

    [McpServerTool(Name = "get_candles", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets oldest-first USDT perpetual candles from the selected or explicit exchange.")]
    public Task<ToolResult<IReadOnlyList<Candle>>> GetCandlesAsync(string symbol, string interval = "15m", int limit = 100, string? exchange = null, CancellationToken cancellationToken = default)
    {
        if (!TryParseInterval(interval, out CandleInterval parsed))
            return Task.FromResult(ToolResponse.Failure<IReadOnlyList<Candle>>("INVALID_INPUT", "Supported intervals: 1m, 3m, 5m, 15m, 30m, 1h, 2h, 4h, 6h, 12h, 1d, 1w.", settings.Bybit.Environment, timeProvider));
        return RunAsync(session => session.MarketData.GetCandlesAsync(symbol, parsed, limit, cancellationToken), "Candles retrieved.", exchange);
    }

    [McpServerTool(Name = "get_instrument_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets precision and limit metadata for a USDT perpetual instrument.")]
    public Task<ToolResult<InstrumentInfo>> GetInstrumentInfoAsync(string symbol, string? exchange = null, CancellationToken cancellationToken = default) =>
        RunAsync(session => session.MarketData.GetInstrumentInfoAsync(symbol, cancellationToken), "Instrument information retrieved.", exchange);

    [McpServerTool(Name = "get_order_book", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets a USDT perpetual order-book snapshot with a maximum depth of 50.")]
    public Task<ToolResult<OrderBookSnapshot>> GetOrderBookAsync(string symbol, int depth = 25, string? exchange = null, CancellationToken cancellationToken = default) =>
        RunAsync(session => session.MarketData.GetOrderBookAsync(symbol, depth, cancellationToken), "Order book retrieved.", exchange);

    private Task<ToolResult<T>> RunAsync<T>(Func<ProviderSessionAccess, Task<T>> action, string message, string? exchange)
    {
        if (!sessions.TryResolve(exchange, out ProviderSessionAccess? session, out string code, out string error) || session is null)
            return Task.FromResult(ToolResponse.Failure<T>(code, error, settings.Bybit.Environment, timeProvider));
        return ToolResponse.RunAsync(_ => action(session), message, session.Environment, timeProvider, CancellationToken.None);
    }

    private static bool TryParseInterval(string value, out CandleInterval interval)
    {
        Dictionary<string, CandleInterval> values = new(StringComparer.OrdinalIgnoreCase) { ["1m"] = CandleInterval.OneMinute, ["3m"] = CandleInterval.ThreeMinutes, ["5m"] = CandleInterval.FiveMinutes, ["15m"] = CandleInterval.FifteenMinutes, ["30m"] = CandleInterval.ThirtyMinutes, ["1h"] = CandleInterval.OneHour, ["2h"] = CandleInterval.TwoHours, ["4h"] = CandleInterval.FourHours, ["6h"] = CandleInterval.SixHours, ["12h"] = CandleInterval.TwelveHours, ["1d"] = CandleInterval.OneDay, ["1w"] = CandleInterval.OneWeek };
        return values.TryGetValue(value?.Trim() ?? string.Empty, out interval);
    }
}
