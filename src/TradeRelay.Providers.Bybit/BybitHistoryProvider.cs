using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.Bybit;

internal sealed class BybitHistoryProvider(IBybitRestClient client) : IExchangeHistoryProvider
{
    public async Task<IReadOnlyList<HistoricalOrder>> GetOrderHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string? symbol = string.IsNullOrWhiteSpace(query.Symbol) ? null : BybitValidation.NormalizeSymbol(query.Symbol);
        int limit = Math.Clamp(query.Limit, 1, 100);
        var result = await client.V5Api.Trading.GetOrderHistoryAsync(
            Category.Linear,
            symbol: symbol,
            startTime: query.FromUtc?.UtcDateTime,
            endTime: query.ToUtc?.UtcDateTime,
            limit: limit,
            ct: cancellationToken).ConfigureAwait(false);
        return BybitResult.Require(result).List.Select(order => new HistoricalOrder(
            order.OrderId,
            order.ClientOrderId,
            order.Symbol,
            order.Side == OrderSide.Sell ? TradeSide.Sell : TradeSide.Buy,
            order.OrderType.ToString(),
            order.Price,
            order.Quantity,
            order.QuantityFilled ?? 0m,
            order.QuantityRemaining ?? 0m,
            order.Status.ToString(),
            order.ReduceOnly ?? false,
            Utc(order.CreateTime),
            Utc(order.UpdateTime))).ToArray();
    }

    public async Task<IReadOnlyList<HistoricalExecution>> GetExecutionHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string? symbol = string.IsNullOrWhiteSpace(query.Symbol) ? null : BybitValidation.NormalizeSymbol(query.Symbol);
        int limit = Math.Clamp(query.Limit, 1, 100);
        var result = await client.V5Api.Trading.GetUserTradesAsync(
            Category.Linear,
            symbol: symbol,
            settleAsset: "USDT",
            startTime: query.FromUtc?.UtcDateTime,
            endTime: query.ToUtc?.UtcDateTime,
            limit: limit,
            ct: cancellationToken).ConfigureAwait(false);
        return BybitResult.Require(result).List.Select(trade => new HistoricalExecution(
            trade.TradeId,
            trade.OrderId,
            trade.ClientOrderId,
            trade.Symbol,
            trade.Side == OrderSide.Sell ? TradeSide.Sell : TradeSide.Buy,
            trade.Price,
            trade.Quantity,
            trade.Fee,
            trade.FeeAsset,
            trade.IsMaker,
            Utc(trade.Timestamp))).ToArray();
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
