using TradeRelay.Core.Models;

namespace TradeRelay.Core.Security;

/// <summary>Stores exchange credentials without exposing their values.</summary>
public interface ICredentialStore
{
    /// <summary>Saves credentials under an identifier.</summary>
    Task SaveAsync(string id, ExchangeCredentialSet credentials, CancellationToken cancellationToken);
    /// <summary>Loads credentials when present.</summary>
    Task<ExchangeCredentialSet?> LoadAsync(string id, CancellationToken cancellationToken);
    /// <summary>Deletes credentials.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);
    /// <summary>Gets whether values can survive application restart.</summary>
    bool CanPersist { get; }
}
