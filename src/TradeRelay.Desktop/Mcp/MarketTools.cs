using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class MarketTools(ExchangeConnectionManager manager, AppSettings settings, TimeProvider timeProvider)
{
    [McpServerTool(Name = "get_ticker", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets a current Bybit USDT perpetual ticker snapshot.")]
    public Task<ToolResult<TickerSnapshot>> GetTickerAsync([Description("USDT perpetual symbol, for example BTCUSDT")] string symbol, CancellationToken cancellationToken) =>
        ToolResponse.RunAsync(ct => manager.MarketData.GetTickerAsync(symbol, ct), "Ticker retrieved.", settings.Bybit.Environment, timeProvider, cancellationToken);

    [McpServerTool(Name = "get_candles", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets oldest-first Bybit USDT perpetual candles.")]
    public Task<ToolResult<IReadOnlyList<Candle>>> GetCandlesAsync(string symbol, string interval = "15m", int limit = 100, CancellationToken cancellationToken = default)
    {
        if (!TryParseInterval(interval, out CandleInterval parsed))
            return Task.FromResult(ToolResponse.Failure<IReadOnlyList<Candle>>("INVALID_INPUT", "Supported intervals: 1m, 3m, 5m, 15m, 30m, 1h, 2h, 4h, 6h, 12h, 1d, 1w.", settings.Bybit.Environment, timeProvider));
        return ToolResponse.RunAsync(ct => manager.MarketData.GetCandlesAsync(symbol, parsed, limit, ct), "Candles retrieved.", settings.Bybit.Environment, timeProvider, cancellationToken);
    }

    [McpServerTool(Name = "get_instrument_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets precision and limit metadata for a Bybit USDT perpetual instrument.")]
    public Task<ToolResult<InstrumentInfo>> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken) =>
        ToolResponse.RunAsync(ct => manager.MarketData.GetInstrumentInfoAsync(symbol, ct), "Instrument information retrieved.", settings.Bybit.Environment, timeProvider, cancellationToken);

    [McpServerTool(Name = "get_order_book", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets a Bybit USDT perpetual order-book snapshot with a maximum depth of 50.")]
    public Task<ToolResult<OrderBookSnapshot>> GetOrderBookAsync(string symbol, int depth = 25, CancellationToken cancellationToken = default) =>
        ToolResponse.RunAsync(ct => manager.MarketData.GetOrderBookAsync(symbol, depth, ct), "Order book retrieved.", settings.Bybit.Environment, timeProvider, cancellationToken);

    private static bool TryParseInterval(string value, out CandleInterval interval)
    {
        Dictionary<string, CandleInterval> values = new(StringComparer.OrdinalIgnoreCase) { ["1m"] = CandleInterval.OneMinute, ["3m"] = CandleInterval.ThreeMinutes, ["5m"] = CandleInterval.FiveMinutes, ["15m"] = CandleInterval.FifteenMinutes, ["30m"] = CandleInterval.ThirtyMinutes, ["1h"] = CandleInterval.OneHour, ["2h"] = CandleInterval.TwoHours, ["4h"] = CandleInterval.FourHours, ["6h"] = CandleInterval.SixHours, ["12h"] = CandleInterval.TwelveHours, ["1d"] = CandleInterval.OneDay, ["1w"] = CandleInterval.OneWeek };
        return values.TryGetValue(value?.Trim() ?? string.Empty, out interval);
    }
}
