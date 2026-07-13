using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Security;

namespace TradeRelay.Desktop.Services;

internal sealed record OAuthTokenResult(bool Success, string Code, string? AccessToken, string? RefreshToken, int ExpiresIn, string Scope, string? Error);

internal sealed class OAuthPairingService(
    ApplicationDataPaths paths,
    IProtectedSecretStore protectedStore,
    TimeProvider timeProvider) : IHostedService
{
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshInactivity = TimeSpan.FromDays(30);
    private readonly ConcurrentDictionary<string, RegisteredClient> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, PairingRequest> _pairings = new();
    private readonly ConcurrentDictionary<string, AuthorizationCode> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AccessGrant> _access = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public event EventHandler<OAuthPairingSnapshot>? PairingChanged;
    public event EventHandler? ClientsChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LoadClients();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public IReadOnlyList<OAuthClientSnapshot> GetClients() => _clients.Values.Select(ToSnapshot).OrderBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<OAuthPairingSnapshot> GetPendingPairings()
    {
        ExpirePairings();
        return _pairings.Values.Where(item => item.State == OAuthPairingState.Pending).Select(ToSnapshot).OrderBy(item => item.CreatedUtc).ToArray();
    }

    public OAuthPairingSnapshot? GetPairing(Guid pairingId)
    {
        ExpirePairings();
        return _pairings.TryGetValue(pairingId, out PairingRequest? request) ? ToSnapshot(request) : null;
    }

    public OAuthClientSnapshot RegisterClient(string clientName, IReadOnlyList<string> redirectUris)
    {
        string safeName = string.IsNullOrWhiteSpace(clientName) ? "Local MCP client" : clientName.Trim()[..Math.Min(clientName.Trim().Length, 80)];
        string[] redirects = redirectUris.Where(IsAllowedRedirect).Distinct(StringComparer.Ordinal).Take(8).ToArray();
        if (redirects.Length == 0) throw new ArgumentException("At least one exact loopback redirect URI is required.", nameof(redirectUris));
        DateTimeOffset now = timeProvider.GetUtcNow();
        var client = new RegisteredClient($"trc_{RandomHex(16)}", safeName, redirects, [], now, null, false);
        _clients[client.ClientId] = client;
        PersistClientsAsync(CancellationToken.None).GetAwaiter().GetResult();
        ClientsChanged?.Invoke(this, EventArgs.Empty);
        return ToSnapshot(client);
    }

    public OAuthPairingSnapshot BeginAuthorization(string clientId, string redirectUri, string state, string codeChallenge, string codeChallengeMethod, IReadOnlyList<string> requestedScopes)
    {
        if (!_clients.TryGetValue(clientId, out RegisteredClient? client) || client.Revoked) throw new InvalidOperationException("The OAuth client is not registered.");
        if (!client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal)) throw new InvalidOperationException("The redirect URI does not exactly match registration.");
        if (string.IsNullOrWhiteSpace(state) || state.Length > 256) throw new InvalidOperationException("A valid OAuth state value is required.");
        if (!string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal) || !IsBase64Url(codeChallenge, 43, 128)) throw new InvalidOperationException("PKCE-S256 is required.");
        string[] scopes = NormalizeScopes(requestedScopes);
        DateTimeOffset now = timeProvider.GetUtcNow();
        var request = new PairingRequest(Guid.NewGuid(), clientId, client.ClientName, redirectUri, state, codeChallenge, scopes, OAuthPairingState.Pending, now, now + PairingLifetime, null, null);
        _pairings[request.PairingId] = request;
        OAuthPairingSnapshot snapshot = ToSnapshot(request);
        PairingChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    public bool Approve(Guid pairingId, bool grantTrade)
    {
        if (!_pairings.TryGetValue(pairingId, out PairingRequest? request) || request.State != OAuthPairingState.Pending || request.ExpiresUtc <= timeProvider.GetUtcNow()) return false;
        string[] granted = request.RequestedScopes.Where(scope => scope != TradeRelayScopes.Trade || grantTrade).ToArray();
        if (!granted.Contains(TradeRelayScopes.Read, StringComparer.Ordinal)) granted = [TradeRelayScopes.Read, .. granted];
        string code = $"tra_{RandomHex(32)}";
        DateTimeOffset now = timeProvider.GetUtcNow();
        var authorizationCode = new AuthorizationCode(Hash(code), request.ClientId, request.RedirectUri, request.CodeChallenge, granted, now + CodeLifetime, request.PairingId);
        _codes[authorizationCode.Hash] = authorizationCode;
        PairingRequest approved = request with { State = OAuthPairingState.Approved, AuthorizationCode = code, GrantedScopes = granted };
        _pairings[pairingId] = approved;
        PairingChanged?.Invoke(this, ToSnapshot(approved));
        return true;
    }

    public bool Reject(Guid pairingId)
    {
        if (!_pairings.TryGetValue(pairingId, out PairingRequest? request) || request.State != OAuthPairingState.Pending) return false;
        PairingRequest rejected = request with { State = OAuthPairingState.Rejected };
        _pairings[pairingId] = rejected;
        PairingChanged?.Invoke(this, ToSnapshot(rejected));
        return true;
    }

    public string? GetAuthorizationRedirect(Guid pairingId)
    {
        ExpirePairings();
        if (!_pairings.TryGetValue(pairingId, out PairingRequest? request)) return null;
        if (request.State == OAuthPairingState.Rejected) return AddQuery(request.RedirectUri, "error", "access_denied", "state", request.StateValue);
        if (request.State != OAuthPairingState.Approved || request.AuthorizationCode is null) return null;
        return AddQuery(request.RedirectUri, "code", request.AuthorizationCode, "state", request.StateValue);
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, string clientId, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        string hash = Hash(code);
        if (!_codes.TryRemove(hash, out AuthorizationCode? grant) || grant.ExpiresUtc <= timeProvider.GetUtcNow()) return Failure("invalid_grant", "The authorization code is invalid, expired, or already used.");
        if (!string.Equals(grant.ClientId, clientId, StringComparison.Ordinal) || !string.Equals(grant.RedirectUri, redirectUri, StringComparison.Ordinal) || !VerifyPkce(grant.CodeChallenge, codeVerifier)) return Failure("invalid_grant", "The authorization code binding is invalid.");
        if (_pairings.TryGetValue(grant.PairingId, out PairingRequest? pairing))
        {
            PairingRequest completed = pairing with { State = OAuthPairingState.Completed, AuthorizationCode = null };
            _pairings[grant.PairingId] = completed;
            PairingChanged?.Invoke(this, ToSnapshot(completed));
        }
        return await IssueTokensAsync(clientId, grant.Scopes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OAuthTokenResult> RefreshAsync(string refreshToken, string? requestedScope, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryGetRefreshClient(refreshToken, out string clientId) || !_clients.TryGetValue(clientId, out RegisteredClient? client) || client.Revoked) return Failure("invalid_grant", "The refresh credential is invalid or revoked.");
            if (client.LastSeenUtc is null || timeProvider.GetUtcNow() - client.LastSeenUtc > RefreshInactivity) return Failure("invalid_grant", "The refresh credential expired after inactivity.");
            string? storedHash = await protectedStore.LoadAsync(RefreshStorageId(clientId), cancellationToken).ConfigureAwait(false);
            if (storedHash is null || !FixedEquals(storedHash, Hash(refreshToken))) return Failure("invalid_grant", "The refresh credential is invalid or already rotated.");
            string[] scopes = string.IsNullOrWhiteSpace(requestedScope) ? client.GrantedScopes : NormalizeScopes(requestedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (scopes.Any(scope => !client.GrantedScopes.Contains(scope, StringComparer.Ordinal))) return Failure("invalid_scope", "Refresh cannot escalate client scopes.");
            return await IssueTokensAsync(clientId, scopes, cancellationToken).ConfigureAwait(false);
        }
        finally { _refreshLock.Release(); }
    }

    public bool ValidateAccessToken(string token, string requiredScope, out OAuthClientSnapshot? client)
    {
        client = null;
        string hash = Hash(token);
        foreach ((string stored, AccessGrant grant) in _access)
        {
            if (!FixedEquals(stored, hash)) continue;
            if (grant.ExpiresUtc <= timeProvider.GetUtcNow() || !grant.Scopes.Contains(requiredScope, StringComparer.Ordinal) || !_clients.TryGetValue(grant.ClientId, out RegisteredClient? registered) || registered.Revoked)
            {
                _access.TryRemove(stored, out _);
                return false;
            }
            client = ToSnapshot(registered);
            return true;
        }
        return false;
    }

    public async Task<bool> RevokeClientAsync(string clientId, CancellationToken cancellationToken)
    {
        if (!_clients.TryGetValue(clientId, out RegisteredClient? client)) return false;
        _clients[clientId] = client with { Revoked = true };
        foreach ((string key, AccessGrant grant) in _access.Where(item => item.Value.ClientId == clientId).ToArray()) _access.TryRemove(key, out _);
        await protectedStore.DeleteAsync(RefreshStorageId(clientId), cancellationToken).ConfigureAwait(false);
        await PersistClientsAsync(cancellationToken).ConfigureAwait(false);
        ClientsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task<OAuthTokenResult> IssueTokensAsync(string clientId, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        string accessToken = $"access.{RandomHex(32)}";
        string refreshToken = $"refresh.{clientId}.{RandomHex(32)}";
        DateTimeOffset now = timeProvider.GetUtcNow();
        _access[Hash(accessToken)] = new AccessGrant(clientId, scopes.ToArray(), now + AccessLifetime);
        await protectedStore.SaveAsync(RefreshStorageId(clientId), Hash(refreshToken), cancellationToken).ConfigureAwait(false);
        if (_clients.TryGetValue(clientId, out RegisteredClient? client))
        {
            _clients[clientId] = client with { GrantedScopes = scopes.ToArray(), LastSeenUtc = now, Revoked = false };
            await PersistClientsAsync(cancellationToken).ConfigureAwait(false);
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
        return new OAuthTokenResult(true, "OK", accessToken, refreshToken, (int)AccessLifetime.TotalSeconds, string.Join(' ', scopes), null);
    }

    private void ExpirePairings()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach ((Guid id, PairingRequest request) in _pairings.Where(item => item.Value.State == OAuthPairingState.Pending && item.Value.ExpiresUtc <= now).ToArray())
        {
            PairingRequest expired = request with { State = OAuthPairingState.Expired };
            _pairings[id] = expired;
            PairingChanged?.Invoke(this, ToSnapshot(expired));
        }
    }

    private void LoadClients()
    {
        try
        {
            if (!File.Exists(paths.OAuthClientsFile)) return;
            RegisteredClient[]? clients = JsonSerializer.Deserialize<RegisteredClient[]>(File.ReadAllText(paths.OAuthClientsFile));
            foreach (RegisteredClient client in clients ?? []) _clients[client.ClientId] = client;
        }
        catch { }
    }

    private async Task PersistClientsAsync(CancellationToken cancellationToken)
    {
        await _persistenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.Root);
            string temporary = paths.OAuthClientsFile + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_clients.Values.OrderBy(item => item.ClientId)), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, paths.OAuthClientsFile, true);
        }
        finally { _persistenceLock.Release(); }
    }

    private static string[] NormalizeScopes(IEnumerable<string> scopes)
    {
        string[] normalized = scopes.SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(TradeRelayScopes.All.Contains).Distinct(StringComparer.Ordinal).ToArray();
        return normalized.Length == 0 ? TradeRelayScopes.Default.ToArray() : normalized;
    }
    private static bool IsAllowedRedirect(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttp && IPAddressIsLoopback(uri.Host);
    private static bool IPAddressIsLoopback(string host) => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || System.Net.IPAddress.TryParse(host, out System.Net.IPAddress? address) && System.Net.IPAddress.IsLoopback(address);
    private static bool IsBase64Url(string value, int minimum, int maximum) => value.Length >= minimum && value.Length <= maximum && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static bool VerifyPkce(string challenge, string verifier) => IsBase64Url(verifier, 43, 128) && FixedEquals(challenge, Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    private static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static bool TryGetRefreshClient(string token, out string clientId) { string[] parts = token.Split('.', 3); clientId = parts.Length == 3 ? parts[1] : string.Empty; return parts.Length == 3 && clientId.StartsWith("trc_", StringComparison.Ordinal); }
    private static string RefreshStorageId(string clientId) => $"oauth:refresh:{clientId}";
    private static string AddQuery(string uri, string key1, string value1, string key2, string value2) => $"{uri}{(uri.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{Uri.EscapeDataString(key1)}={Uri.EscapeDataString(value1)}&{Uri.EscapeDataString(key2)}={Uri.EscapeDataString(value2)}";
    private static OAuthTokenResult Failure(string code, string message) => new(false, code, null, null, 0, string.Empty, message);
    private static OAuthClientSnapshot ToSnapshot(RegisteredClient client) => new(client.ClientId, client.ClientName, client.RedirectUris, client.GrantedScopes, client.RegisteredUtc, client.LastSeenUtc, client.Revoked);
    private static OAuthPairingSnapshot ToSnapshot(PairingRequest request) => new(request.PairingId, request.ClientId, request.ClientName, request.RedirectUri, request.RequestedScopes, request.State, request.CreatedUtc, request.ExpiresUtc, request.State == OAuthPairingState.Rejected ? "Pairing was rejected by the desktop operator." : null);

    private sealed record RegisteredClient(string ClientId, string ClientName, string[] RedirectUris, string[] GrantedScopes, DateTimeOffset RegisteredUtc, DateTimeOffset? LastSeenUtc, bool Revoked);
    private sealed record PairingRequest(Guid PairingId, string ClientId, string ClientName, string RedirectUri, string StateValue, string CodeChallenge, string[] RequestedScopes, OAuthPairingState State, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, string? AuthorizationCode, string[]? GrantedScopes);
    private sealed record AuthorizationCode(string Hash, string ClientId, string RedirectUri, string CodeChallenge, string[] Scopes, DateTimeOffset ExpiresUtc, Guid PairingId);
    private sealed record AccessGrant(string ClientId, string[] Scopes, DateTimeOffset ExpiresUtc);
}
