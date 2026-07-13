using TradeRelay.Desktop.Mcp;
using Xunit;

namespace TradeRelay.Tests;

public sealed class ClientConfigurationTemplateTests
{
    [Fact]
    public void CodexTemplateUsesBearerEnvironmentVariableWithoutTokenValue()
    {
        string template = ClientConfigurationTemplates.CreateCodex("http://127.0.0.1:5050/mcp");
        Assert.Contains("bearer_token_env_var = \"TRADERELAY_MCP_TOKEN\"", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization:", template, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClaudeCommandPreservesEnvironmentReferenceAgainstShellExpansion()
    {
        string command = ClientConfigurationTemplates.CreateClaudeCodeCommand("http://127.0.0.1:5050/mcp");
        Assert.Contains("'Authorization: Bearer ${TRADERELAY_MCP_TOKEN}'", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--header \"", command, StringComparison.Ordinal);
    }
}
