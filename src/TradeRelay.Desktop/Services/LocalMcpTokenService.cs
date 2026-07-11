using System.Security.Cryptography;

namespace TradeRelay.Desktop.Services;

internal sealed class LocalMcpTokenService
{
    internal const int TokenByteLength = 32;
    internal const int TokenCharacterLength = TokenByteLength * 2;
    internal const string MaskedToken = "••••••••••••••••";

    private string _currentToken = GenerateToken();

    public string CurrentToken => Volatile.Read(ref _currentToken);

    public string Rotate()
    {
        string token = GenerateToken();
        Interlocked.Exchange(ref _currentToken, token);
        return token;
    }

    public bool IsValid(string? candidate)
    {
        if (candidate is null || candidate.Length != TokenCharacterLength)
        {
            return false;
        }

        try
        {
            byte[] candidateBytes = Convert.FromHexString(candidate);
            byte[] currentBytes = Convert.FromHexString(CurrentToken);
            return CryptographicOperations.FixedTimeEquals(candidateBytes, currentBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenByteLength));
}
