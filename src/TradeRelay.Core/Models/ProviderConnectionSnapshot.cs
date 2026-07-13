namespace TradeRelay.Core.Models;

/// <summary>Represents the current non-secret exchange connection state.</summary>
public sealed record ProviderConnectionSnapshot(
    string ProviderName,
    TradingEnvironment Environment,
    ServiceHealthState RestHealth,
    ServiceHealthState StreamHealth,
    bool CredentialLoaded,
    string CredentialSummary,
    string? SavedKeyPreview,
    ApiCredentialInfo? CredentialInfo,
    int OpenPositionCount,
    int OpenOrderCount,
    string? LastError,
    Guid ConnectionGenerationId);
