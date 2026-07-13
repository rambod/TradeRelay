using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class OAuthEndpointIntegrationTests
{
    [Fact]
    public async Task DiscoveryRegistrationPairingTokenAndReadScopeWorkOverRealLoopbackKestrel()
    {
        await using TestServerContext context = TestServerContext.Create();
        await context.Host.StartServerAsync();
        Uri mcp = new(context.Host.Snapshot.Url);
        string origin = $"{mcp.Scheme}://{mcp.Authority}";
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        using JsonDocument protectedMetadata = JsonDocument.Parse(await http.GetStringAsync(origin + "/.well-known/oauth-protected-resource"));
        Assert.Equal(origin + "/mcp", protectedMetadata.RootElement.GetProperty("resource").GetString());
        using HttpResponseMessage registration = await http.PostAsync(origin + "/oauth/register", Json(new { client_name = "Integration Client", redirect_uris = new[] { "http://127.0.0.1:49152/callback" } }));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using JsonDocument registered = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
        string clientId = registered.RootElement.GetProperty("client_id").GetString()!;
        (string verifier, string challenge) = Pkce();
        string authorize = origin + "/oauth/authorize?response_type=code&client_id=" + Uri.EscapeDataString(clientId) + "&redirect_uri=" + Uri.EscapeDataString("http://127.0.0.1:49152/callback") + "&state=integration-state&code_challenge=" + challenge + "&code_challenge_method=S256&scope=traderelay.read";
        using HttpResponseMessage authorization = await http.GetAsync(authorize);
        Assert.Equal(HttpStatusCode.OK, authorization.StatusCode);
        OAuthPairingSnapshot pending = Assert.Single(context.OAuth.GetPendingPairings());
        Assert.True(context.OAuth.Approve(pending.PairingId, grantTrade: false));
        using JsonDocument pairing = JsonDocument.Parse(await http.GetStringAsync(origin + "/oauth/pairing/" + pending.PairingId));
        string redirect = pairing.RootElement.GetProperty("redirect").GetString()!;
        string code = QueryValue(redirect, "code");
        Assert.Equal("integration-state", QueryValue(redirect, "state"));

        using HttpResponseMessage tokenResponse = await http.PostAsync(origin + "/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["redirect_uri"] = "http://127.0.0.1:49152/callback",
            ["code_verifier"] = verifier,
        }));
        tokenResponse.EnsureSuccessStatusCode();
        using JsonDocument token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        string accessToken = token.RootElement.GetProperty("access_token").GetString()!;

        await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = mcp,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },
        }));
        McpClientTool status = (await client.ListToolsAsync()).Single(tool => tool.Name == "get_system_status");
        Assert.NotEqual(true, (await status.CallAsync()).IsError);
        McpClientTool prepare = (await client.ListToolsAsync()).Single(tool => tool.Name == "prepare_order");
        await Assert.ThrowsAnyAsync<Exception>(async () => await prepare.CallAsync(new Dictionary<string, object?> { ["clientRequestId"] = "scope-check", ["symbol"] = "BTCUSDT", ["side"] = "Buy", ["orderType"] = "Market", ["quantity"] = 1m }));
    }

    private static StringContent Json<T>(T value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    private static (string Verifier, string Challenge) Pkce() { string verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_'); return (verifier, Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_')); }
    private static string QueryValue(string uri, string key) => Uri.UnescapeDataString(uri.Split('?', 2)[1].Split('&').Select(item => item.Split('=', 2)).Single(item => item[0] == key)[1]);
}
