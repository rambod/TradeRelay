using TradeRelay.Core.Models;
using Xunit;

namespace TradeRelay.Tests;

public sealed class OrderExecutionServiceTests
{
    [Fact]
    public async Task AmbiguousSubmission_QueriesOnceNeverRetriesAndFailsUnknown()
    {
        var trading = new StubTradingProvider { ThrowOnPlace = true, QueryResult = null };
        await using TestServerContext context = await ReadyContextAsync(trading);
        PreparedOrder plan = await PrepareAsync(context, "ambiguous-execution");

        var result = await context.OrderExecutionService.ExecuteAsync(plan.PreparationId, "correlation", default);

        Assert.False(result.Success);
        Assert.Equal("ORDER_STATE_UNKNOWN", result.Code);
        Assert.Equal(1, trading.PlaceCount);
        Assert.Equal(1, trading.QueryCount);
        Assert.Equal(PreparedOrderState.Failed, context.PreparedOrderStore.Get(plan.PreparationId)?.State);
    }

    [Fact]
    public async Task PartialFill_IsPreservedAsReconciledExecution()
    {
        var trading = new StubTradingProvider
        {
            PlaceResult = new(true, true, "partial-1", "client", ExchangeOrderStatus.PartiallyFilled, .1m, .04m, .06m, 100.5m, "Partially filled.", DateTimeOffset.UtcNow)
        };
        await using TestServerContext context = await ReadyContextAsync(trading);
        PreparedOrder plan = await PrepareAsync(context, "partial-execution");

        var result = await context.OrderExecutionService.ExecuteAsync(plan.PreparationId, "correlation", default);

        Assert.True(result.Success);
        Assert.Equal(.04m, result.Data?.FilledQuantity);
        Assert.Equal(.06m, result.Data?.RemainingQuantity);
        Assert.Equal(ExchangeOrderStatus.PartiallyFilled, context.PreparedOrderStore.Get(plan.PreparationId)?.Submission?.Status);
    }

    private static async Task<TestServerContext> ReadyContextAsync(StubTradingProvider trading)
    {
        TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false, trading: trading));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "key", "secret", false, default)).Success);
        await context.Host.StartServerAsync();
        Assert.True((await context.TradingControl.EnableAsync(true, true, default)).Allowed);
        return context;
    }

    private static async Task<PreparedOrder> PrepareAsync(TestServerContext context, string requestId)
    {
        PreparedOrderResult prepared = await context.OrderPreparationService.PrepareAsync(new(requestId, "BTCUSDT", TradeSide.Buy, OrderType.Limit, .1m, 100m, 90m, 120m, 1m, null), default);
        Assert.True(prepared.Success);
        return Assert.IsType<PreparedOrder>(prepared.Order);
    }
}
