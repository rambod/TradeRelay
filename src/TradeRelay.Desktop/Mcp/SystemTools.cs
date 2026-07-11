using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class SystemTools(
    LocalMcpServerHost serverHost,
    AppSettings settings,
    ApplicationMetadata metadata,
    TimeProvider timeProvider)
{
    [McpServerTool(
        Name = "get_system_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the current non-secret TradeRelay application, MCP server, provider, and safety status.")]
    public ToolResult<SystemStatusSnapshot> GetSystemStatus()
    {
        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        TradingEnvironment environment = settings.Bybit.Environment;
        bool manualApprovalRequired = environment == TradingEnvironment.Live
            ? settings.Risk.RequireManualApprovalForLive
            : settings.Risk.RequireManualApprovalForDemo;
        McpServerSnapshot server = serverHost.Snapshot;

        var status = new SystemStatusSnapshot(
            metadata.Version,
            server.State,
            server.Url,
            "Bybit",
            environment,
            TradingAccessMode.ReadOnly,
            LiveTradingEnabled: false,
            manualApprovalRequired,
            ServiceHealthState.NotConfigured,
            ServiceHealthState.NotConfigured,
            CredentialLoaded: false,
            CredentialTypeSummary: "None",
            PendingApprovalCount: 0,
            timestamp);

        return new ToolResult<SystemStatusSnapshot>(
            Success: true,
            Code: "OK",
            Message: "TradeRelay system status is available.",
            Data: status,
            CorrelationId: Guid.NewGuid().ToString("N"),
            Environment: environment,
            TimestampUtc: timestamp);
    }
}
