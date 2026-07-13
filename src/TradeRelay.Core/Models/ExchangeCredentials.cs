using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TradeRelay.Core.Models;

/// <summary>Contains a provider-defined credential set that must never be serialized or logged.</summary>
public class ExchangeCredentialSet
{
    private readonly IReadOnlyDictionary<string, string> _fields;

    /// <summary>Initializes a redacted credential set.</summary>
    public ExchangeCredentialSet(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0) throw new ArgumentException("At least one credential field is required.", nameof(fields));

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in fields)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Credential names and values are required.", nameof(fields));
            copy[name.Trim()] = value.Trim();
        }

        _fields = new ReadOnlyDictionary<string, string>(copy);
    }

    /// <summary>Gets the non-secret field names present in this set.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> FieldNames => _fields.Keys.ToArray();

    /// <summary>Gets a required credential value without exposing it through serialization.</summary>
    [JsonIgnore]
    public string this[string fieldName] => _fields.TryGetValue(fieldName, out string? value)
        ? value
        : throw new KeyNotFoundException($"Credential field '{fieldName}' is missing.");

    /// <summary>Creates a private copy for a protected-storage implementation.</summary>
    public IReadOnlyDictionary<string, string> CopySecretFields() =>
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(_fields, StringComparer.Ordinal));

    /// <inheritdoc />
    public override string ToString() => $"ExchangeCredentialSet {{ Fields = {FieldNames.Count}, Redacted = true }}";
}

/// <summary>Provides the API-key and API-secret shape retained for the Bybit adapter.</summary>
public sealed class ExchangeCredentials : ExchangeCredentialSet
{
    /// <summary>Initializes API-key credentials.</summary>
    public ExchangeCredentials(string apiKey, string apiSecret)
        : base(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ApiKeyField] = apiKey,
            [ApiSecretField] = apiSecret,
        })
    {
    }

    /// <summary>The canonical API-key field name.</summary>
    public const string ApiKeyField = "apiKey";
    /// <summary>The canonical API-secret field name.</summary>
    public const string ApiSecretField = "apiSecret";

    /// <summary>Gets the API key.</summary>
    [JsonIgnore]
    public string ApiKey => this[ApiKeyField];

    /// <summary>Gets the API secret.</summary>
    [JsonIgnore]
    public string ApiSecret => this[ApiSecretField];

    /// <inheritdoc />
    public override string ToString() => "ExchangeCredentials { Redacted = true }";
}
