using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TradeRelay.Core.Models;

namespace TradeRelay.Desktop.Services;

internal static class OAuthEndpoints
{
    public static void MapTradeRelayOAuth(this WebApplication application, OAuthPairingService pairing)
    {
        application.MapGet("/.well-known/oauth-protected-resource", (HttpRequest request) => Results.Json(new
        {
            resource = Base(request) + "/mcp",
            authorization_servers = new[] { Base(request) },
            scopes_supported = TradeRelayScopes.All.Order(StringComparer.Ordinal).ToArray(),
            bearer_methods_supported = new[] { "header" },
        }));
        application.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) => Results.Json(new
        {
            issuer = Base(request),
            authorization_endpoint = Base(request) + "/oauth/authorize",
            token_endpoint = Base(request) + "/oauth/token",
            registration_endpoint = Base(request) + "/oauth/register",
            revocation_endpoint = Base(request) + "/oauth/revoke",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none" },
            scopes_supported = TradeRelayScopes.All.Order(StringComparer.Ordinal).ToArray(),
        }));
        application.MapPost("/oauth/register", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            ClientRegistration? registration = await request.ReadFromJsonAsync<ClientRegistration>(cancellationToken).ConfigureAwait(false);
            if (registration?.RedirectUris is not { Length: > 0 }) return Results.BadRequest(new { error = "invalid_client_metadata" });
            try
            {
                OAuthClientSnapshot client = pairing.RegisterClient(registration.ClientName ?? "Local MCP client", registration.RedirectUris);
                return Results.Json(new { client_id = client.ClientId, client_name = client.ClientName, redirect_uris = client.RedirectUris, token_endpoint_auth_method = "none" }, statusCode: StatusCodes.Status201Created);
            }
            catch (ArgumentException) { return Results.BadRequest(new { error = "invalid_redirect_uri" }); }
        });
        application.MapGet("/oauth/authorize", (HttpRequest request) =>
        {
            if (!string.Equals(request.Query["response_type"], "code", StringComparison.Ordinal)) return Results.BadRequest(new { error = "unsupported_response_type" });
            try
            {
                OAuthPairingSnapshot pending = pairing.BeginAuthorization(request.Query["client_id"].ToString(), request.Query["redirect_uri"].ToString(), request.Query["state"].ToString(), request.Query["code_challenge"].ToString(), request.Query["code_challenge_method"].ToString(), request.Query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                string id = pending.PairingId.ToString("D");
                string html = $$"""
                    <!doctype html><html><head><meta charset="utf-8"><title>TradeRelay pairing</title><style>body{font:16px system-ui;background:#0b1118;color:#f2f6f8;display:grid;place-items:center;min-height:100vh}main{max-width:520px;padding:28px;border:1px solid #334454;border-radius:12px;background:#16212c}code{color:#35c9e8}</style></head><body><main><h1>Approve in TradeRelay</h1><p>Review <strong>{{HtmlEncoder.Default.Encode(pending.ClientName)}}</strong> in Connections. This request expires in five minutes.</p><p>Pairing ID: <code>{{id}}</code></p><p id="status">Waiting for desktop approval…</p></main><script>setInterval(async()=>{const r=await fetch('/oauth/pairing/{{id}}');const j=await r.json();if(j.redirect)location.href=j.redirect;else if(j.state!=='Pending')document.getElementById('status').textContent=j.message||j.state;},1000);</script></body></html>
                    """;
                return Results.Content(html, "text/html; charset=utf-8");
            }
            catch (InvalidOperationException exception) { return Results.BadRequest(new { error = "invalid_request", error_description = exception.Message }); }
        });
        application.MapGet("/oauth/pairing/{pairingId:guid}", (Guid pairingId) =>
        {
            string? redirect = pairing.GetAuthorizationRedirect(pairingId);
            OAuthPairingSnapshot? snapshot = pairing.GetPairing(pairingId);
            return snapshot is null
                ? Results.NotFound(new { state = "NotFound", message = "The pairing request was not found." })
                : Results.Json(new { state = snapshot.State.ToString(), redirect, message = redirect is null ? snapshot.SafeError ?? PairingMessage(snapshot.State) : null });
        });
        application.MapPost("/oauth/token", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            OAuthTokenResult result = form["grant_type"].ToString() switch
            {
                "authorization_code" => await pairing.ExchangeCodeAsync(form["code"].ToString(), form["client_id"].ToString(), form["redirect_uri"].ToString(), form["code_verifier"].ToString(), cancellationToken).ConfigureAwait(false),
                "refresh_token" => await pairing.RefreshAsync(form["refresh_token"].ToString(), form["scope"].ToString(), cancellationToken).ConfigureAwait(false),
                _ => new OAuthTokenResult(false, "unsupported_grant_type", null, null, 0, string.Empty, "Supported grants are authorization_code and refresh_token."),
            };
            return result.Success
                ? Results.Json(new { access_token = result.AccessToken, token_type = "Bearer", expires_in = result.ExpiresIn, refresh_token = result.RefreshToken, scope = result.Scope })
                : Results.Json(new { error = result.Code, error_description = result.Error }, statusCode: StatusCodes.Status400BadRequest);
        }).DisableAntiforgery();
        application.MapPost("/oauth/revoke", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            await pairing.RevokeClientAsync(form["client_id"].ToString(), cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        }).DisableAntiforgery();
    }

    private static string Base(HttpRequest request) => $"{request.Scheme}://{request.Host}";
    private static string PairingMessage(OAuthPairingState state) => state switch
    {
        OAuthPairingState.Pending => "Waiting for desktop approval.",
        OAuthPairingState.Expired => "The pairing request expired. Start pairing again.",
        OAuthPairingState.Completed => "Pairing completed.",
        _ => $"Pairing is {state.ToString().ToLowerInvariant()}.",
    };
    private sealed record ClientRegistration(
        [property: JsonPropertyName("client_name")] string? ClientName,
        [property: JsonPropertyName("redirect_uris")] string[] RedirectUris);
}
