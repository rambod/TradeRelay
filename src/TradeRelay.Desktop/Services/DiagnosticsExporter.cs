using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.Services;

internal sealed record DiagnosticsPackage(string Name, string Version);

internal sealed record DiagnosticsSnapshot(
    string SchemaVersion,
    string ApplicationVersion,
    DateTimeOffset GeneratedAtUtc,
    string OperatingSystem,
    string ProcessArchitecture,
    string Runtime,
    TradingEnvironment SelectedEnvironment,
    McpServerSnapshot Mcp,
    ServiceHealthState ProviderRestHealth,
    ServiceHealthState ProviderPrivateStreamHealth,
    AuditHealthSnapshot Audit,
    RiskSettings RiskSettings,
    IReadOnlyList<SafeDiagnosticError> SafeErrors,
    IReadOnlyList<DiagnosticsPackage> Packages);

internal sealed record DiagnosticsExportResult(bool Success, string Code, string Message, string? FilePath);

internal interface IDiagnosticsExporter
{
    Task<DiagnosticsExportResult> ExportAsync(CancellationToken cancellationToken = default);
}

internal sealed class DiagnosticsExporter(
    AppSettings settings,
    ApplicationDataPaths paths,
    ApplicationMetadata metadata,
    LocalMcpServerHost mcpHost,
    LocalMcpTokenService tokenService,
    ExchangeConnectionManager connectionManager,
    AuditLogService auditLog,
    SafeLogService safeLog,
    SensitiveDataRedactor redactor,
    TimeProvider timeProvider) : IDiagnosticsExporter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _exportLock = new(1, 1);

    public async Task<DiagnosticsExportResult> ExportAsync(CancellationToken cancellationToken = default)
    {
        await _exportLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset timestamp = timeProvider.GetUtcNow();
            DiagnosticsSnapshot snapshot = CreateSnapshot(timestamp);
            string json = JsonSerializer.Serialize(snapshot, Options);
            redactor.EnsureSafeJson(json, [tokenService.CurrentToken]);

            Directory.CreateDirectory(paths.DiagnosticsDirectory);
            string fileName = $"traderelay-diagnostics-{timestamp.UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}.json";
            string destination = Path.Combine(paths.DiagnosticsDirectory, fileName);
            string temporary = destination + ".tmp";
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, true);
            await safeLog.TryWriteAsync(SafeLogLevel.Information, "DIAGNOSTICS_EXPORTED", "diagnostics", "A non-secret diagnostics snapshot was exported.", cancellationToken: cancellationToken).ConfigureAwait(false);
            return new(true, "OK", "Non-secret diagnostics were exported.", destination);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await safeLog.TryWriteAsync(SafeLogLevel.Error, "DIAGNOSTICS_EXPORT_FAILED", "diagnostics", "The diagnostics snapshot could not be exported.", exception: exception, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new(false, "DIAGNOSTICS_EXPORT_FAILED", "Diagnostics could not be exported. No trading safety control was changed.", null);
        }
        finally
        {
            _exportLock.Release();
        }
    }

    internal DiagnosticsSnapshot CreateSnapshot(DateTimeOffset timestamp)
    {
        ProviderConnectionSnapshot provider = connectionManager.Snapshot;
        return new(
            "1.0",
            metadata.Version,
            timestamp,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            settings.Bybit.Environment,
            mcpHost.Snapshot,
            provider.RestHealth,
            provider.StreamHealth,
            auditLog.Health,
            settings.Risk,
            safeLog.GetRecentErrors(50),
            LoadPackages());
    }

    internal static IReadOnlyList<DiagnosticsPackage> LoadPackages(string? depsFile = null)
    {
        depsFile ??= Path.Combine(AppContext.BaseDirectory, "TradeRelay.deps.json");
        if (!File.Exists(depsFile)) return [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(depsFile));
            if (!document.RootElement.TryGetProperty("libraries", out JsonElement libraries)) return [];

            return libraries.EnumerateObject()
                .Where(item => item.Value.TryGetProperty("type", out JsonElement type) && type.GetString() == "package")
                .Select(item => SplitPackage(item.Name))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DiagnosticsPackage SplitPackage(string value)
    {
        int separator = value.LastIndexOf('/');
        return separator > 0 ? new(value[..separator], value[(separator + 1)..]) : new(value, "unknown");
    }
}
