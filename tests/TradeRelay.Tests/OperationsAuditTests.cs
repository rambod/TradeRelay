using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Desktop.Services;
using TradeRelay.Desktop.ViewModels;
using Xunit;

namespace TradeRelay.Tests;

public sealed class OperationsAuditTests
{
    [Fact]
    public async Task AuditQuery_ReadsLegacyAndLifecycleLinesPagesAndPurgesWithExactPhrase()
    {
        string root = Path.Combine(Path.GetTempPath(), "TradeRelay.OperationsTests", Guid.NewGuid().ToString("N"));
        try
        {
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.Zero));
            var audit = new AuditLogService(new ApplicationDataPaths(root), time, new SensitiveDataRedactor());
            Assert.True(await audit.TryWriteAsync(audit.Create("legacy", "legacy_event", "OK", TradingEnvironment.Live, "legacy-correlation"), default));
            Assert.True(await audit.TryWriteLifecycleAsync(new TradingLifecycleEvent(2, Guid.NewGuid(), "lifecycle-correlation", time.GetUtcNow(), new ExchangeId("bybit"), TradingEnvironment.Live, TradingLifecycleKind.Position, "position_changed", "CHANGED", "BTCUSDT", TradeSide.Buy, Quantity: 1m, State: "Open"), default));

            AuditHistoryPage first = await audit.QueryAsync(null, null, 1, 1, default);
            AuditHistoryPage second = await audit.QueryAsync(null, null, 2, 1, default);
            Assert.Single(first.Events);
            Assert.True(first.HasMore);
            Assert.Single(second.Events);
            Assert.Contains(new[] { first.Events[0].Action, second.Events[0].Action }, action => action == "legacy_event");
            Assert.Contains(new[] { first.Events[0].Action, second.Events[0].Action }, action => action == "position_changed");

            Assert.False(await audit.PurgeAsync(null, null, "delete audit history", default));
            Assert.True(await audit.PurgeAsync(null, null, "DELETE AUDIT HISTORY", default));
            AuditHistoryPage after = await audit.QueryAsync(null, null, 1, 100, default);
            AuditEvent purge = Assert.Single(after.Events);
            Assert.Equal("audit_history_purged", purge.Action);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OperationsViewModel_SeparatesCurrentPositionsExchangeHistoryAndObservedEvents()
    {
        var position = new PositionSnapshot("BTCUSDT", TradeSide.Buy, 2m, 90m, 100m, 2m, 20m, 50m, 80m, 120m, "0");
        var order = new OrderSnapshot("open-1", "client-1", "BTCUSDT", TradeSide.Sell, "Limit", 110m, 1m, 0m, "New", false, DateTimeOffset.UtcNow);
        var history = new FakeHistory();
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(positions: [position], orders: [order], history: history));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "key", "secret", false, default)).Success);
        using var viewModel = new OperationsViewModel(context.ConnectionManager, context.AuditLog, new ImmediateUiDispatcher(), TimeProvider.System);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        PositionOperationRow row = Assert.Single(viewModel.Positions);
        Assert.Equal("Long", row.Direction);
        Assert.Equal(200m, row.Exposure);
        Assert.Equal("SL 80 · TP 120", row.Protection);
        Assert.Single(viewModel.OrderHistory);
        Assert.Single(viewModel.Fills);
    }

    [Fact]
    public async Task ErrorCenter_GroupsSafeErrorsWithoutRawExceptionMessages()
    {
        await using TestServerContext context = TestServerContext.Create();
        using var viewModel = new ActivityViewModel(context.AuditLog, new ImmediateUiDispatcher(), new RecordingShellService(), context.SafeLog);
        var properties = new Dictionary<string, string> { ["provider"] = "Bybit", ["action"] = "reconcile", ["correlationId"] = "corr-1" };
        await context.SafeLog.TryWriteAsync(SafeLogLevel.Error, "RECONCILIATION_UNAVAILABLE", "provider.reconcile", "Safe fixed message", properties, new InvalidOperationException("sentinel-raw-message"));
        await context.SafeLog.TryWriteAsync(SafeLogLevel.Error, "RECONCILIATION_UNAVAILABLE", "provider.reconcile", "Safe fixed message", properties, new InvalidOperationException("another-sentinel"));

        RuntimeErrorSummary group = Assert.Single(viewModel.RuntimeErrors);
        Assert.Equal(2, group.OccurrenceCount);
        Assert.Equal("Bybit", group.Provider);
        Assert.DoesNotContain("sentinel", group.RecoveryGuidance, StringComparison.OrdinalIgnoreCase);
        string log = await File.ReadAllTextAsync(Directory.GetFiles(context.Paths.LogsDirectory).Single());
        Assert.DoesNotContain("sentinel-raw-message", log, StringComparison.Ordinal);
        Assert.DoesNotContain("another-sentinel", log, StringComparison.Ordinal);
    }

    private sealed class FakeHistory : IExchangeHistoryProvider
    {
        public Task<IReadOnlyList<HistoricalOrder>> GetOrderHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoricalOrder>>([new("history-1", "client-history", "BTCUSDT", TradeSide.Buy, "Limit", 95m, 1m, 1m, 0m, "Filled", false, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1))]);
        public Task<IReadOnlyList<HistoricalExecution>> GetExecutionHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoricalExecution>>([new("fill-1", "history-1", "client-history", "BTCUSDT", TradeSide.Buy, 95m, 1m, .01m, "USDT", false, DateTimeOffset.UtcNow.AddMinutes(-1))]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class ImmediateUiDispatcher : IUiDispatcher { public void Post(Action action) => action(); }
    private sealed class RecordingShellService : IDesktopShellService { public bool TryOpenFolder(string path, out string? error) { error = null; return true; } }
}
