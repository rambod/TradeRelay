using System.Collections.Concurrent;
using TradeRelay.Core.Models;

namespace TradeRelay.Core.Security;

/// <summary>Stores credentials in memory for the current process only.</summary>
public sealed class SessionCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, ExchangeCredentialSet> _credentials = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool CanPersist => false;

    /// <inheritdoc />
    public Task SaveAsync(string id, ExchangeCredentialSet credentials, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(credentials);
        cancellationToken.ThrowIfCancellationRequested();
        _credentials[id] = credentials;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ExchangeCredentialSet?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        _credentials.TryGetValue(id, out ExchangeCredentialSet? credentials);
        return Task.FromResult(credentials);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        _credentials.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
