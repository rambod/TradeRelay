namespace TradeRelay.Core.Models;

/// <summary>
/// Describes whether exchange write operations are permitted for the current session.
/// </summary>
public enum TradingAccessMode
{
    /// <summary>
    /// Allows read and calculation operations only.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Indicates that trading is available in principle but currently disabled.
    /// </summary>
    TradingDisabled,

    /// <summary>
    /// Indicates that eligible trading operations may pass to later safety gates.
    /// </summary>
    TradingEnabled
}
