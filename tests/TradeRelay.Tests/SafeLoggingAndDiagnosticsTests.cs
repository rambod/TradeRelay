using System.Text.Json;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class SafeLoggingAndDiagnosticsTests
{
    [Fact]
    public async Task SafeLog_RedactsSecretsAndStoresExceptionTypeOnly()
    {
        string root = CreateRoot();
        try
        {
            var redactor = new SensitiveDataRedactor();
            var log = new SafeLogService(new ApplicationDataPaths(root), new FixedTimeProvider(new DateTimeOffset(2026, 7, 13, 1, 2, 3, TimeSpan.Zero)), redactor);
            var exception = new InvalidOperationException("sentinel-exception-message");

            Assert.True(await log.TryWriteAsync(
                SafeLogLevel.Error,
                "provider_failed",
                "provider.connection",
                "authorization=sentinel-token failed safely",
                new Dictionary<string, string> { ["symbol"] = "BTCUSDT", ["apiKey"] = "sentinel-key" },
                exception));

            string text = await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(root, "logs")).Single());
            Assert.DoesNotContain("sentinel-token", text, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-key", text, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-exception-message", text, StringComparison.Ordinal);
            Assert.Contains(typeof(InvalidOperationException).FullName!, text, StringComparison.Ordinal);
            Assert.Single(log.GetRecentErrors());
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task SafeLog_SerializesConcurrentWritesAndRetainsSevenFiles()
    {
        string root = CreateRoot();
        try
        {
            var paths = new ApplicationDataPaths(root);
            Directory.CreateDirectory(paths.LogsDirectory);
            for (int index = 1; index <= 8; index++)
            {
                await File.WriteAllTextAsync(Path.Combine(paths.LogsDirectory, $"traderelay-2026-06-{index:00}.log"), "old");
            }

            var log = new SafeLogService(paths, TimeProvider.System, new SensitiveDataRedactor());
            await log.StartAsync(default);
            Assert.Equal(7, Directory.GetFiles(paths.LogsDirectory).Length);

            await Task.WhenAll(Enumerable.Range(0, 25).Select(index =>
                log.TryWriteAsync(SafeLogLevel.Information, $"event_{index}", "test", "Safe event.")));
            string current = Directory.GetFiles(paths.LogsDirectory, $"traderelay-{DateTime.UtcNow:yyyy-MM-dd}.log").Single();
            Assert.Equal(25, (await File.ReadAllLinesAsync(current)).Length);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task DiagnosticsExport_IsAtomicDeterministicAndExcludesSensitiveValues()
    {
        string token;
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 13, 4, 5, 6, TimeSpan.Zero));
        await using TestServerContext context = TestServerContext.Create(timeProvider: time);
        token = context.TokenService.CurrentToken;
        await context.SafeLog.TryWriteAsync(SafeLogLevel.Warning, "TEST_WARNING", "test", "secret=sentinel-secret was removed.");
        var exporter = new DiagnosticsExporter(
            context.Settings,
            context.Paths,
            context.Metadata,
            context.Host,
            context.TokenService,
            context.ConnectionManager,
            context.AuditLog,
            context.SafeLog,
            context.Redactor,
            time);

        DiagnosticsExportResult result = await exporter.ExportAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.FilePath);
        Assert.EndsWith("traderelay-diagnostics-20260713T040506000Z.json", result.FilePath, StringComparison.Ordinal);
        Assert.False(File.Exists(result.FilePath + ".tmp"));
        string json = await File.ReadAllTextAsync(result.FilePath!);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("auditEvents", json, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(context.Metadata.Version, document.RootElement.GetProperty("applicationVersion").GetString());
    }

    [Fact]
    public async Task PackageExtractionReadsPackageNamesAndVersionsOnly()
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, """
                {"libraries":{"Avalonia/12.1.0":{"type":"package"},"TradeRelay/1.0.0":{"type":"project"}}}
                """);
            DiagnosticsPackage package = Assert.Single(DiagnosticsExporter.LoadPackages(file));
            Assert.Equal("Avalonia", package.Name);
            Assert.Equal("12.1.0", package.Version);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task DiagnosticsFileFailureReturnsSafeResultAndDoesNotThrow()
    {
        await using TestServerContext context = TestServerContext.Create();
        Directory.CreateDirectory(context.Paths.Root);
        await File.WriteAllTextAsync(context.Paths.DiagnosticsDirectory, "not a directory");
        var exporter = new DiagnosticsExporter(
            context.Settings,
            context.Paths,
            context.Metadata,
            context.Host,
            context.TokenService,
            context.ConnectionManager,
            context.AuditLog,
            context.SafeLog,
            context.Redactor,
            context.TimeProvider);

        DiagnosticsExportResult result = await exporter.ExportAsync();

        Assert.False(result.Success);
        Assert.Equal("DIAGNOSTICS_EXPORT_FAILED", result.Code);
        Assert.DoesNotContain("IOException", result.Message, StringComparison.Ordinal);
    }

    private static string CreateRoot() => Path.Combine(Path.GetTempPath(), "TradeRelay.ProductionTests", Guid.NewGuid().ToString("N"));
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}
