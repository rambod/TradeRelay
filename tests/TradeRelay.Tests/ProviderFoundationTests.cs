using System.Text.Json;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class ProviderFoundationTests
{
    [Fact]
    public void ExchangeIdentity_NormalizesProfilesAndCapabilities()
    {
        var exchange = new ExchangeId(" ByBit ");
        var profile = new ExchangeProfileKey(exchange, TradingEnvironment.Live);

        Assert.Equal("bybit", exchange.Value);
        Assert.Equal("bybit:live", profile.CredentialId);
        Assert.True((ProviderCapabilities.MarketData | ProviderCapabilities.TradingWrite).HasFlag(ProviderCapabilities.TradingWrite));
    }

    [Fact]
    public void ProviderCredentialSet_SupportsPassphrasesWithoutSerializationOrStringLeaks()
    {
        var credentials = new ExchangeCredentialSet(new Dictionary<string, string>
        {
            ["apiKey"] = "sentinel-key",
            ["apiSecret"] = "sentinel-secret",
            ["passphrase"] = "sentinel-passphrase",
        });

        string json = JsonSerializer.Serialize(credentials);
        string display = credentials.ToString();

        Assert.Equal("sentinel-passphrase", credentials["passphrase"]);
        Assert.DoesNotContain("sentinel", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", display, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_RememberEnvironmentIndependentlyPerProvider()
    {
        var settings = new TradeRelay.Core.Settings.AppSettings();
        TradeRelay.Core.Settings.ExchangeProviderSettings bybit = settings.GetExchange(new ExchangeId("bybit"));
        TradeRelay.Core.Settings.ExchangeProviderSettings binance = settings.GetExchange(new ExchangeId("binance"));

        bybit.Environment = TradingEnvironment.Live;
        bybit.SetRemember(TradingEnvironment.Live, true);
        bybit.SetRemember(TradingEnvironment.Demo, false);
        binance.Environment = TradingEnvironment.Live;
        binance.SetRemember(TradingEnvironment.Live, false);

        Assert.True(bybit.ShouldRemember(TradingEnvironment.Live));
        Assert.False(bybit.ShouldRemember(TradingEnvironment.Demo));
        Assert.False(binance.ShouldRemember(TradingEnvironment.Live));
    }

    [Fact]
    public void SettingsStore_MigratesLegacyBybitSettingsAndCreatesOneBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), "TradeRelay.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new ApplicationDataPaths(root);
            Directory.CreateDirectory(root);
            File.WriteAllText(paths.SettingsFile, "{\"server\":{\"port\":5050},\"bybit\":{\"environment\":\"Live\",\"rememberCredentials\":true},\"risk\":{}}");
            var store = new ApplicationSettingsStore(paths);

            TradeRelay.Core.Settings.AppSettings migrated = store.Load();
            TradeRelay.Core.Settings.AppSettings loadedAgain = store.Load();

            Assert.Equal(2, migrated.SchemaVersion);
            Assert.Equal(TradingEnvironment.Live, migrated.Bybit.Environment);
            Assert.True(migrated.Bybit.ShouldRemember(TradingEnvironment.Live));
            Assert.Equal(TradingEnvironment.Live, loadedAgain.Bybit.Environment);
            Assert.True(File.Exists(paths.SettingsFile + ".v1.backup"));
            Assert.DoesNotContain("apiSecret", File.ReadAllText(paths.SettingsFile), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
