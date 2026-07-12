using Bybit.Net;
using Bybit.Net.Clients;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.Bybit;

/// <summary>Creates Bybit read-only provider services.</summary>
public sealed class BybitExchangeProviderFactory(TimeProvider timeProvider) : IExchangeProviderFactory
{
    /// <inheritdoc />
    public string ProviderName => "Bybit";

    /// <inheritdoc />
    public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment)
    {
        // Bybit Demo uses mainnet public market data.
        var client = new BybitRestClient(options => options.Environment = BybitEnvironment.Live);
        return new BybitMarketDataProvider(client, timeProvider);
    }

    /// <inheritdoc />
    public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        BybitEnvironment bybitEnvironment = environment == TradingEnvironment.Demo
            ? BybitEnvironment.DemoTrading
            : BybitEnvironment.Live;
        var rest = new BybitRestClient(options =>
        {
            options.Environment = bybitEnvironment;
            options.ApiCredentials = new BybitCredentials(credentials.ApiKey, credentials.ApiSecret);
        });
        var socket = new BybitSocketClient(options =>
        {
            options.Environment = bybitEnvironment;
            options.ApiCredentials = new BybitCredentials(credentials.ApiKey, credentials.ApiSecret);
        });
        return new BybitExchangeConnection(rest, socket, environment, timeProvider);
    }
}
