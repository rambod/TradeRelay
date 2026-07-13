using System.Security.Cryptography;
using System.Text;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Security;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class OAuthPairingTests
{
    [Fact]
    public async Task AuthorizationCode_RequiresExactRedirectPkceAndIsSingleUse()
    {
        TestFixture fixture = Create();
        OAuthClientSnapshot client = fixture.Service.RegisterClient("Codex", ["http://127.0.0.1:4567/callback"]);
        (string verifier, string challenge) = Pkce();

        Assert.Throws<InvalidOperationException>(() => fixture.Service.BeginAuthorization(client.ClientId, "http://127.0.0.1:4568/callback", "state", challenge, "S256", TradeRelayScopes.Default));
        OAuthPairingSnapshot pairing = fixture.Service.BeginAuthorization(client.ClientId, client.RedirectUris[0], "opaque-state", challenge, "S256", TradeRelayScopes.Default);
        Assert.True(fixture.Service.Approve(pairing.PairingId, grantTrade: false));
        string redirect = Assert.IsType<string>(fixture.Service.GetAuthorizationRedirect(pairing.PairingId));
        string code = QueryValue(redirect, "code");
        Assert.Equal("opaque-state", QueryValue(redirect, "state"));

        OAuthTokenResult wrongVerifier = await fixture.Service.ExchangeCodeAsync(code, client.ClientId, client.RedirectUris[0], new string('x', 43), default);
        Assert.False(wrongVerifier.Success);
        OAuthTokenResult replay = await fixture.Service.ExchangeCodeAsync(code, client.ClientId, client.RedirectUris[0], verifier, default);
        Assert.False(replay.Success);

        OAuthPairingSnapshot second = fixture.Service.BeginAuthorization(client.ClientId, client.RedirectUris[0], "second-state", challenge, "S256", TradeRelayScopes.Default);
        Assert.True(fixture.Service.Approve(second.PairingId, grantTrade: false));
        OAuthTokenResult tokens = await fixture.Service.ExchangeCodeAsync(QueryValue(fixture.Service.GetAuthorizationRedirect(second.PairingId)!, "code"), client.ClientId, client.RedirectUris[0], verifier, default);
        Assert.True(tokens.Success);
        Assert.True(fixture.Service.ValidateAccessToken(tokens.AccessToken!, TradeRelayScopes.Read, out _));
        Assert.True(fixture.Service.ValidateAccessToken(tokens.AccessToken!, TradeRelayScopes.Plan, out _));
        Assert.False(fixture.Service.ValidateAccessToken(tokens.AccessToken!, TradeRelayScopes.Trade, out _));
        Assert.DoesNotContain(tokens.RefreshToken!, fixture.Store.Values.Values, StringComparer.Ordinal);
        string metadata = await File.ReadAllTextAsync(fixture.Paths.OAuthClientsFile);
        Assert.DoesNotContain(tokens.AccessToken!, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(tokens.RefreshToken!, metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentRefresh_AllowsExactlyOneRotation()
    {
        TestFixture fixture = Create();
        OAuthClientSnapshot client = fixture.Service.RegisterClient("Concurrent client", ["http://127.0.0.1:9090/callback"]);
        OAuthTokenResult initial = await AuthorizeAsync(fixture.Service, client, grantTrade: false);

        OAuthTokenResult[] results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => fixture.Service.RefreshAsync(initial.RefreshToken!, null, default)));

        Assert.Single(results, result => result.Success);
        Assert.Equal(11, results.Count(result => !result.Success && result.Code == "invalid_grant"));
    }

    [Fact]
    public async Task Refresh_RotatesRevokesAndCannotEscalateScope()
    {
        TestFixture fixture = Create();
        OAuthClientSnapshot client = fixture.Service.RegisterClient("Claude Code", ["http://localhost:7777/oauth/callback"]);
        OAuthTokenResult initial = await AuthorizeAsync(fixture.Service, client, grantTrade: false);

        OAuthTokenResult escalated = await fixture.Service.RefreshAsync(initial.RefreshToken!, TradeRelayScopes.Trade, default);
        Assert.False(escalated.Success);
        OAuthTokenResult rotated = await fixture.Service.RefreshAsync(initial.RefreshToken!, null, default);
        Assert.True(rotated.Success);
        Assert.False((await fixture.Service.RefreshAsync(initial.RefreshToken!, null, default)).Success);
        Assert.True((await fixture.Service.RefreshAsync(rotated.RefreshToken!, null, default)).Success);
        Assert.True(await fixture.Service.RevokeClientAsync(client.ClientId, default));
        Assert.False(fixture.Service.ValidateAccessToken(rotated.AccessToken!, TradeRelayScopes.Read, out _));
    }

    [Fact]
    public async Task PairingCodeAccessAndRefreshExpirationsAreEnforced()
    {
        TestFixture fixture = Create();
        OAuthClientSnapshot client = fixture.Service.RegisterClient("Gemini CLI", ["http://127.0.0.1:8888/callback"]);
        (string verifier, string challenge) = Pkce();
        OAuthPairingSnapshot expiredPairing = fixture.Service.BeginAuthorization(client.ClientId, client.RedirectUris[0], "state", challenge, "S256", TradeRelayScopes.Default);
        fixture.Time.Advance(TimeSpan.FromMinutes(6));
        Assert.False(fixture.Service.Approve(expiredPairing.PairingId, false));

        OAuthPairingSnapshot codePairing = fixture.Service.BeginAuthorization(client.ClientId, client.RedirectUris[0], "state-2", challenge, "S256", TradeRelayScopes.Default);
        Assert.True(fixture.Service.Approve(codePairing.PairingId, false));
        string code = QueryValue(fixture.Service.GetAuthorizationRedirect(codePairing.PairingId)!, "code");
        fixture.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.False((await fixture.Service.ExchangeCodeAsync(code, client.ClientId, client.RedirectUris[0], verifier, default)).Success);

        OAuthTokenResult tokens = await AuthorizeAsync(fixture.Service, client, grantTrade: true);
        Assert.True(fixture.Service.ValidateAccessToken(tokens.AccessToken!, TradeRelayScopes.Trade, out _));
        fixture.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.False(fixture.Service.ValidateAccessToken(tokens.AccessToken!, TradeRelayScopes.Read, out _));
        fixture.Time.Advance(TimeSpan.FromDays(31));
        Assert.False((await fixture.Service.RefreshAsync(tokens.RefreshToken!, null, default)).Success);
    }

    private static async Task<OAuthTokenResult> AuthorizeAsync(OAuthPairingService service, OAuthClientSnapshot client, bool grantTrade)
    {
        (string verifier, string challenge) = Pkce();
        string[] scopes = grantTrade ? [TradeRelayScopes.Read, TradeRelayScopes.Plan, TradeRelayScopes.Trade] : TradeRelayScopes.Default.ToArray();
        OAuthPairingSnapshot pairing = service.BeginAuthorization(client.ClientId, client.RedirectUris[0], Guid.NewGuid().ToString("N"), challenge, "S256", scopes);
        Assert.True(service.Approve(pairing.PairingId, grantTrade));
        string code = QueryValue(service.GetAuthorizationRedirect(pairing.PairingId)!, "code");
        return await service.ExchangeCodeAsync(code, client.ClientId, client.RedirectUris[0], verifier, default);
    }

    private static (string Verifier, string Challenge) Pkce()
    {
        string verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }
    private static string QueryValue(string uri, string key) => Uri.UnescapeDataString(uri.Split('?', 2)[1].Split('&').Select(item => item.Split('=', 2)).Single(item => item[0] == key)[1]);
    private static TestFixture Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "TradeRelay.OAuthTests", Guid.NewGuid().ToString("N"));
        var store = new RecordingProtectedStore();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        var paths = new ApplicationDataPaths(root);
        return new TestFixture(new OAuthPairingService(paths, store, time), store, time, paths);
    }
    private sealed record TestFixture(OAuthPairingService Service, RecordingProtectedStore Store, MutableTimeProvider Time, ApplicationDataPaths Paths);
    private sealed class RecordingProtectedStore : IProtectedSecretStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal); public bool CanPersist => true;
        public Task SaveAsync(string id, string value, CancellationToken cancellationToken) { Values[id] = value; return Task.CompletedTask; }
        public Task<string?> LoadAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Values.GetValueOrDefault(id));
        public Task DeleteAsync(string id, CancellationToken cancellationToken) { Values.Remove(id); return Task.CompletedTask; }
    }
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; public void Advance(TimeSpan amount) => now += amount; }
}
