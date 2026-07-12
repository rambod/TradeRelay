using System.Diagnostics;

namespace TradeRelay.Desktop.Security;

internal sealed class LinuxSecretServiceSecretStore : IProtectedSecretStore
{
    public bool CanPersist => OperatingSystem.IsLinux() && FindExecutable() is not null;

    public async Task SaveAsync(string id, string value, CancellationToken cancellationToken)
    {
        string executable = EnsureAvailable();
        using Process process = Start(executable, ["store", "--label=TradeRelay protected value", "application", "TradeRelay", "id", id], redirectInput: true);
        await process.StandardInput.WriteAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(process);
    }

    public async Task<string?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        string executable = EnsureAvailable();
        using Process process = Start(executable, ["lookup", "application", "TradeRelay", "id", id], redirectInput: false);
        string value = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 1 ? null : process.ExitCode == 0 ? value.TrimEnd('\r', '\n') : throw new InvalidOperationException("Secret Service could not load the protected value.");
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        string executable = EnsureAvailable();
        using Process process = Start(executable, ["clear", "application", "TradeRelay", "id", id], redirectInput: false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(process);
    }

    private static Process Start(string executable, IEnumerable<string> arguments, bool redirectInput)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = redirectInput, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("Secret Service could not be started.");
    }

    private static void EnsureSuccess(Process process) { if (process.ExitCode != 0) throw new InvalidOperationException("Secret Service could not update the protected value."); }
    private string EnsureAvailable() => FindExecutable() ?? throw new PlatformNotSupportedException("Secret Service is unavailable.");
    private static string? FindExecutable() => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Select(path => Path.Combine(path, "secret-tool")).FirstOrDefault(File.Exists);
}
