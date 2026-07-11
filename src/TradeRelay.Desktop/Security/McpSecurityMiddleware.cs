using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Security;

internal sealed class McpSecurityMiddleware(
    RequestDelegate next,
    LocalMcpTokenService tokenService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!TryGetBearerToken(context.Request.Headers.Authorization, out string? bearerToken) ||
            !tokenService.IsValid(bearerToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool TryGetBearerToken(StringValues values, out string? token)
    {
        token = null;

        if (values.Count != 1 ||
            !AuthenticationHeaderValue.TryParse(values[0], out AuthenticationHeaderValue? header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return false;
        }

        token = header.Parameter;
        return true;
    }
}
