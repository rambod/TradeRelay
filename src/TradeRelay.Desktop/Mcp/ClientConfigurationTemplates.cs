namespace TradeRelay.Desktop.Mcp;

internal static class ClientConfigurationTemplates
{
    public static string CreateCodex(string endpoint) =>
        $$"""
        [mcp_servers.traderelay]
        url = "{{endpoint}}"
        bearer_token_env_var = "TRADERELAY_MCP_TOKEN"
        default_tools_approval_mode = "writes"
        startup_timeout_sec = 10
        tool_timeout_sec = 60
        enabled = true
        """;

    public static string CreateClaudeCodeCommand(string endpoint) =>
        $"claude mcp add --transport http traderelay {endpoint} " +
        "--header \"Authorization: Bearer ${TRADERELAY_MCP_TOKEN}\"";
}
