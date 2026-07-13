namespace TradeRelay.Core.Models;

/// <summary>Identifies an exchange adapter independently of its SDK.</summary>
public readonly record struct ExchangeId
{
    /// <summary>Initializes an exchange identifier.</summary>
    public ExchangeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("An exchange identifier is required.", nameof(value));
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Exchange identifiers may contain only letters, numbers, hyphens, and underscores.", nameof(value));
        Value = normalized;
    }

    /// <summary>Gets the normalized identifier.</summary>
    public string Value { get; }
    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies one provider and environment profile.</summary>
public readonly record struct ExchangeProfileKey(ExchangeId Exchange, TradingEnvironment Environment)
{
    /// <summary>Gets the stable protected-storage identifier.</summary>
    public string CredentialId => $"{Exchange.Value}:{Environment.ToString().ToLowerInvariant()}";
    /// <inheritdoc />
    public override string ToString() => CredentialId;
}

/// <summary>Describes normalized capabilities advertised by an adapter.</summary>
[Flags]
public enum ProviderCapabilities
{
    /// <summary>No capability.</summary>
    None = 0,
    /// <summary>Public market data.</summary>
    MarketData = 1,
    /// <summary>Authenticated account reads.</summary>
    AccountRead = 2,
    /// <summary>Private state streams.</summary>
    PrivateStream = 4,
    /// <summary>Order and execution history.</summary>
    History = 8,
    /// <summary>Exchange writes behind TradeRelay safety gates.</summary>
    TradingWrite = 16,
}

/// <summary>Describes one provider-defined credential input.</summary>
public sealed record CredentialFieldDescriptor(string Name, string Label, bool IsSecret, bool IsRequired = true);

/// <summary>Describes an exchange adapter without exposing SDK types.</summary>
public sealed record ExchangeProviderDescriptor(
    ExchangeId Id,
    string DisplayName,
    ProviderCapabilities Capabilities,
    IReadOnlyList<TradingEnvironment> Environments,
    IReadOnlyList<CredentialFieldDescriptor> CredentialFields);

/// <summary>Provides registered exchange adapters.</summary>
public interface IExchangeProviderRegistry
{
    /// <summary>Gets all registered descriptors in display order.</summary>
    IReadOnlyList<ExchangeProviderDescriptor> Descriptors { get; }
    /// <summary>Resolves a provider factory.</summary>
    bool TryGetFactory(ExchangeId exchange, out Providers.IExchangeProviderFactory? factory);
}
