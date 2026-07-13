using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;
using TradeRelay.Core.Risk;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class SystemTools(
    LocalMcpServerHost serverHost,
    AppSettings settings,
    IExchangeSessionCoordinator sessions,
    PreparedOrderStore preparedOrderStore,
    LiveActionConfirmationStore liveConfirmations,
    TradingControlService tradingControl,
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
        sessions.TryResolve(null, out ProviderSessionAccess? selected, out _, out _);
        TradingEnvironment environment = selected?.Environment ?? settings.Bybit.Environment;
        bool writeCapable = selected?.Descriptor.Capabilities.HasFlag(ProviderCapabilities.TradingWrite) == true;
        bool manualApprovalRequired = writeCapable && (environment == TradingEnvironment.Live
            ? settings.Risk.RequireManualApprovalForLive
            : settings.Risk.RequireManualApprovalForDemo);
        McpServerSnapshot server = serverHost.Snapshot;
        ProviderConnectionSnapshot provider = selected?.Snapshot ?? sessions.Sessions[0].Snapshot;

        var status = new SystemStatusSnapshot(
            metadata.Version,
            server.State,
            server.Url,
            selected?.Descriptor.DisplayName ?? "Bybit",
            environment,
            writeCapable ? tradingControl.Snapshot.Enabled ? TradingAccessMode.TradingEnabled : TradingAccessMode.TradingDisabled : TradingAccessMode.ReadOnly,
            LiveTradingEnabled: writeCapable && environment == TradingEnvironment.Live && tradingControl.Snapshot.Enabled,
            manualApprovalRequired,
            provider.RestHealth,
            provider.StreamHealth,
            provider.CredentialLoaded,
            provider.CredentialSummary,
            PendingApprovalCount: preparedOrderStore.GetPending().Count + liveConfirmations.GetPending().Count,
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
