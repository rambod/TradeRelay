namespace TradeRelay.Core.Models;

/// <summary>
/// Represents the current user-facing state of the local MCP server.
/// </summary>
/// <param name="State">The current server lifecycle state.</param>
/// <param name="Url">The configured or active MCP endpoint URL.</param>
/// <param name="Port">The configured or active loopback port.</param>
/// <param name="AuthenticationEnabled">Whether bearer authentication is enabled.</param>
/// <param name="ConnectedSessionCount">The connected session count when the transport exposes one.</param>
/// <param name="LastError">The latest safe user-facing server error, when present.</param>
public sealed record McpServerSnapshot(
    McpServerState State,
    string Url,
    int Port,
    bool AuthenticationEnabled,
    int? ConnectedSessionCount,
    string? LastError);
