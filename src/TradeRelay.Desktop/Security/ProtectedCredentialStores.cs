using TradeRelay.Core.Models;
using TradeRelay.Core.Security;

namespace TradeRelay.Desktop.Security;

internal abstract class ProtectedCredentialStore(IProtectedSecretStore store) : ICredentialStore
{
    public bool CanPersist => store.CanPersist;
    public async Task SaveAsync(string id, ExchangeCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        await store.SaveAsync(id + ":key", credentials.ApiKey, cancellationToken).ConfigureAwait(false);
        try { await store.SaveAsync(id + ":secret", credentials.ApiSecret, cancellationToken).ConfigureAwait(false); }
        catch { await store.DeleteAsync(id + ":key", CancellationToken.None).ConfigureAwait(false); throw; }
    }
    public async Task<ExchangeCredentials?> LoadAsync(string id, CancellationToken cancellationToken)
    {
        string? key = await store.LoadAsync(id + ":key", cancellationToken).ConfigureAwait(false);
        string? secret = await store.LoadAsync(id + ":secret", cancellationToken).ConfigureAwait(false);
        return key is null || secret is null ? null : new ExchangeCredentials(key, secret);
    }
    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id + ":key", cancellationToken).ConfigureAwait(false);
        await store.DeleteAsync(id + ":secret", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class MacOsKeychainCredentialStore(MacOsKeychainSecretStore store) : ProtectedCredentialStore(store);
internal sealed class WindowsProtectedCredentialStore(WindowsProtectedSecretStore store) : ProtectedCredentialStore(store);
internal sealed class LinuxSecretServiceCredentialStore(LinuxSecretServiceSecretStore store) : ProtectedCredentialStore(store);
