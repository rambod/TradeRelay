using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeRelay.Desktop.Security;

namespace TradeRelay.Desktop.Services;

internal sealed class LocalMcpTokenService(IProtectedSecretStore protectedStore, ILogger<LocalMcpTokenService> logger) : IHostedService
{
    internal const int TokenByteLength = 32;
    internal const int TokenCharacterLength = TokenByteLength * 2;
    internal const string MaskedToken = "••••••••••••••••";

    private string _currentToken = GenerateToken();

    internal LocalMcpTokenService() : this(new SessionProtectedSecretStore(), NullLogger<LocalMcpTokenService>.Instance) { }

    public string CurrentToken => Volatile.Read(ref _currentToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!protectedStore.CanPersist) return;
        try
        {
            string? stored = await protectedStore.LoadAsync("mcp:bearer-token", cancellationToken).ConfigureAwait(false);
            if (stored is { Length: TokenCharacterLength } && IsHex(stored)) Interlocked.Exchange(ref _currentToken, stored);
            else await protectedStore.SaveAsync("mcp:bearer-token", CurrentToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Protected MCP token storage is unavailable; using a session token ({ErrorType})", exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public string Rotate()
    {
        string token = GenerateToken();
        Interlocked.Exchange(ref _currentToken, token);
        return token;
    }

    public async Task<string> RotateAsync(CancellationToken cancellationToken)
    {
        string token = Rotate();
        if (protectedStore.CanPersist)
        {
            try { await protectedStore.SaveAsync("mcp:bearer-token", token, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("The rotated MCP token could not be persisted ({ErrorType})", exception.GetType().Name);
            }
        }
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

    private static bool IsHex(string value)
    {
        try { _ = Convert.FromHexString(value); return true; }
        catch (FormatException) { return false; }
    }
}

file sealed class SessionProtectedSecretStore : IProtectedSecretStore
{
    public bool CanPersist => false;
    public Task SaveAsync(string id, string value, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<string?> LoadAsync(string id, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task DeleteAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
}
