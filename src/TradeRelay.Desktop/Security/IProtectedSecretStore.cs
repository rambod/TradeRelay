namespace TradeRelay.Desktop.Security;

internal interface IProtectedSecretStore
{
    bool CanPersist { get; }
    Task SaveAsync(string id, string value, CancellationToken cancellationToken);
    Task<string?> LoadAsync(string id, CancellationToken cancellationToken);
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}
