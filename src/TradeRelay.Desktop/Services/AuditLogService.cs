using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using TradeRelay.Core.Models;

namespace TradeRelay.Desktop.Services;

internal sealed class AuditLogService(ApplicationDataPaths paths, TimeProvider timeProvider, SensitiveDataRedactor redactor) : IHostedService
{
    private const int SessionLimit = 500;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _eventsLock = new();
    private readonly List<AuditEvent> _sessionEvents = [];
    private readonly List<TradingLifecycleEvent> _sessionLifecycle = [];
    private AuditHealthSnapshot _health = new(true, null, timeProvider.GetUtcNow());

    public event EventHandler<AuditEvent>? EventWritten;
    public event EventHandler<TradingLifecycleEvent>? LifecycleWritten;
    public event EventHandler<AuditHealthSnapshot>? HealthChanged;
    public AuditHealthSnapshot Health => Volatile.Read(ref _health);
    public string DirectoryPath => paths.AuditDirectory;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WriteRequiredAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (!Health.Healthy) throw new ProviderException("AUDIT_UNAVAILABLE", "The activity audit is unavailable; new trading actions are blocked.");
        if (!await TryWriteAsync(auditEvent, cancellationToken).ConfigureAwait(false)) throw new ProviderException("AUDIT_UNAVAILABLE", "The activity audit could not be written; new trading actions are blocked.");
    }

    public async Task<bool> TryWriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        auditEvent = Sanitize(auditEvent);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.AuditDirectory);
            string file = Path.Combine(paths.AuditDirectory, $"audit-{auditEvent.TimestampUtc.UtcDateTime:yyyy-MM-dd}.jsonl");
            string json = JsonSerializer.Serialize(auditEvent) + Environment.NewLine;
            await File.AppendAllTextAsync(file, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            SetHealth(new(true, null, timeProvider.GetUtcNow()));
            lock (_eventsLock)
            {
                _sessionEvents.Add(auditEvent);
                if (_sessionEvents.Count > SessionLimit) _sessionEvents.RemoveRange(0, _sessionEvents.Count - SessionLimit);
            }
            try { EventWritten?.Invoke(this, auditEvent); } catch { }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            SetHealth(new(false, "Activity audit storage is unavailable.", timeProvider.GetUtcNow()));
            return false;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<bool> TryWriteLifecycleAsync(TradingLifecycleEvent lifecycleEvent, CancellationToken cancellationToken)
    {
        lifecycleEvent = Sanitize(lifecycleEvent);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AppendJsonLineAsync(lifecycleEvent.TimestampUtc, lifecycleEvent, cancellationToken).ConfigureAwait(false);
            SetHealth(new(true, null, timeProvider.GetUtcNow()));
            lock (_eventsLock)
            {
                _sessionLifecycle.Add(lifecycleEvent);
                if (_sessionLifecycle.Count > SessionLimit) _sessionLifecycle.RemoveRange(0, _sessionLifecycle.Count - SessionLimit);
            }
            try { LifecycleWritten?.Invoke(this, lifecycleEvent); } catch { }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            SetHealth(new(false, "Activity audit storage is unavailable.", timeProvider.GetUtcNow()));
            return false;
        }
        finally { _writeLock.Release(); }
    }

    public async Task<(IReadOnlyList<AuditEvent> Events, string? Warning)> LoadRecentAsync(CancellationToken cancellationToken)
    {
        var events = new List<AuditEvent>();
        bool malformed = false;
        try
        {
            if (Directory.Exists(paths.AuditDirectory))
            {
                foreach (string file in Directory.GetFiles(paths.AuditDirectory, "audit-*.jsonl").OrderDescending().Take(7))
                {
                    foreach (string line in await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false))
                    {
                        if (events.Count >= 1000) break;
                        try { if (ParseLine(line) is { } item) events.Add(item); else malformed = true; }
                        catch (JsonException) { malformed = true; }
                    }
                }
            }
            lock (_eventsLock) events.AddRange(_sessionEvents);
            return (events.DistinctBy(item => item.EventId).OrderByDescending(item => item.TimestampUtc).Take(1000).ToArray(), malformed ? "Some malformed activity entries were skipped." : null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (events, "Recent activity files could not be loaded.");
        }
    }

    public async Task<AuditHistoryPage> QueryAsync(DateOnly? fromUtc, DateOnly? toUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        int skip = (page - 1) * pageSize;
        var matches = new List<AuditEvent>(pageSize + 1);
        bool malformed = false;
        try
        {
            if (Directory.Exists(paths.AuditDirectory))
            {
                foreach (string file in Directory.GetFiles(paths.AuditDirectory, "audit-*.jsonl").OrderDescending())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryGetFileDate(file, out DateOnly date) || fromUtc is not null && date < fromUtc || toUtc is not null && date > toUtc) continue;
                    foreach (string line in File.ReadLines(file).Reverse())
                    {
                        AuditEvent? item;
                        try { item = ParseLine(line); } catch (JsonException) { malformed = true; continue; }
                        if (item is null) { malformed = true; continue; }
                        if (skip > 0) { skip--; continue; }
                        matches.Add(item);
                        if (matches.Count > pageSize) break;
                    }
                    if (matches.Count > pageSize) break;
                }
            }
            return new AuditHistoryPage(matches.Take(pageSize).ToArray(), page, pageSize, matches.Count > pageSize, malformed ? "Some malformed activity entries were skipped." : null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AuditHistoryPage(matches.Take(pageSize).ToArray(), page, pageSize, false, "Historical activity could not be loaded.");
        }
    }

    public async Task<bool> PurgeAsync(DateOnly? fromUtc, DateOnly? toUtc, string confirmation, CancellationToken cancellationToken)
    {
        if (!string.Equals(confirmation, "DELETE AUDIT HISTORY", StringComparison.Ordinal)) return false;
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(paths.AuditDirectory))
            {
                foreach (string file in Directory.GetFiles(paths.AuditDirectory, "audit-*.jsonl"))
                {
                    if (!TryGetFileDate(file, out DateOnly date) || fromUtc is not null && date < fromUtc || toUtc is not null && date > toUtc) continue;
                    File.Delete(file);
                }
            }
            lock (_eventsLock) { _sessionEvents.Clear(); _sessionLifecycle.Clear(); }
        }
        catch
        {
            SetHealth(new(false, "Activity audit storage is unavailable.", timeProvider.GetUtcNow()));
            return false;
        }
        finally { _writeLock.Release(); }

        return await TryWriteAsync(Create("desktop", "audit_history_purged", "OK", TradingEnvironment.Demo, Guid.NewGuid().ToString("N"), providerResult: fromUtc is null && toUtc is null ? "All retained history" : $"{fromUtc?.ToString() ?? "start"} to {toUtc?.ToString() ?? "end"}"), cancellationToken).ConfigureAwait(false);
    }

    public AuditEvent Create(string tool, string action, string result, TradingEnvironment environment, string correlationId, string? symbol = null, Guid? preparationId = null, string? clientOrderId = null, string? exchangeOrderId = null, string? approvalState = null, string? riskSummary = null, string? providerResult = null, string? finalStatus = null, string? errorCode = null) =>
        new(Guid.NewGuid(), correlationId, timeProvider.GetUtcNow(), environment, tool, action, result, symbol, preparationId, clientOrderId, exchangeOrderId, approvalState, riskSummary, providerResult, finalStatus, errorCode);

    private void SetHealth(AuditHealthSnapshot health) { Volatile.Write(ref _health, health); HealthChanged?.Invoke(this, health); }

    private AuditEvent Sanitize(AuditEvent item) => item with
    {
        ProviderResult = redactor.Redact(item.ProviderResult),
        RiskSummary = redactor.Redact(item.RiskSummary),
        ErrorCode = redactor.Redact(item.ErrorCode)
    };

    private TradingLifecycleEvent Sanitize(TradingLifecycleEvent item) => item with
    {
        ErrorCode = redactor.Redact(item.ErrorCode),
        State = redactor.Redact(item.State),
    };

    private async Task AppendJsonLineAsync<T>(DateTimeOffset timestamp, T item, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.AuditDirectory);
        string file = Path.Combine(paths.AuditDirectory, $"audit-{timestamp.UtcDateTime:yyyy-MM-dd}.jsonl");
        string json = JsonSerializer.Serialize(item) + Environment.NewLine;
        await File.AppendAllTextAsync(file, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static AuditEvent? ParseLine(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        if (document.RootElement.TryGetProperty("Kind", out _) || document.RootElement.TryGetProperty("kind", out _))
        {
            TradingLifecycleEvent? lifecycle = JsonSerializer.Deserialize<TradingLifecycleEvent>(line);
            return lifecycle is null ? null : new AuditEvent(
                lifecycle.EventId,
                lifecycle.CorrelationId,
                lifecycle.TimestampUtc,
                lifecycle.Environment,
                lifecycle.Kind.ToString(),
                lifecycle.Action,
                lifecycle.Result,
                lifecycle.Symbol,
                ClientOrderId: lifecycle.ClientOrderId,
                ExchangeOrderId: lifecycle.ExchangeOrderId,
                ProviderResult: lifecycle.State,
                FinalStatus: lifecycle.State,
                ErrorCode: lifecycle.ErrorCode,
                SchemaVersion: lifecycle.SchemaVersion,
                Exchange: lifecycle.Exchange.Value,
                Source: lifecycle.Source);
        }
        return JsonSerializer.Deserialize<AuditEvent>(line);
    }

    private static bool TryGetFileDate(string file, out DateOnly date)
    {
        date = default;
        string name = Path.GetFileNameWithoutExtension(file);
        return name.Length >= 16 && DateOnly.TryParseExact(name[^10..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
    }
}
