using TradeRelay.Core.Models;
using TradeRelay.Providers.Bybit;
using TradeRelay.Core.Risk;
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

    [Fact]
    [Trait("Category", "BybitDemoTradingIntegration")]
    public async Task DemoOrder_CanPrepareExecuteReconcileAndCancel_WhenExplicitlyEnabled()
    {
        string? enabled = Environment.GetEnvironmentVariable("TRADERELAY_RUN_DEMO_TRADING_TESTS");
        string? key = Environment.GetEnvironmentVariable("TRADERELAY_BYBIT_DEMO_API_KEY");
        string? secret = Environment.GetEnvironmentVariable("TRADERELAY_BYBIT_DEMO_API_SECRET");
        if (enabled != "1" || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret)) return;

        var factory = new BybitExchangeProviderFactory(TimeProvider.System);
        await using TestServerContext context = TestServerContext.Create(providerFactory: factory);
        ExchangeConnectionResult saved = await context.ConnectionManager.SaveAsync(TradingEnvironment.Demo, key, secret, false, default);
        Assert.True(saved.Success);
        Assert.False(saved.CredentialInfo?.IsReadOnly);
        await context.Host.StartServerAsync();
        Assert.True((await context.TradingControl.EnableAsync(true, true, default)).Allowed);

        const string symbol = "BTCUSDT";
        InstrumentInfo instrument = await context.ConnectionManager.MarketData.GetInstrumentInfoAsync(symbol, default);
        TickerSnapshot ticker = await context.ConnectionManager.MarketData.GetTickerAsync(symbol, default);
        decimal limit = DecimalNormalizer.RoundToTick((ticker.BidPrice ?? ticker.LastPrice) * .90m, instrument.TickSize);
        decimal stop = DecimalNormalizer.RoundDownToStep(limit * .98m, instrument.TickSize);
        decimal take = DecimalNormalizer.RoundToTick(limit * 1.02m, instrument.TickSize);
        decimal quantity = instrument.MinimumQuantity;

        PreparedOrderResult prepared = await context.OrderPreparationService.PrepareAsync(new PrepareOrderRequest($"demo-write-{Guid.NewGuid():N}", symbol, TradeSide.Buy, OrderType.Limit, quantity, limit, stop, take, 1m, "Opt-in automated Demo acceptance"), default);
        Assert.True(prepared.Success);
        var execution = await context.OrderExecutionService.ExecuteAsync(prepared.Order!.PreparationId, Guid.NewGuid().ToString("N"), default);
        Assert.True(execution.Success);
        Assert.NotNull(execution.Data?.ExchangeOrderId);
        var cancellation = await context.OrderExecutionService.CancelAsync(symbol, execution.Data!.ExchangeOrderId!, Guid.NewGuid().ToString("N"), default);
        Assert.True(cancellation.Success);
    }
}
