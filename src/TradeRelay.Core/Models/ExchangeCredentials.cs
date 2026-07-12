using System.Text.Json.Serialization;

namespace TradeRelay.Core.Models;

/// <summary>Contains exchange credentials that must never be serialized or logged.</summary>
public sealed class ExchangeCredentials
{
    /// <summary>Initializes exchange credentials.</summary>
    /// <param name="apiKey">The exchange API key.</param>
    /// <param name="apiSecret">The exchange API secret.</param>
    [JsonConstructor]
    private ExchangeCredentials() => throw new NotSupportedException("Exchange credentials cannot be deserialized.");

    /// <summary>Initializes exchange credentials.</summary>
    /// <param name="apiKey">The exchange API key.</param>
    /// <param name="apiSecret">The exchange API secret.</param>
    public ExchangeCredentials(string apiKey, string apiSecret)
    {
        ApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("An API key is required.", nameof(apiKey))
            : apiKey.Trim();
        ApiSecret = string.IsNullOrWhiteSpace(apiSecret)
            ? throw new ArgumentException("An API secret is required.", nameof(apiSecret))
            : apiSecret.Trim();
    }

    /// <summary>Gets the API key.</summary>
    [JsonIgnore]
    public string ApiKey { get; } = string.Empty;

    /// <summary>Gets the API secret.</summary>
    [JsonIgnore]
    public string ApiSecret { get; } = string.Empty;

    /// <inheritdoc />
    public override string ToString() => "ExchangeCredentials { Redacted = true }";
}
