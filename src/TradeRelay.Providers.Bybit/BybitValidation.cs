using System.Text.RegularExpressions;
using TradeRelay.Core.Models;

namespace TradeRelay.Providers.Bybit;

internal static partial class BybitValidation
{
    public static string NormalizeSymbol(string symbol)
    {
        string normalized = symbol?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SymbolPattern().IsMatch(normalized) || !normalized.EndsWith("USDT", StringComparison.Ordinal))
            throw new ProviderException("INVALID_INPUT", "Symbol must be a valid USDT perpetual symbol such as BTCUSDT.");
        return normalized;
    }
    [GeneratedRegex("^[A-Z0-9]{5,30}$", RegexOptions.CultureInvariant)] private static partial Regex SymbolPattern();
}
