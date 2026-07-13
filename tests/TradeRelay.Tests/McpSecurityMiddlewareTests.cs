using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using TradeRelay.Desktop.Security;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class McpSecurityMiddlewareTests
{
    [Theory]
    [InlineData("10.10.0.4")]
    [InlineData("203.0.113.20")]
    public async Task InvokeAsync_RejectsNonLoopbackAddresses(string address)
    {
        // Arrange
        var tokenService = new LocalMcpTokenService();
        bool nextCalled = false;
        var middleware = new McpSecurityMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            tokenService,
            CreateOAuth());
        DefaultHttpContext context = CreateContext(IPAddress.Parse(address), tokenService.CurrentToken);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_RejectsMissingRemoteAddress()
    {
        // Arrange
        var tokenService = new LocalMcpTokenService();
        var middleware = new McpSecurityMiddleware(_ => Task.CompletedTask, tokenService, CreateOAuth());
        DefaultHttpContext context = CreateContext(null, tokenService.CurrentToken);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer wrong")]
    public async Task InvokeAsync_RejectsMissingOrInvalidBearerToken(string? authorization)
    {
        // Arrange
        var tokenService = new LocalMcpTokenService();
        bool nextCalled = false;
        var middleware = new McpSecurityMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            tokenService,
            CreateOAuth());
        DefaultHttpContext context = CreateContext(IPAddress.Loopback, authorization, rawAuthorization: true);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.StartsWith("Bearer", context.Response.Headers.WWWAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.False(nextCalled);
        Assert.DoesNotContain(tokenService.CurrentToken, ReadBody(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_AllowsValidLoopbackBearerToken()
    {
        // Arrange
        var tokenService = new LocalMcpTokenService();
        bool nextCalled = false;
        var middleware = new McpSecurityMiddleware(
            context =>
            {
                nextCalled = true;
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            tokenService,
            CreateOAuth());
        DefaultHttpContext context = CreateContext(IPAddress.Loopback, tokenService.CurrentToken);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_RequiresHighestScopeInJsonRpcBatch()
    {
        var tokenService = new LocalMcpTokenService();
        OAuthPairingService oauth = CreateOAuth();
        string accessToken = await AuthorizeReadAndPlanAsync(oauth);
        bool nextCalled = false;
        var middleware = new McpSecurityMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, tokenService, oauth);
        DefaultHttpContext context = CreateContext(IPAddress.Loopback, accessToken);
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("[{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"params\":{\"name\":\"get_system_status\"}},{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"params\":{\"name\":\"cancel_all_orders\"}}]"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextCalled);
        Assert.Contains("traderelay.trade", context.Response.Headers.WWWAuthenticate.ToString(), StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateContext(
        IPAddress? remoteAddress,
        string? authorization,
        bool rawAuthorization = false)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/mcp";

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = rawAuthorization
                ? authorization
                : $"Bearer {authorization}";
        }

        return context;
    }

    private static OAuthPairingService CreateOAuth() => new(new ApplicationDataPaths(Path.Combine(Path.GetTempPath(), "TradeRelay.Tests", Guid.NewGuid().ToString("N"))), new MemoryProtectedStore(), TimeProvider.System);

    private static async Task<string> AuthorizeReadAndPlanAsync(OAuthPairingService oauth)
    {
        const string redirectUri = "http://127.0.0.1:9876/callback";
        string verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var client = oauth.RegisterClient("Batch test", [redirectUri]);
        var pairing = oauth.BeginAuthorization(client.ClientId, redirectUri, "state", challenge, "S256", TradeRelay.Core.Models.TradeRelayScopes.Default);
        Assert.True(oauth.Approve(pairing.PairingId, false));
        string code = Uri.UnescapeDataString(oauth.GetAuthorizationRedirect(pairing.PairingId)!.Split("code=", 2)[1].Split('&')[0]);
        OAuthTokenResult result = await oauth.ExchangeCodeAsync(code, client.ClientId, redirectUri, verifier, default);
        return Assert.IsType<string>(result.AccessToken);
    }

    private sealed class MemoryProtectedStore : IProtectedSecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public bool CanPersist => true;
        public Task SaveAsync(string id, string value, CancellationToken cancellationToken) { _values[id] = value; return Task.CompletedTask; }
        public Task<string?> LoadAsync(string id, CancellationToken cancellationToken) => Task.FromResult(_values.GetValueOrDefault(id));
        public Task DeleteAsync(string id, CancellationToken cancellationToken) { _values.Remove(id); return Task.CompletedTask; }
    }

    private static string ReadBody(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            Encoding.UTF8,
            leaveOpen: true);
        return reader.ReadToEnd();
    }
}
