using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TradeRelay.Tests;

public sealed partial class ReleaseEngineeringTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void SharedMetadataAndDesktopPublishDefinitionAreProductionReady()
    {
        XDocument shared = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));
        string version = shared.Descendants("Version").Single().Value;
        Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+$", version);
        Assert.Contains($"Current version: `{version}`", File.ReadAllText(Path.Combine(Root, "README.md")), StringComparison.Ordinal);
        Assert.Equal("TradeRelay", shared.Descendants("Product").Single().Value);
        Assert.Equal("true", shared.Descendants("Deterministic").Single().Value);
        Assert.Equal("true", shared.Descendants("TreatWarningsAsErrors").Single().Value);

        XDocument desktop = XDocument.Load(Path.Combine(Root, "src/TradeRelay.Desktop/TradeRelay.Desktop.csproj"));
        string[] runtimeIdentifiers = desktop.Descendants("RuntimeIdentifiers").Single().Value.Split(';');
        Assert.Equal(
            ["osx-arm64", "osx-x64", "win-arm64", "win-x64", "linux-arm64", "linux-x64"],
            runtimeIdentifiers);
        Assert.Equal("TradeRelay", desktop.Descendants("AssemblyName").Single().Value);
        Assert.Equal("false", desktop.Descendants("PublishTrimmed").Single().Value);
        Assert.Equal("false", desktop.Descendants("PublishAot").Single().Value);
    }

    [Fact]
    public void AllPackageDefinitionsUseTradeRelayProductNamesAndIcons()
    {
        string mac = File.ReadAllText(Path.Combine(Root, "eng/package-macos.sh"));
        string windows = File.ReadAllText(Path.Combine(Root, "eng/package-windows.ps1"));
        string linux = File.ReadAllText(Path.Combine(Root, "eng/package-linux.sh"));
        Assert.Contains("TradeRelay.app", mac, StringComparison.Ordinal);
        Assert.Contains("io.github.rambod.TradeRelay", mac, StringComparison.Ordinal);
        Assert.Contains("LSMinimumSystemVersion", mac, StringComparison.Ordinal);
        Assert.Contains("TradeRelay.exe", windows, StringComparison.Ordinal);
        Assert.Contains("TradeRelay-$version-$rid.tar.gz", linux, StringComparison.Ordinal);
        Assert.Contains("launch-traderelay", linux, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Root, "assets/icons/TradeRelay.ico")));
        Assert.True(File.Exists(Path.Combine(Root, "assets/icons/TradeRelay.icns")));
        Assert.True(File.Exists(Path.Combine(Root, "assets/icons/TradeRelay-1024.png")));
        Assert.True(File.Exists(Path.Combine(Root, "eng/generate-icons.sh")));
    }

    [Fact]
    public void WorkflowsPinActionsAndReleaseCoversSixNativeRunners()
    {
        string[] workflows = Directory.GetFiles(Path.Combine(Root, ".github/workflows"), "*.yml");
        foreach (string workflow in workflows)
        {
            foreach (string line in File.ReadLines(workflow).Where(line => line.TrimStart().StartsWith("uses:", StringComparison.Ordinal)))
            {
                Assert.Matches(PinnedActionPattern(), line);
            }
        }

        string release = File.ReadAllText(Path.Combine(Root, ".github/workflows/release.yml"));
        foreach (string runtime in new[] { "osx-arm64", "osx-x64", "win-arm64", "win-x64", "linux-arm64", "linux-x64" })
        {
            Assert.Contains($"rid: {runtime}", release, StringComparison.Ordinal);
        }
        Assert.Contains("windows-11-arm", release, StringComparison.Ordinal);
        Assert.Contains("ubuntu-24.04-arm", release, StringComparison.Ordinal);
        Assert.Contains("actions/attest-build-provenance", release, StringComparison.Ordinal);
    }

    [Fact]
    public void TrackedSourceContainsNoLegacyDesktopProjectName()
    {
        string legacyName = string.Concat("TradeRelay.", "App");
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && Path.GetExtension(file) is ".cs" or ".csproj" or ".sln" or ".axaml" or ".md"))
        {
            Assert.DoesNotContain(legacyName, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CanonicalOperatorSkillAndClientGuidesPreserveSafetyBoundaries()
    {
        string skillRoot = Path.Combine(Root, "integrations/skills/traderelay-operator");
        string skill = File.ReadAllText(Path.Combine(skillRoot, "SKILL.md"));
        string metadata = File.ReadAllText(Path.Combine(skillRoot, "agents/openai.yaml"));
        Assert.Contains("get_system_status", skill, StringComparison.Ordinal);
        Assert.Contains("Never enable Demo or Live trading", skill, StringComparison.Ordinal);
        Assert.Contains("Never retry order placement after an ambiguous response", skill, StringComparison.Ordinal);
        Assert.Contains("reconciled outcome", skill, StringComparison.Ordinal);
        Assert.Contains("value: \"traderelay\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("TRADERELAY_MCP_TOKEN", metadata, StringComparison.Ordinal);

        string codex = File.ReadAllText(Path.Combine(Root, "docs/CODEX_SETUP.md"));
        string claude = File.ReadAllText(Path.Combine(Root, "docs/CLAUDE_CODE_SETUP.md"));
        string gemini = File.ReadAllText(Path.Combine(Root, "docs/GEMINI_CLI_SETUP.md"));
        Assert.Contains("${TRADERELAY_MCP_TOKEN}", claude, StringComparison.Ordinal);
        Assert.Contains("OAuth", codex, StringComparison.Ordinal);
        Assert.Contains("OAuth", gemini, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"uses:\s+[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}(?:\s+#.*)?$")]
    private static partial Regex PinnedActionPattern();
}
