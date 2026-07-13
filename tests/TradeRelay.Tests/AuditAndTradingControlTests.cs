using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class AuditAndTradingControlTests
{
    [Fact]
    public async Task AuditLog_WritesJsonlLoadsNewestAndContainsNoSentinelSecret()
    {
        string root = Path.Combine(Path.GetTempPath(), "TradeRelay.AuditTests", Guid.NewGuid().ToString("N"));
        try
        {
            var audit = new AuditLogService(new ApplicationDataPaths(root), TimeProvider.System, new SensitiveDataRedactor());
            AuditEvent item = audit.Create("execute_prepared_order", "execution_requested", "STARTED", TradingEnvironment.Demo, "correlation", "BTCUSDT", providerResult: "authorization=sentinel-secret");
            Assert.True(await audit.TryWriteAsync(item, default));
            var loaded = await audit.LoadRecentAsync(default);
            Assert.Contains(loaded.Events, value => value.EventId == item.EventId);
            string text = await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(root, "audit")).Single());
            Assert.DoesNotContain("sentinel-secret", text, StringComparison.Ordinal);
            Assert.Contains("execution_requested", text, StringComparison.Ordinal);
            await File.AppendAllTextAsync(Directory.GetFiles(Path.Combine(root, "audit")).Single(), "{malformed}\n");
            Assert.NotNull((await audit.LoadRecentAsync(default)).Warning);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AuditFailure_BecomesUnhealthyAndBlocksRequiredWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), "TradeRelay.AuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "audit"), "not-a-directory");
        try
        {
            var audit = new AuditLogService(new ApplicationDataPaths(root), TimeProvider.System, new SensitiveDataRedactor());
            Assert.False(await audit.TryWriteAsync(audit.Create("test", "write", "STARTED", TradingEnvironment.Demo, "id"), default));
            Assert.False(audit.Health.Healthy);
            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(() => audit.WriteRequiredAsync(audit.Create("test", "write", "STARTED", TradingEnvironment.Demo, "id"), default));
            Assert.Equal("AUDIT_UNAVAILABLE", exception.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DemoTrading_IsExplicitSessionOnlyAndReadOnlyKeysAreRejected()
    {
        await using TestServerContext readOnly = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory());
        Assert.True((await readOnly.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "key", "secret", false, default)).Success);
        TradingGateResult denied = await readOnly.TradingControl.EnableAsync(true, true, default);
        Assert.False(denied.Allowed);
        Assert.Equal("READ_ONLY", denied.Code);

        await using TestServerContext writable = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false));
        Assert.True((await writable.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "key", "secret", false, default)).Success);
        Assert.False(writable.TradingControl.Snapshot.Enabled);
        Assert.False((await writable.TradingControl.EnableAsync(false, true, default)).Allowed);
        Assert.True((await writable.TradingControl.EnableAsync(true, true, default)).Allowed);
        writable.TradingControl.Disable();
        Assert.False(writable.TradingControl.Snapshot.Enabled);
    }
}
