using TradeRelay.Core.Models;
using TradeRelay.Core.Security;
using System.Text.Json;

namespace TradeRelay.Desktop.Security;

internal abstract class ProtectedCredentialStore(IProtectedSecretStore store) : ICredentialStore
{
    public bool CanPersist => store.CanPersist;
    public async Task SaveAsync(string id, ExchangeCredentialSet credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        string protectedPayload = JsonSerializer.Serialize(credentials.CopySecretFields());
        await store.SaveAsync(id + ":credentials", protectedPayload, cancellationToken).ConfigureAwait(false);
    }
    public async Task<ExchangeCredentialSet?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        string? payload = await store.LoadAsync(id + ":credentials", cancellationToken).ConfigureAwait(false);
        if (payload is not null)
        {
            Dictionary<string, string>? fields = JsonSerializer.Deserialize<Dictionary<string, string>>(payload);
            return fields is null || fields.Count == 0 ? null : new ExchangeCredentialSet(fields);
        }

        // Preserve the Milestone 3 identifiers and migrate their two protected items lazily.
        string? key = await store.LoadAsync(id + ":key", cancellationToken).ConfigureAwait(false);
        string? secret = await store.LoadAsync(id + ":secret", cancellationToken).ConfigureAwait(false);
        if (key is null || secret is null) return null;
        var credentials = new ExchangeCredentials(key, secret);
        await SaveAsync(id, credentials, cancellationToken).ConfigureAwait(false);
        return credentials;
    }
    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id + ":key", cancellationToken).ConfigureAwait(false);
        await store.DeleteAsync(id + ":secret", cancellationToken).ConfigureAwait(false);
        await store.DeleteAsync(id + ":credentials", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class MacOsKeychainCredentialStore(MacOsKeychainSecretStore store) : ProtectedCredentialStore(store);
internal sealed class WindowsProtectedCredentialStore(WindowsProtectedSecretStore store) : ProtectedCredentialStore(store);
internal sealed class LinuxSecretServiceCredentialStore(LinuxSecretServiceSecretStore store) : ProtectedCredentialStore(store);
