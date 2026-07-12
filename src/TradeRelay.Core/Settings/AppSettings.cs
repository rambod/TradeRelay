using TradeRelay.Core.Models;

namespace TradeRelay.Core.Settings;

/// <summary>
/// Contains the application's non-secret persisted settings.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Gets the local MCP server settings.
    /// </summary>
    public ServerSettings Server { get; set; } = new();

    /// <summary>
    /// Gets the Bybit environment and credential-retention preferences.
    /// </summary>
    public BybitSettings Bybit { get; set; } = new();

    /// <summary>
    /// Gets the configured trading risk limits.
    /// </summary>
    public RiskSettings Risk { get; set; } = new();
}

/// <summary>
/// Contains non-secret settings for the local MCP server.
/// </summary>
public sealed class ServerSettings
{
    /// <summary>
    /// Gets the loopback port used by the local MCP server.
    /// </summary>
    public int Port { get; set; } = 5050;

    /// <summary>
    /// Gets a value indicating whether the MCP server should start with the desktop application.
    /// </summary>
    public bool StartAutomatically { get; set; }
}

/// <summary>
/// Contains non-secret Bybit connection preferences.
/// </summary>
public sealed class BybitSettings
{
    /// <summary>
    /// Gets the selected trading environment.
    /// </summary>
    public TradingEnvironment Environment { get; set; } = TradingEnvironment.Demo;

    /// <summary>
    /// Gets a value indicating whether protected device storage should be requested for credentials.
    /// </summary>
    public bool RememberCredentials { get; set; }
}

/// <summary>
/// Contains limits applied by the future risk-validation flow.
/// </summary>
public sealed class RiskSettings
{
    /// <summary>
    /// Gets the symbols that may pass risk validation.
    /// </summary>
    public HashSet<string> AllowedSymbols { get; set; } = new(
        ["BTCUSDT", "ETHUSDT", "XRPUSDT"],
        StringComparer.Ordinal);

    /// <summary>
    /// Gets the maximum estimated account risk allowed for one trade, as a percentage.
    /// </summary>
    public decimal MaxRiskPerTradePercent { get; init; } = 0.25m;

    /// <summary>
    /// Gets the maximum order notional in US dollars.
    /// </summary>
    public decimal MaxOrderNotionalUsd { get; init; } = 500m;

    /// <summary>
    /// Gets the maximum number of open positions.
    /// </summary>
    public int MaxOpenPositions { get; init; } = 2;

    /// <summary>
    /// Gets the maximum permitted leverage.
    /// </summary>
    public decimal MaxLeverage { get; init; } = 3m;

    /// <summary>
    /// Gets a value indicating whether prepared orders require a stop loss.
    /// </summary>
    public bool RequireStopLoss { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether demo orders require manual approval.
    /// </summary>
    public bool RequireManualApprovalForDemo { get; init; }

    /// <summary>
    /// Gets a value indicating whether live orders require manual approval.
    /// </summary>
    public bool RequireManualApprovalForLive { get; init; } = true;

    /// <summary>
    /// Gets the number of seconds before a prepared order expires.
    /// </summary>
    public int PreparedOrderExpirySeconds { get; init; } = 120;
}
