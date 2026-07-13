using TradeRelay.Core.Models;
using TradeRelay.Core.Security;

namespace TradeRelay.Desktop.Security;

internal sealed class CredentialStoreCoordinator(SessionCredentialStore sessionStore, ICredentialStore persistentStore)
{
    public bool CanPersist => persistentStore.CanPersist;

    public async Task SaveAsync(string id, ExchangeCredentialSet credentials, bool remember, CancellationToken cancellationToken)
    {
        await sessionStore.SaveAsync(id, credentials, cancellationToken).ConfigureAwait(false);
        if (remember)
        {
            if (!CanPersist) throw new InvalidOperationException("Protected credential storage is unavailable; credentials remain session-only.");
            await persistentStore.SaveAsync(id, credentials, cancellationToken).ConfigureAwait(false);
        }
        else if (CanPersist)
        {
            await persistentStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ExchangeCredentialSet?> LoadAsync(string id, bool remember, CancellationToken cancellationToken)
    {
        ExchangeCredentialSet? current = await sessionStore.LoadAsync(id, cancellationToken).ConfigureAwait(false);
        if (current is not null || !remember || !CanPersist) return current;
        current = await persistentStore.LoadAsync(id, cancellationToken).ConfigureAwait(false);
        if (current is not null) await sessionStore.SaveAsync(id, current, cancellationToken).ConfigureAwait(false);
        return current;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await sessionStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (CanPersist) await persistentStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }
}
