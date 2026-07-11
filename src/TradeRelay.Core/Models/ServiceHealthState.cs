namespace TradeRelay.Core.Models;

/// <summary>
/// Describes the normalized health of a provider-backed service.
/// </summary>
public enum ServiceHealthState
{
    /// <summary>
    /// The service has not been configured for the current application session.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// The service is operating normally.
    /// </summary>
    Healthy,

    /// <summary>
    /// The service is available with reduced capability or reliability.
    /// </summary>
    Degraded,

    /// <summary>
    /// The service is unavailable.
    /// </summary>
    Unavailable
}
