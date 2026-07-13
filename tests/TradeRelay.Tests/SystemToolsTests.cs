using System.Text.Json;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Mcp;
using Xunit;

namespace TradeRelay.Tests;

public sealed class SystemToolsTests
{
    [Fact]
    public void GetSystemStatus_ReturnsSafeReadOnlyDefaults()
    {
        // Arrange
        DateTimeOffset now = new(2026, 7, 12, 12, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        TestServerContext context = TestServerContext.Create(timeProvider: timeProvider);
        var tools = new SystemTools(
            context.Host,
            context.Settings,
            context.Sessions,
            context.PreparedOrderStore,
            context.LiveConfirmations,
            context.TradingControl,
            context.Metadata,
            timeProvider);

        // Act
        ToolResult<SystemStatusSnapshot> result = tools.GetSystemStatus();
        string json = JsonSerializer.Serialize(result);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("OK", result.Code);
        Assert.NotNull(result.Data);
        Assert.Equal(context.Metadata.Version, result.Data.AppVersion);
        Assert.Equal(McpServerState.Stopped, result.Data.ServerState);
        Assert.Equal(TradingEnvironment.Demo, result.Data.Environment);
        Assert.Equal(TradingAccessMode.TradingDisabled, result.Data.AccessMode);
        Assert.False(result.Data.LiveTradingEnabled);
        Assert.Equal(ServiceHealthState.NotConfigured, result.Data.ProviderRestHealth);
        Assert.Equal(ServiceHealthState.NotConfigured, result.Data.ProviderStreamHealth);
        Assert.False(result.Data.CredentialLoaded);
        Assert.Equal(0, result.Data.PendingApprovalCount);
        Assert.Equal(now, result.Data.CurrentUtc);
        Assert.Equal(now, result.TimestampUtc);
        Assert.DoesNotContain(context.TokenService.CurrentToken, json, StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
