using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.Bybit;

internal sealed class BybitMarketDataProvider(IBybitRestClient client, TimeProvider timeProvider) : IMarketDataProvider, IDisposable
{
    public void Dispose() => client.Dispose();
    public async Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken)
    {
        symbol = BybitValidation.NormalizeSymbol(symbol);
        var result = await client.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, symbol, null, null, cancellationToken).ConfigureAwait(false);
        BybitLinearInverseTicker ticker = BybitResult.Require(result).List.SingleOrDefault() ?? throw new ProviderException("INVALID_INPUT", "The requested USDT perpetual instrument was not found.");
        return new TickerSnapshot(ticker.Symbol, ticker.LastPrice, ticker.BestBidPrice, ticker.BestAskPrice, ticker.HighPrice24h, ticker.LowPrice24h, ticker.Volume24h, timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken)
    {
        symbol = BybitValidation.NormalizeSymbol(symbol);
        if (limit is < 1 or > 200) throw new ProviderException("INVALID_INPUT", "Candle limit must be between 1 and 200.");
        var result = await client.V5Api.ExchangeData.GetKlinesAsync(Category.Linear, symbol, Map(interval), limit: limit, ct: cancellationToken).ConfigureAwait(false);
        TimeSpan duration = Duration(interval);
        return BybitResult.Require(result).List
            .OrderBy(candle => candle.StartTime)
            .Select(candle => new Candle(symbol, interval, ToUtc(candle.StartTime), ToUtc(candle.StartTime).Add(duration), candle.OpenPrice, candle.HighPrice, candle.LowPrice, candle.ClosePrice, candle.Volume))
            .ToArray();
    }

    public async Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken)
    {
        symbol = BybitValidation.NormalizeSymbol(symbol);
        var result = await client.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, symbol: symbol, limit: 1, ct: cancellationToken).ConfigureAwait(false);
        BybitLinearInverseSymbol instrument = BybitResult.Require(result).List.SingleOrDefault() ?? throw new ProviderException("INVALID_INPUT", "The requested USDT perpetual instrument was not found.");
        BybitLinearInverseLotSizeFilter lot = instrument.LotSizeFilter ?? throw new ProviderException("PROVIDER_UNAVAILABLE", "Instrument quantity limits are unavailable.");
        BybitLinearInversePriceFilter price = instrument.PriceFilter ?? throw new ProviderException("PROVIDER_UNAVAILABLE", "Instrument price limits are unavailable.");
        return new InstrumentInfo(instrument.Name, instrument.Status.ToString(), price.TickSize, lot.QuantityStep, lot.MinOrderQuantity, lot.MaxOrderQuantity, lot.MaxMarketOrderQuantity, lot.MinNotionalValue, instrument.LeverageFilter?.MaxLeverage, instrument.ContractType.ToString());
    }

    public async Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken)
    {
        symbol = BybitValidation.NormalizeSymbol(symbol);
        if (depth is < 1 or > 50) throw new ProviderException("INVALID_INPUT", "Order-book depth must be between 1 and 50.");
        var result = await client.V5Api.ExchangeData.GetOrderbookAsync(Category.Linear, symbol, depth, cancellationToken).ConfigureAwait(false);
        BybitOrderbook book = BybitResult.Require(result);
        return new OrderBookSnapshot(book.Symbol, book.Bids.Select(x => new OrderBookLevel(x.Price, x.Quantity)).ToArray(), book.Asks.Select(x => new OrderBookLevel(x.Price, x.Quantity)).ToArray(), ToUtc(book.Timestamp));
    }

    private static KlineInterval Map(CandleInterval interval) => interval switch
    {
        CandleInterval.OneMinute => KlineInterval.OneMinute,
        CandleInterval.ThreeMinutes => KlineInterval.ThreeMinutes,
        CandleInterval.FiveMinutes => KlineInterval.FiveMinutes,
        CandleInterval.FifteenMinutes => KlineInterval.FifteenMinutes,
        CandleInterval.ThirtyMinutes => KlineInterval.ThirtyMinutes,
        CandleInterval.OneHour => KlineInterval.OneHour,
        CandleInterval.TwoHours => KlineInterval.TwoHours,
        CandleInterval.FourHours => KlineInterval.FourHours,
        CandleInterval.SixHours => KlineInterval.SixHours,
        CandleInterval.TwelveHours => KlineInterval.TwelveHours,
        CandleInterval.OneDay => KlineInterval.OneDay,
        CandleInterval.OneWeek => KlineInterval.OneWeek,
        _ => throw new ProviderException("INVALID_INPUT", "The candle interval is not supported.")
    };

    private static TimeSpan Duration(CandleInterval interval) => interval switch
    {
        CandleInterval.OneMinute => TimeSpan.FromMinutes(1),
        CandleInterval.ThreeMinutes => TimeSpan.FromMinutes(3),
        CandleInterval.FiveMinutes => TimeSpan.FromMinutes(5),
        CandleInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
        CandleInterval.ThirtyMinutes => TimeSpan.FromMinutes(30),
        CandleInterval.OneHour => TimeSpan.FromHours(1),
        CandleInterval.TwoHours => TimeSpan.FromHours(2),
        CandleInterval.FourHours => TimeSpan.FromHours(4),
        CandleInterval.SixHours => TimeSpan.FromHours(6),
        CandleInterval.TwelveHours => TimeSpan.FromHours(12),
        CandleInterval.OneDay => TimeSpan.FromDays(1),
        CandleInterval.OneWeek => TimeSpan.FromDays(7),
        _ => TimeSpan.Zero
    };

    private static DateTimeOffset ToUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
