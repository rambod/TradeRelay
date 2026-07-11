using System.Net;
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
            tokenService);
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
        var middleware = new McpSecurityMiddleware(_ => Task.CompletedTask, tokenService);
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
            tokenService);
        DefaultHttpContext context = CreateContext(IPAddress.Loopback, authorization, rawAuthorization: true);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate);
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
            tokenService);
        DefaultHttpContext context = CreateContext(IPAddress.Loopback, tokenService.CurrentToken);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(
        IPAddress? remoteAddress,
        string? authorization,
        bool rawAuthorization = false)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Response.Body = new MemoryStream();

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = rawAuthorization
                ? authorization
                : $"Bearer {authorization}";
        }

        return context;
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
