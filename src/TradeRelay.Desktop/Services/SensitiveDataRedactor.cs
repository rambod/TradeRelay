using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradeRelay.Desktop.Services;

internal sealed partial class SensitiveDataRedactor
{
    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey", "apiSecret", "secret", "token", "authorization", "signature", "password",
        "credentials", "authenticatedPayload", "rawPayload"
    };

    public string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        string redacted = SensitiveAssignmentPattern().Replace(value, "$1=[REDACTED]");
        redacted = BearerPattern().Replace(redacted, "Bearer [REDACTED]");
        return PrivateKeyPattern().Replace(redacted, "[REDACTED PRIVATE KEY]");
    }

    public IReadOnlyDictionary<string, string> RedactProperties(IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0) return new Dictionary<string, string>();

        return properties
            .Where(item => !ForbiddenPropertyNames.Contains(item.Key))
            .ToDictionary(item => item.Key, item => Redact(item.Value) ?? string.Empty, StringComparer.Ordinal);
    }

    public void EnsureSafeJson(string json, IEnumerable<string?> forbiddenValues)
    {
        foreach (string forbidden in forbiddenValues.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!))
        {
            if (forbidden.Length >= 4 && json.Contains(forbidden, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The diagnostics payload failed its sensitive-value scan.");
            }
        }

        using JsonDocument document = JsonDocument.Parse(json);
        ScanElement(document.RootElement);

        if (!string.Equals(Redact(json), json, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The diagnostics payload failed its sensitive-content scan.");
        }
    }

    private static void ScanElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (ForbiddenPropertyNames.Contains(property.Name))
                {
                    throw new InvalidDataException("The diagnostics payload contains a forbidden field.");
                }

                ScanElement(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) ScanElement(item);
        }
    }

    [GeneratedRegex("(?i)\\b(api[-_ ]?key|api[-_ ]?secret|secret|token|authorization|signature|password|private[-_ ]?key|credential(?:s)?)\\s*[:=]\\s*[^\\s;,\\\"}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentPattern();

    [GeneratedRegex("(?i)Bearer\\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex("-----BEGIN [A-Z ]*PRIVATE KEY-----[\\s\\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyPattern();
}
