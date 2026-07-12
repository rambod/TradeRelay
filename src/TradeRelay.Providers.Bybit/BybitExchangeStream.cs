using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects.Sockets;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.Bybit;

internal sealed class BybitExchangeStream(IBybitSocketClient client, TimeProvider timeProvider) : IExchangeStream
{
    private readonly List<UpdateSubscription> _subscriptions = [];
    public bool IsConnected { get; private set; }
    public event EventHandler<OrderUpdate>? OrderUpdated;
    public event EventHandler<ExecutionUpdate>? ExecutionUpdated;
    public event EventHandler<PositionUpdate>? PositionUpdated;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected) return;
        var orders = await client.V5PrivateApi.SubscribeToOrderUpdatesAsync(update =>
        {
            foreach (var order in update.Data.Where(x => x.Category == Category.Linear))
                OrderUpdated?.Invoke(this, new OrderUpdate(order.Symbol, order.OrderId, order.Status.ToString(), timeProvider.GetUtcNow()));
        }, cancellationToken).ConfigureAwait(false);
        var executions = await client.V5PrivateApi.SubscribeToUserTradeUpdatesAsync(update =>
        {
            foreach (var trade in update.Data.Where(x => x.Category == Category.Linear))
                ExecutionUpdated?.Invoke(this, new ExecutionUpdate(trade.Symbol, trade.OrderId, trade.Price, trade.Quantity, new DateTimeOffset(DateTime.SpecifyKind(trade.Timestamp, DateTimeKind.Utc))));
        }, cancellationToken).ConfigureAwait(false);
        var positions = await client.V5PrivateApi.SubscribeToPositionUpdatesAsync(update =>
        {
            foreach (var position in update.Data.Where(x => x.Category == Category.Linear))
                PositionUpdated?.Invoke(this, new PositionUpdate(position.Symbol, position.Side == PositionSide.Sell ? TradeSide.Sell : TradeSide.Buy, position.Quantity, timeProvider.GetUtcNow()));
        }, cancellationToken).ConfigureAwait(false);

        Add(orders); Add(executions); Add(positions);
        IsConnected = true;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        foreach (UpdateSubscription subscription in _subscriptions.ToArray())
            await subscription.CloseAsync().ConfigureAwait(false);
        _subscriptions.Clear();
        IsConnected = false;
    }

    private void Add(CryptoExchange.Net.Objects.WebSocketResult<UpdateSubscription> result)
    {
        if (!result.Success || result.Data is null) throw new ProviderException("PROVIDER_UNAVAILABLE", "Bybit private WebSocket authentication failed.");
        _subscriptions.Add(result.Data);
    }
}
