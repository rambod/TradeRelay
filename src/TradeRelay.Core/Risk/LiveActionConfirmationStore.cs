using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TradeRelay.Core.Models;

namespace TradeRelay.Core.Risk;

/// <summary>Stores short-lived destructive Live action confirmations for the current process.</summary>
public sealed partial class LiveActionConfirmationStore(TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, LiveActionConfirmation> _confirmations = new();
    private readonly ConcurrentDictionary<string, Guid> _requestIds = new(StringComparer.Ordinal);

    /// <summary>Raised when a confirmation is added or changes state.</summary>
    public event EventHandler<LiveActionConfirmation>? Changed;

    /// <summary>Creates an immutable pending Live action confirmation.</summary>
    public LiveActionConfirmationResult Add(
        string? clientRequestId,
        LiveActionRequest request,
        Guid connectionGenerationId,
        Guid tradingSessionId)
    {
        string requestId = clientRequestId?.Trim() ?? string.Empty;
        if (!ClientRequestPattern().IsMatch(requestId)) return Failure("VALIDATION_FAILED", "clientRequestId must contain 1–64 letters, numbers, dots, colons, dashes, or underscores.");
        if (_requestIds.ContainsKey(requestId)) return Failure("DUPLICATE_REQUEST", "This Live action clientRequestId has already been used in the current session.");

        DateTimeOffset created = timeProvider.GetUtcNow();
        DateTimeOffset expires = created.Add(Lifetime);
        Guid confirmationId = Guid.NewGuid();
        string hash = ComputeHash(confirmationId, requestId, request, connectionGenerationId, tradingSessionId, created, expires);
        var confirmation = new LiveActionConfirmation(
            confirmationId,
            requestId,
            request,
            TradingEnvironment.Live,
            connectionGenerationId,
            tradingSessionId,
            LiveActionConfirmationState.Pending,
            created,
            expires,
            hash,
            null,
            null,
            null,
            null,
            null,
            null);

        if (!_requestIds.TryAdd(requestId, confirmationId)) return Failure("DUPLICATE_REQUEST", "This Live action clientRequestId has already been used in the current session.");
        if (!_confirmations.TryAdd(confirmationId, confirmation))
        {
            _requestIds.TryRemove(requestId, out _);
            return Failure("INTERNAL_ERROR", "The Live action confirmation could not be stored.");
        }

        RaiseChanged(confirmation);
        return new(true, "LIVE_CONFIRMATION_REQUIRED", "Desktop confirmation is required before this Live action can execute.", confirmation);
    }

    /// <summary>Gets one confirmation and atomically expires it when required.</summary>
    public LiveActionConfirmation? Get(Guid confirmationId) =>
        _confirmations.TryGetValue(confirmationId, out LiveActionConfirmation? confirmation) ? ExpireIfNeeded(confirmation) : null;

    /// <summary>Gets confirmations with pending actions first and then newest.</summary>
    public IReadOnlyList<LiveActionConfirmation> GetAll() => _confirmations.Values
        .Select(ExpireIfNeeded)
        .OrderBy(item => item.State == LiveActionConfirmationState.Pending ? 0 : 1)
        .ThenByDescending(item => item.CreatedAtUtc)
        .ToArray();

    /// <summary>Gets unexpired confirmations awaiting desktop approval.</summary>
    public IReadOnlyList<LiveActionConfirmation> GetPending() => GetAll()
        .Where(item => item.State == LiveActionConfirmationState.Pending)
        .ToArray();

    /// <summary>Approves one pending confirmation when its immutable hash matches.</summary>
    public LiveActionConfirmationResult Approve(Guid confirmationId, string expectedHash) =>
        Transition(confirmationId, expectedHash, LiveActionConfirmationState.Approved);

    /// <summary>Rejects one pending confirmation when its immutable hash matches.</summary>
    public LiveActionConfirmationResult Reject(Guid confirmationId, string expectedHash) =>
        Transition(confirmationId, expectedHash, LiveActionConfirmationState.Rejected);

    /// <summary>Atomically consumes one approved confirmation when all immutable inputs still match.</summary>
    public LiveActionConfirmationResult Begin(
        Guid confirmationId,
        string clientRequestId,
        LiveActionRequest request,
        Guid connectionGenerationId,
        Guid tradingSessionId)
    {
        while (_confirmations.TryGetValue(confirmationId, out LiveActionConfirmation? current))
        {
            current = ExpireIfNeeded(current);
            if (current.State == LiveActionConfirmationState.Expired) return Result(false, "LIVE_CONFIRMATION_EXPIRED", "The Live action confirmation has expired.", current);
            if (current.State == LiveActionConfirmationState.Pending) return Result(false, "LIVE_CONFIRMATION_REQUIRED", "The Live action is still awaiting desktop confirmation.", current);
            if (current.State == LiveActionConfirmationState.Rejected) return Result(false, "LIVE_CONFIRMATION_REJECTED", "The desktop operator rejected this Live action.", current);
            if (current.ConnectionGenerationId != connectionGenerationId) return Result(false, "CONNECTION_CHANGED", "The authenticated exchange connection changed after this action was requested.", current);
            if (current.TradingSessionId != tradingSessionId) return Result(false, "LIVE_SESSION_CHANGED", "The Live trading session changed after this action was requested.", current);

            string expected = ComputeHash(current.ConfirmationId, clientRequestId.Trim(), request, connectionGenerationId, tradingSessionId, current.CreatedAtUtc, current.ExpiresAtUtc);
            if (!CryptographicEquals(current.ImmutableHash, expected) || !CryptographicEquals(current.ApprovedHash, expected))
                return Result(false, "CONFIRMATION_MISMATCH", "The Live action parameters do not match the approved confirmation.", current);
            if (current.State != LiveActionConfirmationState.Approved)
                return Result(false, "DUPLICATE_REQUEST", $"A {current.State} Live confirmation cannot execute again.", current);

            LiveActionConfirmation executing = current with { State = LiveActionConfirmationState.Executing };
            if (_confirmations.TryUpdate(confirmationId, executing, current))
            {
                RaiseChanged(executing);
                return Result(true, "OK", "The approved Live action was claimed for execution.", executing);
            }
        }

        return Failure("VALIDATION_FAILED", "The Live action confirmation was not found.");
    }

    /// <summary>Marks an executing confirmation completed.</summary>
    public LiveActionConfirmationResult Complete(Guid confirmationId, string code, string message) =>
        Finish(confirmationId, LiveActionConfirmationState.Completed, code, message);

    /// <summary>Marks an executing confirmation failed.</summary>
    public LiveActionConfirmationResult Fail(Guid confirmationId, string code, string message) =>
        Finish(confirmationId, LiveActionConfirmationState.Failed, code, message);

    /// <summary>Expires all unexecuted confirmations during disable or application shutdown.</summary>
    public void ExpireAllUnexecuted()
    {
        foreach (LiveActionConfirmation confirmation in _confirmations.Values)
        {
            if (confirmation.State is not (LiveActionConfirmationState.Pending or LiveActionConfirmationState.Approved)) continue;
            LiveActionConfirmation expired = confirmation with { State = LiveActionConfirmationState.Expired };
            if (_confirmations.TryUpdate(confirmation.ConfirmationId, expired, confirmation)) RaiseChanged(expired);
        }
    }

    private LiveActionConfirmationResult Transition(Guid confirmationId, string expectedHash, LiveActionConfirmationState target)
    {
        while (_confirmations.TryGetValue(confirmationId, out LiveActionConfirmation? current))
        {
            current = ExpireIfNeeded(current);
            if (!CryptographicEquals(current.ImmutableHash, expectedHash)) return Result(false, "CONFIRMATION_MISMATCH", "The immutable Live action hash does not match.", current);
            if (current.State == LiveActionConfirmationState.Expired) return Result(false, "LIVE_CONFIRMATION_EXPIRED", "The Live action confirmation has expired.", current);
            if (current.State != LiveActionConfirmationState.Pending) return Result(false, "LIVE_CONFIRMATION_REJECTED", $"A {current.State} Live confirmation cannot change approval state.", current);
            DateTimeOffset now = timeProvider.GetUtcNow();
            LiveActionConfirmation updated = target == LiveActionConfirmationState.Approved
                ? current with { State = target, ApprovedHash = current.ImmutableHash, ApprovedAtUtc = now }
                : current with { State = target, RejectedAtUtc = now };
            if (_confirmations.TryUpdate(confirmationId, updated, current))
            {
                RaiseChanged(updated);
                return Result(true, "OK", target == LiveActionConfirmationState.Approved ? "Live action approved for one execution attempt." : "Live action rejected.", updated);
            }
        }

        return Failure("VALIDATION_FAILED", "The Live action confirmation was not found.");
    }

    private LiveActionConfirmationResult Finish(Guid confirmationId, LiveActionConfirmationState state, string code, string message)
    {
        while (_confirmations.TryGetValue(confirmationId, out LiveActionConfirmation? current))
        {
            if (current.State != LiveActionConfirmationState.Executing) return Result(false, "DUPLICATE_REQUEST", $"A {current.State} Live confirmation cannot finish execution.", current);
            LiveActionConfirmation completed = current with { State = state, CompletedAtUtc = timeProvider.GetUtcNow(), ResultCode = code, ResultMessage = message };
            if (_confirmations.TryUpdate(confirmationId, completed, current))
            {
                RaiseChanged(completed);
                return Result(state == LiveActionConfirmationState.Completed, code, message, completed);
            }
        }

        return Failure("VALIDATION_FAILED", "The Live action confirmation was not found.");
    }

    private LiveActionConfirmation ExpireIfNeeded(LiveActionConfirmation confirmation)
    {
        if (confirmation.State is not (LiveActionConfirmationState.Pending or LiveActionConfirmationState.Approved) || timeProvider.GetUtcNow() < confirmation.ExpiresAtUtc) return confirmation;
        LiveActionConfirmation expired = confirmation with { State = LiveActionConfirmationState.Expired };
        if (_confirmations.TryUpdate(confirmation.ConfirmationId, expired, confirmation))
        {
            RaiseChanged(expired);
            return expired;
        }

        return _confirmations.TryGetValue(confirmation.ConfirmationId, out LiveActionConfirmation? latest) ? latest : expired;
    }

    private static string ComputeHash(Guid confirmationId, string requestId, LiveActionRequest request, Guid connectionGenerationId, Guid tradingSessionId, DateTimeOffset created, DateTimeOffset expires)
    {
        string text = string.Join('|',
            confirmationId.ToString("N"),
            requestId,
            request.Action.ToString(),
            request.Symbol ?? "null",
            request.Quantity?.ToString("G29", CultureInfo.InvariantCulture) ?? "null",
            request.Scope,
            request.CurrentMatchingCount.ToString(CultureInfo.InvariantCulture),
            connectionGenerationId.ToString("N"),
            tradingSessionId.ToString("N"),
            created.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            expires.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool CryptographicEquals(string? left, string? right)
    {
        if (left is null || right is null || left.Length != right.Length) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }

    private void RaiseChanged(LiveActionConfirmation confirmation)
    {
        try { Changed?.Invoke(this, confirmation); } catch { }
    }

    private static LiveActionConfirmationResult Result(bool success, string code, string message, LiveActionConfirmation confirmation) => new(success, code, message, confirmation);
    private static LiveActionConfirmationResult Failure(string code, string message) => new(false, code, message, null);
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.CultureInvariant)] private static partial Regex ClientRequestPattern();
}
