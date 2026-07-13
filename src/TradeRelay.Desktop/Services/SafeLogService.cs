using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace TradeRelay.Desktop.Services;

internal enum SafeLogLevel
{
    Information,
    Warning,
    Error
}

internal sealed record SafeDiagnosticError(
    DateTimeOffset TimestampUtc,
    SafeLogLevel Level,
    string Code,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string> Properties,
    string? ExceptionType);

internal sealed class SafeLogService(
    ApplicationDataPaths paths,
    TimeProvider timeProvider,
    SensitiveDataRedactor redactor) : IHostedService
{
    private const int ErrorLimit = 100;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _errorsLock = new();
    private readonly List<SafeDiagnosticError> _errors = [];

    public string? LastFailure { get; private set; }
    public event EventHandler<SafeDiagnosticError>? ErrorWritten;
    public string DirectoryPath => paths.LogsDirectory;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            RetainLatestFiles();
        }
        catch
        {
            LastFailure = "Safe log storage is unavailable.";
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public IReadOnlyList<SafeDiagnosticError> GetRecentErrors(int maximum = 50)
    {
        lock (_errorsLock)
        {
            return _errors.OrderByDescending(item => item.TimestampUtc).Take(Math.Clamp(maximum, 0, ErrorLimit)).ToArray();
        }
    }

    public async Task<bool> TryWriteAsync(
        SafeLogLevel level,
        string code,
        string category,
        string message,
        IReadOnlyDictionary<string, string>? properties = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new SafeDiagnosticError(
            timeProvider.GetUtcNow(),
            level,
            NormalizeCode(code),
            NormalizeCategory(category),
            redactor.Redact(message) ?? "No safe message was provided.",
            redactor.RedactProperties(properties),
            exception?.GetType().FullName);

        if (level is SafeLogLevel.Warning or SafeLogLevel.Error) AddError(entry);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            string file = Path.Combine(paths.LogsDirectory, $"traderelay-{entry.TimestampUtc.UtcDateTime:yyyy-MM-dd}.log");
            string json = JsonSerializer.Serialize(entry);
            redactor.EnsureSafeJson(json, []);
            await File.AppendAllTextAsync(file, json + Environment.NewLine, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            LastFailure = null;
            RetainLatestFiles();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            LastFailure = "Safe log storage is unavailable.";
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void AddError(SafeDiagnosticError entry)
    {
        lock (_errorsLock)
        {
            _errors.Add(entry);
            if (_errors.Count > ErrorLimit) _errors.RemoveRange(0, _errors.Count - ErrorLimit);
        }
        try { ErrorWritten?.Invoke(this, entry); } catch { }
    }

    private void RetainLatestFiles()
    {
        if (!Directory.Exists(paths.LogsDirectory)) return;
        foreach (string file in Directory.GetFiles(paths.LogsDirectory, "traderelay-*.log").OrderDescending().Skip(7))
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static string NormalizeCode(string value) => string.IsNullOrWhiteSpace(value)
        ? "UNSPECIFIED"
        : new string(value.Trim().ToUpperInvariant().Where(character => char.IsAsciiLetterOrDigit(character) || character == '_').Take(64).ToArray());

    private static string NormalizeCategory(string value) => string.IsNullOrWhiteSpace(value)
        ? "general"
        : new string(value.Trim().ToLowerInvariant().Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.').Take(64).ToArray());
}
