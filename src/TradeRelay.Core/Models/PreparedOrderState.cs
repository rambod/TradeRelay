namespace TradeRelay.Core.Models;

/// <summary>
/// Describes the lifecycle state of an immutable prepared order.
/// </summary>
public enum PreparedOrderState
{
    /// <summary>
    /// The order is waiting for an approval decision.
    /// </summary>
    PendingApproval,

    /// <summary>
    /// The order was approved without changing its immutable plan.
    /// </summary>
    Approved,

    /// <summary>
    /// The order was rejected.
    /// </summary>
    Rejected,

    /// <summary>
    /// The order is being submitted and reconciled.
    /// </summary>
    Executing,

    /// <summary>
    /// The order completed its execution flow.
    /// </summary>
    Executed,

    /// <summary>
    /// The execution flow failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The prepared order expired before execution.
    /// </summary>
    Expired
}
