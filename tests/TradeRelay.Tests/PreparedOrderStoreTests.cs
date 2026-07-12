using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;
using Xunit;

namespace TradeRelay.Tests;

public sealed class PreparedOrderStoreTests
{
    [Fact]
    public void DemoPolicy_AutoApprovesAndCreatesBoundedIdentifiers()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = new PreparedOrderStore(time);
        PreparedOrderResult result = store.Add("request-1", Validation(), TradingEnvironment.Demo, Snapshot(manual: false));

        Assert.True(result.Success);
        Assert.Equal(PreparedOrderState.Approved, result.Order?.State);
        Assert.Equal(result.Order?.ImmutableHash, result.Order?.ApprovedHash);
        Assert.StartsWith("tr-", result.Order?.ClientOrderId);
        Assert.True(result.Order?.ClientOrderId.Length <= 36);
        Assert.Equal(64, result.Order?.ImmutableHash.Length);
    }

    [Fact]
    public void DuplicateClientRequestId_IsRejected()
    {
        var store = new PreparedOrderStore(TimeProvider.System);
        Assert.True(store.Add("same-request", Validation(), TradingEnvironment.Demo, Snapshot(true)).Success);
        PreparedOrderResult duplicate = store.Add("same-request", Validation(), TradingEnvironment.Demo, Snapshot(true));
        Assert.False(duplicate.Success);
        Assert.Equal("DUPLICATE_REQUEST", duplicate.Code);
    }

    [Fact]
    public void ApprovalRequiresMatchingHashAndCannotMutateApprovedPlan()
    {
        var store = new PreparedOrderStore(TimeProvider.System);
        PreparedOrder order = Assert.IsType<PreparedOrder>(store.Add("request-approve", Validation(), TradingEnvironment.Live, Snapshot(true)).Order);
        Assert.False(store.Approve(order.PreparationId, new string('0', 64)).Success);
        PreparedOrderResult approved = store.Approve(order.PreparationId, order.ImmutableHash);
        Assert.True(approved.Success);
        Assert.Equal(order.Order, approved.Order?.Order);
        Assert.Equal(order.ImmutableHash, approved.Order?.ApprovedHash);
        Assert.False(store.Reject(order.PreparationId, order.ImmutableHash).Success);
    }

    [Fact]
    public void RejectedAndExpiredOrdersCannotBeApproved()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var rejectedStore = new PreparedOrderStore(time);
        PreparedOrder rejected = Assert.IsType<PreparedOrder>(rejectedStore.Add("request-reject", Validation(), TradingEnvironment.Live, Snapshot(true)).Order);
        Assert.True(rejectedStore.Reject(rejected.PreparationId, rejected.ImmutableHash).Success);
        Assert.False(rejectedStore.Approve(rejected.PreparationId, rejected.ImmutableHash).Success);

        var expiredStore = new PreparedOrderStore(time);
        PreparedOrder expiring = Assert.IsType<PreparedOrder>(expiredStore.Add("request-expire", Validation(), TradingEnvironment.Live, Snapshot(true, expiry: 30)).Order);
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(PreparedOrderState.Expired, expiredStore.Get(expiring.PreparationId)?.State);
        Assert.Equal("ORDER_EXPIRED", expiredStore.Approve(expiring.PreparationId, expiring.ImmutableHash).Code);
    }

    [Fact]
    public async Task ConcurrentApproval_AllowsOnlyOneTransition()
    {
        var store = new PreparedOrderStore(TimeProvider.System);
        PreparedOrder order = Assert.IsType<PreparedOrder>(store.Add("request-race", Validation(), TradingEnvironment.Live, Snapshot(true)).Order);
        PreparedOrderResult[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.Approve(order.PreparationId, order.ImmutableHash))));
        Assert.Single(results, result => result.Success);
    }

    [Fact]
    public void SettingsSnapshotAndHashRemainUnchangedAfterSourceMutation()
    {
        RiskSettingsSnapshot snapshot = Snapshot(true);
        var store = new PreparedOrderStore(TimeProvider.System);
        PreparedOrder order = Assert.IsType<PreparedOrder>(store.Add("request-snapshot", Validation(), TradingEnvironment.Live, snapshot).Order);
        string hash = order.ImmutableHash;
        Assert.Equal(snapshot, store.Get(order.PreparationId)?.RiskSettings);
        Assert.Equal(hash, store.Get(order.PreparationId)?.ImmutableHash);
    }

    [Fact]
    public async Task ExecutionClaim_AllowsOneCallerAndStoresOutcomeWithoutChangingHash()
    {
        var store = new PreparedOrderStore(TimeProvider.System);
        PreparedOrder order = Assert.IsType<PreparedOrder>(store.Add("execute-race", Validation(), TradingEnvironment.Demo, Snapshot(false)).Order);

        PreparedOrderResult[] claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.BeginExecution(order.PreparationId, order.ImmutableHash))));

        PreparedOrderResult claim = Assert.Single(claims, result => result.Success);
        Assert.Equal(PreparedOrderState.Executing, claim.Order?.State);
        var submission = new OrderSubmissionResult(true, true, "exchange-1", order.ClientOrderId, ExchangeOrderStatus.PartiallyFilled, 1m, .4m, .6m, 101m, "Reconciled.", DateTimeOffset.UtcNow);
        PreparedOrderResult completed = store.CompleteExecution(order.PreparationId, submission);
        Assert.True(completed.Success);
        Assert.Equal(PreparedOrderState.Executed, completed.Order?.State);
        Assert.Equal(order.ImmutableHash, completed.Order?.ImmutableHash);
        Assert.Equal(.4m, completed.Order?.Submission?.FilledQuantity);
        Assert.False(store.BeginExecution(order.PreparationId, order.ImmutableHash).Success);
    }

    [Fact]
    public void ExecutionRequiresCurrentApprovalAndRejectsExpiredPlan()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var store = new PreparedOrderStore(time);
        PreparedOrder pending = Assert.IsType<PreparedOrder>(store.Add("pending-execution", Validation(), TradingEnvironment.Demo, Snapshot(true)).Order);
        Assert.Equal("APPROVAL_REQUIRED", store.BeginExecution(pending.PreparationId, pending.ImmutableHash).Code);
        Assert.True(store.Approve(pending.PreparationId, pending.ImmutableHash).Success);
        time.Advance(TimeSpan.FromSeconds(121));
        Assert.Equal("ORDER_EXPIRED", store.BeginExecution(pending.PreparationId, pending.ImmutableHash).Code);
    }

    private static OrderValidationResult Validation()
    {
        var risk = new RiskEstimate(100m, 10m, 20m, 2m, 1m);
        var order = new NormalizedOrder("BTCUSDT", TradeSide.Buy, OrderType.Limit, 1m, 1m, 100m, 100m, 100m, 90m, 90m, 120m, 120m, 1m, risk, "note");
        return new(true, order, [], ["Simulation only"]);
    }

    private static RiskSettingsSnapshot Snapshot(bool manual, int expiry = 120) => new(["BTCUSDT"], 1m, 1000m, 2, 3m, true, manual, expiry);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
