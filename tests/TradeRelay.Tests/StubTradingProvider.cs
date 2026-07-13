using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Tests;

internal sealed class StubTradingProvider : IExchangeTradingProvider
{
    public int PlaceCount { get; private set; }
    public int QueryCount { get; private set; }
    public int CancelAllCount { get; private set; }
    public int CloseCount { get; private set; }
    public bool ThrowOnPlace { get; init; }
    public OrderSubmissionResult? PlaceResult { get; init; }
    public OrderSubmissionResult? QueryResult { get; init; } = new(true, true, "exchange-1", "client-1", ExchangeOrderStatus.New, 1m, 0m, 1m, null, "Confirmed.", DateTimeOffset.UtcNow);
    public Task<OrderSubmissionResult> PlaceOrderAsync(ExchangeOrderRequest order, CancellationToken cancellationToken)
    {
        PlaceCount++;
        if (ThrowOnPlace) throw new ProviderException("PROVIDER_UNAVAILABLE", "Ambiguous provider failure.");
        return Task.FromResult(PlaceResult ?? new OrderSubmissionResult(true, true, "exchange-1", order.ClientOrderId, ExchangeOrderStatus.New, order.Quantity, 0m, order.Quantity, null, "Confirmed.", DateTimeOffset.UtcNow));
    }
    public Task<OrderSubmissionResult?> GetOrderAsync(string symbol, string? exchangeOrderId, string? clientOrderId, CancellationToken cancellationToken) { QueryCount++; return Task.FromResult(QueryResult); }
    public Task<OperationResult> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken) => Task.FromResult(new OperationResult(true, "OK", "Cancelled.", 1, ExchangeOrderStatus.Cancelled, DateTimeOffset.UtcNow));
    public Task<OperationResult> CancelAllOrdersAsync(string? symbol, CancellationToken cancellationToken) { CancelAllCount++; return Task.FromResult(new OperationResult(true, "OK", "Cancelled all.", 0, ExchangeOrderStatus.Cancelled, DateTimeOffset.UtcNow)); }
    public Task<OrderSubmissionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken) { CloseCount++; return Task.FromResult(new OrderSubmissionResult(true, true, "close-1", "close-client", ExchangeOrderStatus.Filled, request.Quantity, request.Quantity, 0m, 100m, "Closed.", DateTimeOffset.UtcNow)); }
    public Task<OperationResult> SetTradingStopAsync(TradingStopRequest request, CancellationToken cancellationToken) => Task.FromResult(new OperationResult(true, "OK", "Stops set.", 1, null, DateTimeOffset.UtcNow));
}
