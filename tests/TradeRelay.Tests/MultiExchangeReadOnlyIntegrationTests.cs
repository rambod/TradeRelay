using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Providers.Binance;
using TradeRelay.Providers.KuCoin;
using Xunit;

namespace TradeRelay.Tests;

public sealed class MultiExchangeReadOnlyIntegrationTests
{
    [Fact]
    [Trait("Category", "BinanceReadOnlyIntegration")]
    public async Task BinanceLive_CanReadAccountMarketAndPrivateStream_WhenConfigured()
    {
        string? key = Environment.GetEnvironmentVariable("TRADERELAY_BINANCE_LIVE_API_KEY");
        string? secret = Environment.GetEnvironmentVariable("TRADERELAY_BINANCE_LIVE_API_SECRET");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret)) return;
        var factory = new BinanceExchangeProviderFactory(TimeProvider.System);
        await using IExchangeProviderConnection connection = factory.CreateConnection(TradingEnvironment.Live, new ExchangeCredentials(key, secret));
        AccountSummary account = await connection.Account.GetAccountSummaryAsync(default);
        IMarketDataProvider market = factory.CreateMarketDataProvider(TradingEnvironment.Live);
        TickerSnapshot ticker = await market.GetTickerAsync("BTCUSDT", default);
        await connection.Stream.ConnectAsync(default);
        Assert.Equal(TradingEnvironment.Live, account.Environment);
        Assert.True(ticker.LastPrice > 0m);
        Assert.True(connection.Stream.IsConnected);
        await connection.Stream.DisconnectAsync(default);
        if (market is IAsyncDisposable asyncMarket) await asyncMarket.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "KuCoinReadOnlyIntegration")]
    public async Task KuCoinLive_CanReadAccountMarketAndPrivateStream_WhenConfigured()
    {
        string? key = Environment.GetEnvironmentVariable("TRADERELAY_KUCOIN_LIVE_API_KEY");
        string? secret = Environment.GetEnvironmentVariable("TRADERELAY_KUCOIN_LIVE_API_SECRET");
        string? passphrase = Environment.GetEnvironmentVariable("TRADERELAY_KUCOIN_LIVE_API_PASSPHRASE");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(passphrase)) return;
        var fields = new Dictionary<string, string> { [ExchangeCredentials.ApiKeyField] = key, [ExchangeCredentials.ApiSecretField] = secret, [ExchangeCredentials.PassphraseField] = passphrase };
        var factory = new KuCoinExchangeProviderFactory(TimeProvider.System);
        await using IExchangeProviderConnection connection = factory.CreateConnection(TradingEnvironment.Live, new ExchangeCredentialSet(fields));
        AccountSummary account = await connection.Account.GetAccountSummaryAsync(default);
        IMarketDataProvider market = factory.CreateMarketDataProvider(TradingEnvironment.Live);
        TickerSnapshot ticker = await market.GetTickerAsync("BTCUSDT", default);
        await connection.Stream.ConnectAsync(default);
        Assert.Equal(TradingEnvironment.Live, account.Environment);
        Assert.True(ticker.LastPrice > 0m);
        Assert.True(connection.Stream.IsConnected);
        await connection.Stream.DisconnectAsync(default);
        if (market is IAsyncDisposable asyncMarket) await asyncMarket.DisposeAsync();
    }
}
