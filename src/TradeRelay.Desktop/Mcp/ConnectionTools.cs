using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class ConnectionTools(ExchangeConnectionManager manager, AppSettings settings, TimeProvider timeProvider)
{
    [McpServerTool(Name = "test_bybit_connection", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Reports the currently loaded Bybit connection and non-secret API-key permission status. Credentials can only be entered in the TradeRelay UI.")]
    public ToolResult<ExchangeConnectionResult> TestConnection()
    {
        ProviderConnectionSnapshot snapshot = manager.Snapshot;
        if (!snapshot.CredentialLoaded || snapshot.CredentialInfo is null)
            return ToolResponse.Failure<ExchangeConnectionResult>("CREDENTIALS_MISSING", "Load and validate Bybit credentials in TradeRelay first.", settings.Bybit.Environment, timeProvider);
        var result = new ExchangeConnectionResult(snapshot.RestHealth == ServiceHealthState.Healthy, "OK", "Current Bybit connection status.", snapshot.CredentialInfo, snapshot.RestHealth, snapshot.StreamHealth);
        return ToolResponse.Success(result, "Bybit connection status retrieved.", settings.Bybit.Environment, timeProvider);
    }
}
