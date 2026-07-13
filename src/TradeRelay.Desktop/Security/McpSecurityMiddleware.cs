using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using TradeRelay.Desktop.Services;
using TradeRelay.Core.Models;

namespace TradeRelay.Desktop.Security;

internal sealed class McpSecurityMiddleware(
    RequestDelegate next,
    LocalMcpTokenService tokenService,
    OAuthPairingService oauth)
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

        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!TryGetBearerToken(context.Request.Headers.Authorization, out string? bearerToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = Challenge(context);
            return;
        }

        if (!tokenService.IsValid(bearerToken))
        {
            string requiredScope = await RequiredScopeAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            if (!oauth.ValidateAccessToken(bearerToken!, TradeRelayScopes.Read, out OAuthClientSnapshot? client))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = Challenge(context);
                return;
            }
            if (requiredScope != TradeRelayScopes.Read && !oauth.ValidateAccessToken(bearerToken!, requiredScope, out client))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{requiredScope}\"";
                return;
            }
            context.Items["TradeRelay.OAuthClient"] = client;
            context.Items["TradeRelay.Scope"] = requiredScope;
        }

        await next(context).ConfigureAwait(false);
    }

    private static async Task<string> RequiredScopeAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) return TradeRelayScopes.Read;
        request.EnableBuffering();
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                string required = TradeRelayScopes.Read;
                foreach (JsonElement requestElement in document.RootElement.EnumerateArray())
                {
                    string candidate = ScopeForRequest(requestElement);
                    if (candidate == TradeRelayScopes.Trade) return candidate;
                    if (candidate == TradeRelayScopes.Plan) required = candidate;
                }
                return required;
            }
            return ScopeForRequest(document.RootElement);
        }
        catch (JsonException) { return TradeRelayScopes.Read; }
        finally { request.Body.Position = 0; }
    }

    private static string ScopeForRequest(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("method", out JsonElement method) || method.GetString() != "tools/call" || !request.TryGetProperty("params", out JsonElement parameters) || !parameters.TryGetProperty("name", out JsonElement nameElement)) return TradeRelayScopes.Read;
        string? name = nameElement.GetString();
        if (name is "execute_prepared_order" or "cancel_order" or "cancel_all_orders" or "close_position" or "set_trading_stop") return TradeRelayScopes.Trade;
        if (name is "calculate_position_size" or "validate_order" or "prepare_order") return TradeRelayScopes.Plan;
        return TradeRelayScopes.Read;
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

    private static string Challenge(HttpContext context) => context.Request.Host.HasValue
        ? $"Bearer resource_metadata=\"{context.Request.Scheme}://{context.Request.Host}/.well-known/oauth-protected-resource\""
        : "Bearer";
}
