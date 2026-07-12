using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using CoreOrderType = TradeRelay.Core.Models.OrderType;

namespace TradeRelay.Providers.Bybit;

internal sealed class BybitTradingProvider(IBybitRestClient client, TimeProvider timeProvider) : IExchangeTradingProvider
{
    public async Task<OrderSubmissionResult> PlaceOrderAsync(ExchangeOrderRequest order, CancellationToken cancellationToken)
    {
        var result = await client.V5Api.Trading.PlaceOrderAsync(
            Category.Linear, order.Symbol, Side(order.Side), order.OrderType == CoreOrderType.Market ? NewOrderType.Market : NewOrderType.Limit,
            order.Quantity, price: order.LimitPrice, positionIdx: Position(order.PositionMode), clientOrderId: order.ClientOrderId,
            takeProfit: order.TakeProfit, stopLoss: order.StopLoss, reduceOnly: order.ReduceOnly,
            stopLossTakeProfitMode: order.ReduceOnly ? null : StopLossTakeProfitMode.Full, ct: cancellationToken).ConfigureAwait(false);
        BybitOrderId acknowledgement = BybitResult.Require(result);
        return new(true, false, acknowledgement.OrderId, order.ClientOrderId, ExchangeOrderStatus.Pending, order.Quantity, 0m, order.Quantity, null, "Bybit acknowledged the order; reconciliation is pending.", timeProvider.GetUtcNow());
    }

    public async Task<OrderSubmissionResult?> GetOrderAsync(string symbol, string? exchangeOrderId, string? clientOrderId, CancellationToken cancellationToken)
    {
        var open = await client.V5Api.Trading.GetOrdersAsync(Category.Linear, symbol: symbol, orderId: exchangeOrderId, clientOrderId: clientOrderId, openOnly: 0, limit: 50, ct: cancellationToken).ConfigureAwait(false);
        BybitOrder? order = BybitResult.Require(open).List.FirstOrDefault();
        if (order is null)
        {
            var history = await client.V5Api.Trading.GetOrderHistoryAsync(Category.Linear, symbol: symbol, orderId: exchangeOrderId, clientOrderId: clientOrderId, limit: 50, ct: cancellationToken).ConfigureAwait(false);
            order = BybitResult.Require(history).List.FirstOrDefault();
        }
        return order is null ? null : Map(order);
    }

    public async Task<OperationResult> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken)
    {
        var result = await client.V5Api.Trading.CancelOrderAsync(Category.Linear, symbol, exchangeOrderId, clientOrderId: null!, ct: cancellationToken).ConfigureAwait(false);
        BybitResult.Require(result);
        return new(true, "OK", "Bybit acknowledged the cancellation.", 1, ExchangeOrderStatus.Pending, timeProvider.GetUtcNow());
    }

    public async Task<OperationResult> CancelAllOrdersAsync(string? symbol, CancellationToken cancellationToken)
    {
        var result = await client.V5Api.Trading.CancelAllOrderAsync(Category.Linear, symbol: symbol!, settleAsset: "USDT", ct: cancellationToken).ConfigureAwait(false);
        BybitResult.Require(result);
        return new(true, "OK", "Bybit acknowledged cancel-all.", null, ExchangeOrderStatus.Pending, timeProvider.GetUtcNow());
    }

    public Task<OrderSubmissionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken) =>
        PlaceOrderAsync(new ExchangeOrderRequest($"tr-close-{Guid.NewGuid():N}"[..32], request.Symbol, request.PositionSide == TradeSide.Buy ? TradeSide.Sell : TradeSide.Buy, CoreOrderType.Market, request.Quantity, null, null, null, true, request.PositionMode), cancellationToken);

    public async Task<OperationResult> SetTradingStopAsync(TradingStopRequest request, CancellationToken cancellationToken)
    {
        var result = await client.V5Api.Trading.SetTradingStopAsync(Category.Linear, request.Symbol, Position(request.PositionMode), takeProfit: request.TakeProfit, stopLoss: request.StopLoss, stopLossTakeProfitMode: StopLossTakeProfitMode.Full, takeProfitOrderType: global::Bybit.Net.Enums.OrderType.Market, stopLossOrderType: global::Bybit.Net.Enums.OrderType.Market, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) throw new ProviderException(result.Error?.Code?.ToString() == "10006" ? "RATE_LIMITED" : "PROVIDER_UNAVAILABLE", result.Error?.Code?.ToString() == "10006" ? "Bybit rate-limited the request. Try again later." : "Bybit could not update the trading stop.");
        return new(true, "OK", "Bybit acknowledged the trading-stop update.", 1, null, timeProvider.GetUtcNow());
    }

    private OrderSubmissionResult Map(BybitOrder order) => new(true, true, order.OrderId, order.ClientOrderId ?? string.Empty, Status(order.Status), order.Quantity, order.QuantityFilled ?? 0m, order.QuantityRemaining ?? Math.Max(0m, order.Quantity - (order.QuantityFilled ?? 0m)), order.AveragePrice, "Order state reconciled from Bybit.", new DateTimeOffset(DateTime.SpecifyKind(order.UpdateTime, DateTimeKind.Utc)));
    private static OrderSide Side(TradeSide side) => side == TradeSide.Buy ? OrderSide.Buy : OrderSide.Sell;
    private static PositionIdx Position(string value)
    {
        if (Enum.TryParse(value, true, out PositionIdx named) && Enum.IsDefined(named)) return named;
        return int.TryParse(value, out int parsed) && Enum.IsDefined(typeof(PositionIdx), parsed) ? (PositionIdx)parsed : PositionIdx.OneWayMode;
    }
    private static ExchangeOrderStatus Status(global::Bybit.Net.Enums.OrderStatus status) => status switch
    {
        global::Bybit.Net.Enums.OrderStatus.New or global::Bybit.Net.Enums.OrderStatus.Untriggered => ExchangeOrderStatus.New,
        global::Bybit.Net.Enums.OrderStatus.PartiallyFilled => ExchangeOrderStatus.PartiallyFilled,
        global::Bybit.Net.Enums.OrderStatus.Filled => ExchangeOrderStatus.Filled,
        global::Bybit.Net.Enums.OrderStatus.Cancelled or global::Bybit.Net.Enums.OrderStatus.Deactivated => ExchangeOrderStatus.Cancelled,
        global::Bybit.Net.Enums.OrderStatus.Rejected => ExchangeOrderStatus.Rejected,
        _ => ExchangeOrderStatus.Unknown
    };
}
