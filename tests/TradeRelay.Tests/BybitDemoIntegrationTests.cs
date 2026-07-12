using TradeRelay.Core.Models;
using TradeRelay.Providers.Bybit;
using Xunit;

namespace TradeRelay.Tests;

public sealed class BybitDemoIntegrationTests
{
    [Fact]
    [Trait("Category", "BybitDemoIntegration")]
    public async Task DemoCredentials_CanReadCredentialAccountAndMarketData_WhenConfigured()
    {
        string? key = Environment.GetEnvironmentVariable("TRADERELAY_BYBIT_DEMO_API_KEY");
        string? secret = Environment.GetEnvironmentVariable("TRADERELAY_BYBIT_DEMO_API_SECRET");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret)) return;

        var factory = new BybitExchangeProviderFactory(TimeProvider.System);
        await using var connection = factory.CreateConnection(TradingEnvironment.Demo, new ExchangeCredentials(key, secret));
        ApiCredentialInfo info = await connection.Account.GetCredentialInfoAsync(default);
        AccountSummary account = await connection.Account.GetAccountSummaryAsync(default);
        var market = factory.CreateMarketDataProvider(TradingEnvironment.Demo);
        TickerSnapshot ticker = await market.GetTickerAsync("BTCUSDT", default);
        (market as IDisposable)?.Dispose();

        Assert.False(info.HasWithdrawalPermission);
        Assert.Equal(TradingEnvironment.Demo, account.Environment);
        Assert.Equal("BTCUSDT", ticker.Symbol);
        Assert.True(ticker.LastPrice > 0m);
    }
}
