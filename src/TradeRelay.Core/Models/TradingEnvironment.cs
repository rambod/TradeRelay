namespace TradeRelay.Core.Models;

/// <summary>
/// Identifies the exchange environment selected for the application.
/// </summary>
public enum TradingEnvironment
{
    /// <summary>
    /// Uses the exchange's demo environment.
    /// </summary>
    Demo,

    /// <summary>
    /// Uses the exchange's live environment.
    /// </summary>
    Live
}
