using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class LocalMcpTokenServiceTests
{
    [Fact]
    public void Constructor_GeneratesA32ByteHexToken()
    {
        // Arrange and act
        var service = new LocalMcpTokenService();

        // Assert
        Assert.Equal(LocalMcpTokenService.TokenCharacterLength, service.CurrentToken.Length);
        Assert.Equal(LocalMcpTokenService.TokenByteLength, Convert.FromHexString(service.CurrentToken).Length);
        Assert.NotEqual(new LocalMcpTokenService().CurrentToken, service.CurrentToken);
    }

    [Fact]
    public void Rotate_InvalidatesPreviousTokenImmediately()
    {
        // Arrange
        var service = new LocalMcpTokenService();
        string previous = service.CurrentToken;

        // Act
        string current = service.Rotate();

        // Assert
        Assert.NotEqual(previous, current);
        Assert.False(service.IsValid(previous));
        Assert.True(service.IsValid(current));
        Assert.DoesNotContain(previous, LocalMcpTokenService.MaskedToken, StringComparison.Ordinal);
        Assert.DoesNotContain(current, LocalMcpTokenService.MaskedToken, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("AAAAAAAA")]
    public void IsValid_RejectsMalformedValues(string? candidate)
    {
        // Arrange
        var service = new LocalMcpTokenService();

        // Act and assert
        Assert.False(service.IsValid(candidate));
    }
}
