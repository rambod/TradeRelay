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
    private AuditHealthSnapshot _health = new(true, null, timeProvider.GetUtcNow());

    public event EventHandler<AuditEvent>? EventWritten;
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
                        try { if (JsonSerializer.Deserialize<AuditEvent>(line) is { } item) events.Add(item); else malformed = true; }
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

    public AuditEvent Create(string tool, string action, string result, TradingEnvironment environment, string correlationId, string? symbol = null, Guid? preparationId = null, string? clientOrderId = null, string? exchangeOrderId = null, string? approvalState = null, string? riskSummary = null, string? providerResult = null, string? finalStatus = null, string? errorCode = null) =>
        new(Guid.NewGuid(), correlationId, timeProvider.GetUtcNow(), environment, tool, action, result, symbol, preparationId, clientOrderId, exchangeOrderId, approvalState, riskSummary, providerResult, finalStatus, errorCode);

    private void SetHealth(AuditHealthSnapshot health) { Volatile.Write(ref _health, health); HealthChanged?.Invoke(this, health); }

    private AuditEvent Sanitize(AuditEvent item) => item with
    {
        ProviderResult = redactor.Redact(item.ProviderResult),
        RiskSummary = redactor.Redact(item.RiskSummary),
        ErrorCode = redactor.Redact(item.ErrorCode)
    };
}
