using Bybit.Net.Interfaces.Clients;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.Bybit;

internal sealed class BybitExchangeConnection : IExchangeProviderConnection
{
    private readonly IBybitRestClient _restClient;
    private readonly IBybitSocketClient _socketClient;

    public BybitExchangeConnection(IBybitRestClient restClient, IBybitSocketClient socketClient, Core.Models.TradingEnvironment environment, TimeProvider timeProvider)
    {
        _restClient = restClient;
        _socketClient = socketClient;
        Account = new BybitTradingAccountProvider(restClient, environment, timeProvider);
        Stream = new BybitExchangeStream(socketClient, timeProvider);
    }

    public ITradingAccountProvider Account { get; }
    public IExchangeStream Stream { get; }

    public async ValueTask DisposeAsync()
    {
        try { await Stream.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        _socketClient.Dispose();
        _restClient.Dispose();
    }
}
