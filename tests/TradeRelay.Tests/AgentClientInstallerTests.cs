using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class AgentClientInstallerTests
{
    [Fact]
    public async Task Install_UsesArgumentArraysCopiesCanonicalSkillAndUninstallsOwnedTargetsOnly()
    {
        var runner = new FakeRunner();
        var files = new FakeFiles();
        var installer = new AgentClientInstaller(runner, files);
        ClientInstallationPreview preview = installer.PreviewInstall(AgentClientKind.Codex, "http://127.0.0.1:5050/mcp");

        Assert.Equal(["mcp", "add", "traderelay", "--url", "http://127.0.0.1:5050/mcp"], preview.Commands[0].Arguments);
        ClientInstallationResult installed = await installer.InstallAsync(AgentClientKind.Codex, "http://127.0.0.1:5050/mcp", default);
        Assert.True(installed.Success);
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(preview.Commands[0].Arguments));
        Assert.Contains(files.Directories, path => path.EndsWith(".agents/skills/traderelay-operator", StringComparison.Ordinal));

        ClientInstallationResult removed = await installer.UninstallAsync(AgentClientKind.Codex, default);
        Assert.True(removed.Success);
        Assert.Empty(files.Directories);
    }

    [Fact]
    public void Preview_UsesSupportedPerUserCommandsWithoutSecrets()
    {
        var installer = new AgentClientInstaller(new FakeRunner(), new FakeFiles());
        const string endpoint = "http://127.0.0.1:5050/mcp";

        ClientInstallationPreview claude = installer.PreviewInstall(AgentClientKind.ClaudeCode, endpoint);
        Assert.Equal(["mcp", "add", "--transport", "http", "--scope", "user", "traderelay", endpoint], claude.Commands.Single().Arguments);

        ClientInstallationPreview gemini = installer.PreviewInstall(AgentClientKind.GeminiCli, endpoint);
        Assert.Equal(["mcp", "add", "--transport", "http", "--scope", "user", "traderelay", endpoint], gemini.Commands[0].Arguments);
        Assert.Equal("skills", gemini.Commands[1].Arguments[0]);
        Assert.Equal("install", gemini.Commands[1].Arguments[1]);
        Assert.DoesNotContain(gemini.Commands.SelectMany(command => command.Arguments), argument => argument.Contains("token", StringComparison.OrdinalIgnoreCase) || argument.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Install_StopsOnConflictingSkillAndMissingCli()
    {
        var runner = new FakeRunner();
        var files = new FakeFiles();
        string target = Path.Combine(files.UserHome, ".claude", "skills", "traderelay-operator");
        files.Directories.Add(target);
        var installer = new AgentClientInstaller(runner, files);

        ClientInstallationResult conflict = await installer.InstallAsync(AgentClientKind.ClaudeCode, "http://127.0.0.1:5050/mcp", default);
        Assert.Equal("CLIENT_CONFLICT", conflict.Code);
        Assert.Empty(runner.Calls);

        runner.Missing.Add("gemini");
        ClientInstallationResult missing = await installer.InstallAsync(AgentClientKind.GeminiCli, "http://127.0.0.1:5050/mcp", default);
        Assert.Equal("CLIENT_NOT_FOUND", missing.Code);
    }

    private sealed class FakeRunner : IClientProcessRunner
    {
        public List<(string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);
        public string? FindExecutable(string name) => Missing.Contains(name) ? null : "/fake/" + name;
        public Task<ClientProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) { Calls.Add((executable, arguments)); return Task.FromResult(arguments.SequenceEqual(["mcp", "get", "traderelay"]) ? new ClientProcessResult(1, string.Empty) : new ClientProcessResult(0, "1.0.0")); }
    }
    private sealed class FakeFiles : IClientFileSystem
    {
        public string UserHome => "/fake/home";
        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public bool FileExists(string path) => Files.Contains(path);
        public void CopyOwnedDirectory(string source, string target) { Directories.Add(target); Files.Add(Path.Combine(target, ".traderelay-owned")); }
        public void DeleteOwnedDirectory(string target) { if (!Files.Contains(Path.Combine(target, ".traderelay-owned"))) throw new InvalidOperationException(); Directories.Remove(target); Files.Remove(Path.Combine(target, ".traderelay-owned")); }
    }
}
