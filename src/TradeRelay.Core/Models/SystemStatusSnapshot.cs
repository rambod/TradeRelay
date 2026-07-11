namespace TradeRelay.Core.Models;

/// <summary>
/// Represents the non-secret system state returned by the MCP status tool.
/// </summary>
/// <param name="AppVersion">The TradeRelay product version.</param>
/// <param name="ServerState">The local MCP server state.</param>
/// <param name="ServerUrl">The local MCP endpoint URL.</param>
/// <param name="SelectedProvider">The selected exchange-provider name.</param>
/// <param name="Environment">The selected trading environment.</param>
/// <param name="AccessMode">The current trading access mode.</param>
/// <param name="LiveTradingEnabled">Whether live trading is enabled for this session.</param>
/// <param name="ManualApprovalRequired">Whether the current environment requires manual approval.</param>
/// <param name="ProviderRestHealth">The selected provider's REST health.</param>
/// <param name="ProviderStreamHealth">The selected provider's private-stream health.</param>
/// <param name="CredentialLoaded">Whether exchange credentials are loaded.</param>
/// <param name="CredentialTypeSummary">A non-secret summary of the loaded credential type.</param>
/// <param name="PendingApprovalCount">The number of orders awaiting approval.</param>
/// <param name="CurrentUtc">The current UTC timestamp.</param>
public sealed record SystemStatusSnapshot(
    string AppVersion,
    McpServerState ServerState,
    string ServerUrl,
    string SelectedProvider,
    TradingEnvironment Environment,
    TradingAccessMode AccessMode,
    bool LiveTradingEnabled,
    bool ManualApprovalRequired,
    ServiceHealthState ProviderRestHealth,
    ServiceHealthState ProviderStreamHealth,
    bool CredentialLoaded,
    string CredentialTypeSummary,
    int PendingApprovalCount,
    DateTimeOffset CurrentUtc);
