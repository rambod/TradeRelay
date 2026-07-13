using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using Xunit;

namespace TradeRelay.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_AreDemoFirstAndConservative()
    {
        // Arrange and act
        var settings = new AppSettings();

        // Assert
        Assert.Equal(5050, settings.Server.Port);
        Assert.False(settings.Server.StartAutomatically);
        Assert.Equal(TradingEnvironment.Demo, settings.Bybit.Environment);
        Assert.False(settings.Bybit.RememberCredentials);
        Assert.True(settings.Risk.RequireStopLoss);
        Assert.False(settings.Risk.RequireManualApprovalForDemo);
        Assert.True(settings.Risk.RequireManualApprovalForLive);
    }

    [Fact]
    public void PersistedSettingsModels_DoNotExposeSecretProperties()
    {
        // Arrange
        Type[] settingsTypes =
        [
            typeof(AppSettings),
            typeof(ServerSettings),
            typeof(ExchangeProviderSettings),
            typeof(RiskSettings)
        ];
        string[] forbiddenPropertyNames =
        [
            "ApiKey",
            "ApiSecret",
            "PrivateKey",
            "BearerToken",
            "Authorization",
            "Password"
        ];

        // Act
        string[] propertyNames = settingsTypes
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        // Assert
        foreach (string forbiddenPropertyName in forbiddenPropertyNames)
        {
            Assert.DoesNotContain(
                propertyNames,
                propertyName => string.Equals(
                    propertyName,
                    forbiddenPropertyName,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}
