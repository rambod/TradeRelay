using TradeRelay.Core.Models;
using System.Text.Json.Serialization;

namespace TradeRelay.Core.Settings;

/// <summary>
/// Contains the application's non-secret persisted settings.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets the persisted settings schema version.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// Gets the local MCP server settings.
    /// </summary>
    public ServerSettings Server { get; set; } = new();

    /// <summary>
    /// Gets the selected provider identifier.
    /// </summary>
    public string SelectedExchange { get; set; } = "bybit";

    /// <summary>Gets non-secret settings keyed by exchange identifier.</summary>
    public Dictionary<string, ExchangeProviderSettings> Exchanges { get; set; } = new(StringComparer.Ordinal)
    {
        ["bybit"] = new ExchangeProviderSettings
        {
            Environment = TradingEnvironment.Demo,
            RememberByEnvironment = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["demo"] = false,
                ["live"] = false,
            },
        },
    };

    /// <summary>
    /// Gets the configured trading risk limits.
    /// </summary>
    public RiskSettings Risk { get; set; } = new();

    /// <summary>Gets the built-in Bybit profile used by the existing guarded write path.</summary>
    [JsonIgnore]
    public ExchangeProviderSettings Bybit => GetExchange(new ExchangeId("bybit"));

    /// <summary>Gets or creates non-secret settings for an exchange.</summary>
    public ExchangeProviderSettings GetExchange(ExchangeId exchange)
    {
        if (!Exchanges.TryGetValue(exchange.Value, out ExchangeProviderSettings? value))
        {
            value = new ExchangeProviderSettings();
            Exchanges[exchange.Value] = value;
        }

        return value;
    }
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
/// Contains non-secret connection preferences for one exchange.
/// </summary>
public sealed class ExchangeProviderSettings
{
    /// <summary>
    /// Gets the selected trading environment.
    /// </summary>
    public TradingEnvironment Environment { get; set; } = TradingEnvironment.Demo;

    /// <summary>
    /// Gets a value indicating whether protected device storage should be requested for credentials.
    /// </summary>
    public Dictionary<string, bool> RememberByEnvironment { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets protected persistence for the currently selected environment.</summary>
    [JsonIgnore]
    public bool RememberCredentials
    {
        get => ShouldRemember(Environment);
        set => SetRemember(Environment, value);
    }

    /// <summary>Gets whether protected persistence is requested for an environment.</summary>
    public bool ShouldRemember(TradingEnvironment environment) =>
        RememberByEnvironment.TryGetValue(environment.ToString().ToLowerInvariant(), out bool remember) && remember;

    /// <summary>Sets whether protected persistence is requested for an environment.</summary>
    public void SetRemember(TradingEnvironment environment, bool remember) =>
        RememberByEnvironment[environment.ToString().ToLowerInvariant()] = remember;
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
    /// Gets the maximum absolute market-price movement permitted between Live preparation and execution.
    /// </summary>
    public decimal MaxMarketPriceDeviationPercent { get; init; } = 0.50m;

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
