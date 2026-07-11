namespace TradeRelay.Core.Models;

/// <summary>
/// Describes the lifecycle state of the local MCP server.
/// </summary>
public enum McpServerState
{
    /// <summary>
    /// The server is not running.
    /// </summary>
    Stopped,

    /// <summary>
    /// The server is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// The server is running.
    /// </summary>
    Running,

    /// <summary>
    /// The server is stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// The server encountered an unrecoverable startup or runtime error.
    /// </summary>
    Faulted
}
