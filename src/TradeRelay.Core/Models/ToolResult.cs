namespace TradeRelay.Core.Models;

/// <summary>
/// Wraps an MCP tool result in a consistent, non-secret response envelope.
/// </summary>
/// <typeparam name="T">The structured result data type.</typeparam>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Code">A stable machine-readable result code.</param>
/// <param name="Message">A safe user-facing result message.</param>
/// <param name="Data">The structured result data, when available.</param>
/// <param name="CorrelationId">The identifier used to correlate this operation with application logs.</param>
/// <param name="Environment">The trading environment used by the operation.</param>
/// <param name="TimestampUtc">The UTC timestamp when the result was produced.</param>
public sealed record ToolResult<T>(
    bool Success,
    string Code,
    string Message,
    T? Data,
    string CorrelationId,
    TradingEnvironment Environment,
    DateTimeOffset TimestampUtc);
