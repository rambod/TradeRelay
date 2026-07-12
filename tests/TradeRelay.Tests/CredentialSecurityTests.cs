using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TradeRelay.Core.Models;
using TradeRelay.Core.Security;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Security;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class CredentialSecurityTests
{
    [Fact]
    public async Task SessionStore_SeparatesLoadsAndDeletesCredentials()
    {
        var store = new SessionCredentialStore();
        var demo = new ExchangeCredentials("demo-key", "demo-secret");
        var live = new ExchangeCredentials("live-key", "live-secret");

        await store.SaveAsync("bybit:demo", demo, default);
        await store.SaveAsync("bybit:live", live, default);
        Assert.Equal("demo-key", (await store.LoadAsync("bybit:demo", default))?.ApiKey);
        Assert.Equal("live-key", (await store.LoadAsync("bybit:live", default))?.ApiKey);

        await store.DeleteAsync("bybit:demo", default);
        Assert.Null(await store.LoadAsync("bybit:demo", default));
        Assert.NotNull(await store.LoadAsync("bybit:live", default));
    }

    [Fact]
    public void Credentials_DoNotSerializeOrRevealValuesInToString()
    {
        var credentials = new ExchangeCredentials("sentinel-key", "sentinel-secret");
        string json = JsonSerializer.Serialize(credentials);
        string text = credentials.ToString();

        Assert.DoesNotContain("sentinel-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-key", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedCredentialStore_UsesSeparateProtectedValuesAndDeletesBoth()
    {
        var secrets = new RecordingSecretStore();
        var store = new TestProtectedCredentialStore(secrets);
        await store.SaveAsync("bybit:demo", new ExchangeCredentials("key", "secret"), default);

        Assert.Equal("key", secrets.Values["bybit:demo:key"]);
        Assert.Equal("secret", secrets.Values["bybit:demo:secret"]);
        ExchangeCredentials? loaded = await store.LoadAsync("bybit:demo", default);
        Assert.Equal("key", loaded?.ApiKey);

        await store.DeleteAsync("bybit:demo", default);
        Assert.Empty(secrets.Values);
    }

    [Fact]
    public async Task McpToken_LoadsPersistsAndRotatesProtectedValue()
    {
        var secrets = new RecordingSecretStore();
        var first = new LocalMcpTokenService(secrets, NullLogger<LocalMcpTokenService>.Instance);
        await first.StartAsync(default);
        string original = first.CurrentToken;
        Assert.Equal(original, secrets.Values["mcp:bearer-token"]);

        string rotated = await first.RotateAsync(default);
        Assert.NotEqual(original, rotated);
        Assert.Equal(rotated, secrets.Values["mcp:bearer-token"]);
        Assert.False(first.IsValid(original));

        var second = new LocalMcpTokenService(secrets, NullLogger<LocalMcpTokenService>.Instance);
        await second.StartAsync(default);
        Assert.Equal(rotated, second.CurrentToken);
    }

    [Fact]
    public async Task SettingsStore_WritesOnlyNonSecretSettingsAtomically()
    {
        string root = Path.Combine(Path.GetTempPath(), "TradeRelay.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new ApplicationDataPaths(root);
            var store = new ApplicationSettingsStore(paths);
            var settings = new AppSettings { Bybit = new BybitSettings { Environment = TradingEnvironment.Live, RememberCredentials = true } };
            await store.SaveAsync(settings, default);
            string json = await File.ReadAllTextAsync(paths.SettingsFile);

            Assert.Contains("Live", json, StringComparison.Ordinal);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apiSecret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bearerToken", json, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(paths.SettingsFile + ".tmp"));
            Assert.Equal(TradingEnvironment.Live, store.Load().Bybit.Environment);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class TestProtectedCredentialStore(IProtectedSecretStore store) : ProtectedCredentialStore(store);

    private sealed class RecordingSecretStore : IProtectedSecretStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public bool CanPersist => true;
        public Task SaveAsync(string id, string value, CancellationToken cancellationToken) { Values[id] = value; return Task.CompletedTask; }
        public Task<string?> LoadAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Values.GetValueOrDefault(id));
        public Task DeleteAsync(string id, CancellationToken cancellationToken) { Values.Remove(id); return Task.CompletedTask; }
    }
}
