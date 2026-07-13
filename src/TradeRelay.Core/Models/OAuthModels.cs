namespace TradeRelay.Core.Models;

/// <summary>TradeRelay OAuth scopes.</summary>
public static class TradeRelayScopes
{
    /// <summary>Read exchange and runtime state.</summary>
    public const string Read = "traderelay.read";
    /// <summary>Calculate, validate, and prepare immutable plans.</summary>
    public const string Plan = "traderelay.plan";
    /// <summary>Invoke gated exchange write tools.</summary>
    public const string Trade = "traderelay.trade";
    /// <summary>Default scopes for a newly paired client.</summary>
    public static IReadOnlyList<string> Default { get; } = [Read, Plan];
    /// <summary>All recognized scopes.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>([Read, Plan, Trade], StringComparer.Ordinal);
}

/// <summary>Safe OAuth pairing status shown in the desktop.</summary>
public enum OAuthPairingState { Pending, Approved, Rejected, Expired, Completed, Revoked }

/// <summary>A safe pending or completed local OAuth pairing request.</summary>
public sealed record OAuthPairingSnapshot(
    Guid PairingId,
    string ClientId,
    string ClientName,
    string RedirectUri,
    IReadOnlyList<string> RequestedScopes,
    OAuthPairingState State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    string? SafeError = null);

/// <summary>Safe registered-client metadata for the Connections UI.</summary>
public sealed record OAuthClientSnapshot(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> GrantedScopes,
    DateTimeOffset RegisteredUtc,
    DateTimeOffset? LastSeenUtc,
    bool Revoked);
