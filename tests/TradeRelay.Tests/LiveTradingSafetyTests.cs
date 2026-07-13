using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class LiveTradingSafetyTests
{
    [Fact]
    public async Task LiveTrading_StartsDisabledRequiresExactPhraseAndEmergencyDisableBlocksNewLeases()
    {
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "live-key", "live-secret", false, default)).Success);
        await context.Host.StartServerAsync();

        Assert.False(context.TradingControl.Snapshot.Enabled);
        Assert.Equal("LIVE_CONFIRMATION_REQUIRED", (await context.TradingControl.EnableAsync(true, false, "enable live trading", default)).Code);
        Assert.Equal("LIVE_CONFIRMATION_REQUIRED", (await context.TradingControl.EnableAsync(true, false, " ENABLE LIVE TRADING ", default)).Code);
        Assert.True((await context.TradingControl.EnableAsync(true, false, "ENABLE LIVE TRADING", default)).Allowed);
        Assert.Equal(TradingEnvironment.Live, context.TradingControl.Snapshot.Environment);
        Assert.NotNull(context.TradingControl.Snapshot.SessionId);

        TradingWriteLease? active = context.TradingControl.TryBeginWrite();
        Assert.NotNull(active);
        context.TradingControl.Disable("Emergency disable test.", emergency: true);
        Assert.Null(context.TradingControl.TryBeginWrite());
        Assert.Equal(TradingSessionState.EmergencyDisabled, context.TradingControl.Snapshot.State);
        Assert.False(await context.TradingControl.WaitForActiveWritesAsync(TimeSpan.FromMilliseconds(10), default));
        active.Dispose();
        Assert.True(await context.TradingControl.WaitForActiveWritesAsync(TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public void LiveActionConfirmations_AreHashedSingleUseAndExpire()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 13, 1, 0, 0, TimeSpan.Zero));
        var store = new LiveActionConfirmationStore(time);
        Guid connection = Guid.NewGuid();
        Guid session = Guid.NewGuid();
        var request = new LiveActionRequest(LiveActionType.CancelAllOrders, null, null, "All active USDT-linear orders", 2);

        LiveActionConfirmationResult created = store.Add("live-action-1", request, connection, session);
        LiveActionConfirmation confirmation = Assert.IsType<LiveActionConfirmation>(created.Confirmation);
        Assert.Equal("LIVE_CONFIRMATION_REQUIRED", created.Code);
        Assert.False(store.Approve(confirmation.ConfirmationId, new string('0', 64)).Success);
        Assert.True(store.Approve(confirmation.ConfirmationId, confirmation.ImmutableHash).Success);
        Assert.Equal("CONFIRMATION_MISMATCH", store.Begin(confirmation.ConfirmationId, "live-action-1", request with { Symbol = "BTCUSDT" }, connection, session).Code);
        Assert.True(store.Begin(confirmation.ConfirmationId, "live-action-1", request, connection, session).Success);
        Assert.True(store.Complete(confirmation.ConfirmationId, "OK", "Completed.").Success);
        Assert.Equal("DUPLICATE_REQUEST", store.Begin(confirmation.ConfirmationId, "live-action-1", request, connection, session).Code);

        LiveActionConfirmationResult expiring = store.Add("live-action-2", request, connection, session);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(LiveActionConfirmationState.Expired, store.Get(expiring.Confirmation!.ConfirmationId)?.State);
    }

    [Fact]
    public async Task LiveMarketExecution_RejectsApprovedPlanAfterPriceDeviation()
    {
        var trading = new StubTradingProvider();
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false, trading: trading));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "live-key", "live-secret", false, default)).Success);
        await context.Host.StartServerAsync();
        Assert.True((await context.TradingControl.EnableAsync(true, false, "ENABLE LIVE TRADING", default)).Allowed);

        var normalized = new NormalizedOrder("BTCUSDT", TradeSide.Buy, OrderType.Market, .1m, .1m, null, null, 100m, 99m, 99m, 120m, 120m, 1m, new RiskEstimate(10m, .1m, 2m, 20m, .01m), null);
        PreparedOrderResult added = context.PreparedOrderStore.Add("live-market-drift", new(true, normalized, [], []), TradingEnvironment.Live, RiskSettingsSnapshot.Create(context.Settings.Risk, TradingEnvironment.Live), context.ConnectionManager.Snapshot.ConnectionGenerationId);
        Assert.True(added.Success);
        Assert.True(context.PreparedOrderStore.Approve(added.Order!.PreparationId, added.Order.ImmutableHash).Success);

        var result = await context.OrderExecutionService.ExecuteAsync(added.Order.PreparationId, "correlation", default);

        Assert.False(result.Success);
        Assert.Equal("PRICE_DEVIATION_EXCEEDED", result.Code);
        Assert.Equal(0, trading.PlaceCount);
    }

    [Fact]
    public async Task PreparedLivePlan_CannotExecuteAfterCredentialConnectionChanges()
    {
        var trading = new StubTradingProvider();
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false, trading: trading));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "live-key-a", "live-secret-a", false, default)).Success);
        PreparedOrderResult prepared = await context.OrderPreparationService.PrepareAsync(new("connection-bound", "BTCUSDT", TradeSide.Buy, OrderType.Limit, .1m, 100m, 90m, 120m, 1m, null), default);
        Assert.True(prepared.Success);
        Assert.True(context.PreparedOrderStore.Approve(prepared.Order!.PreparationId, prepared.Order.ImmutableHash).Success);

        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "live-key-b", "live-secret-b", false, default)).Success);
        await context.Host.StartServerAsync();
        Assert.True((await context.TradingControl.EnableAsync(true, false, "ENABLE LIVE TRADING", default)).Allowed);
        var result = await context.OrderExecutionService.ExecuteAsync(prepared.Order.PreparationId, "correlation", default);

        Assert.Equal("CONNECTION_CHANGED", result.Code);
        Assert.Equal(0, trading.PlaceCount);
    }

    [Fact]
    public async Task LiveCancelAllAndClose_RequireDesktopTicketsAndExecuteOnce()
    {
        var trading = new StubTradingProvider();
        PositionSnapshot position = new("BTCUSDT", TradeSide.Buy, 1m, 100m, 101m, 1m, 1m, null, 90m, 120m, "0");
        OrderSnapshot order = new("order-1", "client-1", "BTCUSDT", TradeSide.Buy, "Limit", 100m, 1m, 0m, "New", false, DateTimeOffset.UtcNow);
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false, trading: trading, positions: [position], orders: [order]));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "live-key", "live-secret", false, default)).Success);
        await context.Host.StartServerAsync();
        Assert.True((await context.TradingControl.EnableAsync(true, false, "ENABLE LIVE TRADING", default)).Allowed);

        var requested = await context.OrderExecutionService.CancelAllAsync(true, null, "cancel-all-live", null, "correlation-1", default);
        LiveActionConfirmation cancelConfirmation = Assert.IsType<LiveActionConfirmation>(requested.Data?.Confirmation);
        Assert.Equal("LIVE_CONFIRMATION_REQUIRED", requested.Code);
        Assert.Equal(0, trading.CancelAllCount);
        Assert.True(context.LiveConfirmations.Approve(cancelConfirmation.ConfirmationId, cancelConfirmation.ImmutableHash).Success);

        var cancelled = await context.OrderExecutionService.CancelAllAsync(true, null, "cancel-all-live", cancelConfirmation.ConfirmationId.ToString(), "correlation-2", default);
        Assert.True(cancelled.Success);
        Assert.Equal(1, trading.CancelAllCount);
        var duplicate = await context.OrderExecutionService.CancelAllAsync(true, null, "cancel-all-live", cancelConfirmation.ConfirmationId.ToString(), "correlation-3", default);
        Assert.Equal("DUPLICATE_REQUEST", duplicate.Code);
        Assert.Equal(1, trading.CancelAllCount);

        var closeRequested = await context.OrderExecutionService.CloseAsync("BTCUSDT", null, "close-live", null, "correlation-4", default);
        LiveActionConfirmation closeConfirmation = Assert.IsType<LiveActionConfirmation>(closeRequested.Data?.Confirmation);
        Assert.True(context.LiveConfirmations.Approve(closeConfirmation.ConfirmationId, closeConfirmation.ImmutableHash).Success);
        var closed = await context.OrderExecutionService.CloseAsync("BTCUSDT", null, "close-live", closeConfirmation.ConfirmationId.ToString(), "correlation-5", default);
        Assert.True(closed.Success);
        Assert.Equal(1, trading.CloseCount);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
