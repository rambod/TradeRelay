using System.Diagnostics;

namespace TradeRelay.Desktop.Services;

internal enum AgentClientKind { Codex, ClaudeCode, GeminiCli }
internal enum AgentClientInstallationState { NotDetected, Available, Installed, Conflict, Error }
internal sealed record AgentClientStatus(AgentClientKind Kind, string DisplayName, AgentClientInstallationState State, string? Version, string SkillTarget, string? SafeError);
internal sealed record ClientProcessCommand(string Executable, IReadOnlyList<string> Arguments);
internal sealed record ClientInstallationPreview(AgentClientKind Kind, IReadOnlyList<ClientProcessCommand> Commands, IReadOnlyList<string> Targets);
internal sealed record ClientInstallationResult(bool Success, string Code, string Message);
internal sealed record ClientProcessResult(int ExitCode, string StandardOutput);

internal interface IClientProcessRunner
{
    string? FindExecutable(string name);
    Task<ClientProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

internal interface IClientFileSystem
{
    string UserHome { get; }
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CopyOwnedDirectory(string source, string target);
    void DeleteOwnedDirectory(string target);
}

internal sealed class ClientProcessRunner : IClientProcessRunner
{
    public string? FindExecutable(string name)
    {
        string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, name + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public async Task<ClientProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("The client process could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
        return new ClientProcessResult(process.ExitCode, output.Length > 200 ? output[..200] : output);
    }
}

internal sealed class ClientFileSystem : IClientFileSystem
{
    private const string Marker = ".traderelay-owned";
    public string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);

    public void CopyOwnedDirectory(string source, string target)
    {
        if (Directory.Exists(target) && !File.Exists(Path.Combine(target, Marker))) throw new InvalidOperationException("A conflicting skill directory is not owned by TradeRelay.");
        Directory.CreateDirectory(target);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
        File.WriteAllText(Path.Combine(target, Marker), "TradeRelay-owned skill installation.\n");
    }

    public void DeleteOwnedDirectory(string target)
    {
        if (!File.Exists(Path.Combine(target, Marker))) throw new InvalidOperationException("TradeRelay will not remove a directory it does not own.");
        Directory.Delete(target, true);
    }
}

internal sealed class AgentClientInstaller(IClientProcessRunner processes, IClientFileSystem files)
{
    private readonly string _skillSource = Path.Combine(AppContext.BaseDirectory, "Skills", "traderelay-operator");

    public async Task<IReadOnlyList<AgentClientStatus>> DetectAsync(CancellationToken cancellationToken)
    {
        var results = new List<AgentClientStatus>();
        foreach (AgentClientKind kind in Enum.GetValues<AgentClientKind>())
        {
            ClientDefinition definition = Definition(kind);
            string target = SkillTarget(kind);
            string? executable = processes.FindExecutable(definition.Executable);
            if (executable is null) { results.Add(new(kind, definition.DisplayName, AgentClientInstallationState.NotDetected, null, target, "CLI not found. Manual recovery instructions are available.")); continue; }
            try
            {
                ClientProcessResult version = await processes.RunAsync(executable, ["--version"], cancellationToken).ConfigureAwait(false);
                bool conflict = files.DirectoryExists(target) && !files.FileExists(Path.Combine(target, ".traderelay-owned"));
                bool installed = kind == AgentClientKind.GeminiCli ? files.FileExists(Path.Combine(target, ".traderelay-owned")) : files.DirectoryExists(target) && !conflict;
                results.Add(new(kind, definition.DisplayName, conflict ? AgentClientInstallationState.Conflict : installed ? AgentClientInstallationState.Installed : AgentClientInstallationState.Available, version.ExitCode == 0 ? version.StandardOutput.Trim() : null, target, conflict ? "A conflicting traderelay-operator skill exists and will not be overwritten." : version.ExitCode == 0 ? null : "The CLI was found but did not report a version."));
            }
            catch { results.Add(new(kind, definition.DisplayName, AgentClientInstallationState.Error, null, target, "The CLI could not be inspected safely.")); }
        }
        return results;
    }

    public ClientInstallationPreview PreviewInstall(AgentClientKind kind, string endpoint)
    {
        ClientDefinition definition = Definition(kind);
        return new ClientInstallationPreview(kind, InstallCommands(kind, definition.Executable, endpoint), [_skillSource, SkillTarget(kind)]);
    }

    public async Task<ClientInstallationResult> InstallAsync(AgentClientKind kind, string endpoint, CancellationToken cancellationToken)
    {
        ClientDefinition definition = Definition(kind);
        string? executable = processes.FindExecutable(definition.Executable);
        if (executable is null) return new(false, "CLIENT_NOT_FOUND", $"{definition.DisplayName} was not found. Use the displayed manual recovery steps.");
        string target = SkillTarget(kind);
        if (files.DirectoryExists(target) && !files.FileExists(Path.Combine(target, ".traderelay-owned"))) return new(false, "CLIENT_CONFLICT", "A conflicting traderelay-operator skill exists. TradeRelay did not overwrite it.");
        if (!Directory.Exists(_skillSource)) return new(false, "SKILL_NOT_AVAILABLE", "The packaged TradeRelay operator skill is missing.");
        try
        {
            ClientProcessResult existing = await processes.RunAsync(executable, ["mcp", "get", "traderelay"], cancellationToken).ConfigureAwait(false);
            bool owned = files.FileExists(Path.Combine(target, ".traderelay-owned"));
            if (existing.ExitCode == 0 && !owned) return new(false, "CLIENT_CONFLICT", "A conflicting traderelay MCP entry already exists. TradeRelay did not change it.");
            if (existing.ExitCode == 0 && owned) await processes.RunAsync(executable, kind == AgentClientKind.ClaudeCode ? ["mcp", "remove", "--scope", "user", "traderelay"] : ["mcp", "remove", "traderelay"], cancellationToken).ConfigureAwait(false);
            files.CopyOwnedDirectory(_skillSource, target);
            foreach (ClientProcessCommand command in InstallCommands(kind, executable, endpoint))
            {
                ClientProcessResult result = await processes.RunAsync(command.Executable, command.Arguments, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0) return new(false, "CLIENT_INSTALL_FAILED", $"{definition.DisplayName} rejected an installation step. Existing unknown configuration was not rewritten.");
            }
            return new(true, "OK", $"{definition.DisplayName} MCP and traderelay-operator skill installation completed. Start the client to pair with Read & Plan scopes.");
        }
        catch (OperationCanceledException) { throw; }
        catch { return new(false, "CLIENT_INSTALL_FAILED", "Installation could not be completed safely. Unknown client configuration was not rewritten."); }
    }

    public async Task<ClientInstallationResult> UninstallAsync(AgentClientKind kind, CancellationToken cancellationToken)
    {
        ClientDefinition definition = Definition(kind);
        string? executable = processes.FindExecutable(definition.Executable);
        string target = SkillTarget(kind);
        try
        {
            if (executable is not null)
            {
                IReadOnlyList<string> remove = kind switch
                {
                    AgentClientKind.Codex => ["mcp", "remove", "traderelay"],
                    AgentClientKind.ClaudeCode => ["mcp", "remove", "--scope", "user", "traderelay"],
                    _ => ["mcp", "remove", "--scope", "user", "traderelay"],
                };
                await processes.RunAsync(executable, remove, cancellationToken).ConfigureAwait(false);
            }
            if (files.DirectoryExists(target)) files.DeleteOwnedDirectory(target);
            return new(true, "OK", $"TradeRelay-owned {definition.DisplayName} files were removed.");
        }
        catch (OperationCanceledException) { throw; }
        catch { return new(false, "CLIENT_UNINSTALL_FAILED", "TradeRelay stopped because the target was not clearly owned by TradeRelay."); }
    }

    private IReadOnlyList<ClientProcessCommand> InstallCommands(AgentClientKind kind, string executable, string endpoint) => kind switch
    {
        AgentClientKind.Codex => [new(executable, ["mcp", "add", "traderelay", "--url", endpoint])],
        AgentClientKind.ClaudeCode => [new(executable, ["mcp", "add", "--transport", "http", "--scope", "user", "traderelay", endpoint])],
        AgentClientKind.GeminiCli => [new(executable, ["mcp", "add", "--transport", "http", "--scope", "user", "traderelay", endpoint]), new(executable, ["skills", "install", _skillSource])],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private string SkillTarget(AgentClientKind kind) => kind switch
    {
        AgentClientKind.Codex => Path.Combine(files.UserHome, ".agents", "skills", "traderelay-operator"),
        AgentClientKind.ClaudeCode => Path.Combine(files.UserHome, ".claude", "skills", "traderelay-operator"),
        AgentClientKind.GeminiCli => Path.Combine(files.UserHome, ".gemini", "skills", "traderelay-operator"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ClientDefinition Definition(AgentClientKind kind) => kind switch
    {
        AgentClientKind.Codex => new("codex", "Codex"),
        AgentClientKind.ClaudeCode => new("claude", "Claude Code"),
        AgentClientKind.GeminiCli => new("gemini", "Gemini CLI"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    private sealed record ClientDefinition(string Executable, string DisplayName);
}
