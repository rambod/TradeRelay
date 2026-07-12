using CryptoExchange.Net.Objects;
using TradeRelay.Core.Models;

namespace TradeRelay.Providers.Bybit;

internal static class BybitResult
{
    public static T Require<T>(HttpResult<T> result) where T : notnull
    {
        if (result.Success && result.Data is not null) return result.Data;
        string? providerCode = result.Error?.Code?.ToString();
        string code = providerCode switch
        {
            "10003" or "10004" or "10005" or "10007" or "10010" => "CREDENTIALS_INVALID",
            "10006" => "RATE_LIMITED",
            _ => "PROVIDER_UNAVAILABLE"
        };
        string message = code switch
        {
            "CREDENTIALS_INVALID" => "Bybit rejected the API credentials.",
            "RATE_LIMITED" => "Bybit rate-limited the request. Try again later.",
            _ => "Bybit could not complete the request."
        };
        throw new ProviderException(code, message);
    }
}
