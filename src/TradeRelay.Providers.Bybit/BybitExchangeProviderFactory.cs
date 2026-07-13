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
    public ExchangeProviderDescriptor Descriptor { get; } = new(
        new ExchangeId("bybit"),
        "Bybit",
        ProviderCapabilities.MarketData | ProviderCapabilities.AccountRead | ProviderCapabilities.PrivateStream | ProviderCapabilities.TradingWrite,
        [TradingEnvironment.Demo, TradingEnvironment.Live],
        [
            new CredentialFieldDescriptor(ExchangeCredentials.ApiKeyField, "API key", false),
            new CredentialFieldDescriptor(ExchangeCredentials.ApiSecretField, "API secret", true),
        ]);

    /// <inheritdoc />
    public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment)
    {
        // Bybit Demo uses mainnet public market data.
        var client = new BybitRestClient(options => options.Environment = BybitEnvironment.Live);
        return new BybitMarketDataProvider(client, timeProvider);
    }

    /// <inheritdoc />
    public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        BybitEnvironment bybitEnvironment = environment == TradingEnvironment.Demo
            ? BybitEnvironment.DemoTrading
            : BybitEnvironment.Live;
        var rest = new BybitRestClient(options =>
        {
            options.Environment = bybitEnvironment;
            options.ApiCredentials = new BybitCredentials(credentials[ExchangeCredentials.ApiKeyField], credentials[ExchangeCredentials.ApiSecretField]);
        });
        var socket = new BybitSocketClient(options =>
        {
            options.Environment = bybitEnvironment;
            options.ApiCredentials = new BybitCredentials(credentials[ExchangeCredentials.ApiKeyField], credentials[ExchangeCredentials.ApiSecretField]);
        });
        return new BybitExchangeConnection(rest, socket, environment, timeProvider);
    }
}
