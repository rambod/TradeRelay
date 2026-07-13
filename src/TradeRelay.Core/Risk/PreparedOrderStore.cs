using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TradeRelay.Core.Models;

namespace TradeRelay.Core.Risk;

/// <summary>Stores immutable prepared plans and execution state for the current process.</summary>
public sealed partial class PreparedOrderStore(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<Guid, PreparedOrder> _orders = new();
    private readonly ConcurrentDictionary<string, Guid> _requestIds = new(StringComparer.Ordinal);

    /// <summary>Raised when a prepared order is added or changes state.</summary>
    public event EventHandler<PreparedOrder>? Changed;

    /// <summary>Adds a valid normalized simulation with session idempotency.</summary>
    public PreparedOrderResult Add(
        string? clientRequestId,
        OrderValidationResult validation,
        TradingEnvironment environment,
        RiskSettingsSnapshot settings,
        Guid connectionGenerationId = default)
    {
        string requestId = clientRequestId?.Trim() ?? string.Empty;
        if (!ClientRequestPattern().IsMatch(requestId)) return Failure("VALIDATION_FAILED", "clientRequestId must contain 1–64 letters, numbers, dots, colons, dashes, or underscores.");
        if (!validation.Valid || validation.Order is null) return Failure("VALIDATION_FAILED", "Only a valid normalized order can be prepared.");
        if (_requestIds.ContainsKey(requestId)) return Failure("DUPLICATE_REQUEST", "This clientRequestId has already been used in the current session.");

        DateTimeOffset created = timeProvider.GetUtcNow();
        Guid preparationId = Guid.NewGuid();
        string clientOrderId = $"tr-{Guid.NewGuid():N}"[..23];
        DateTimeOffset expires = created.AddSeconds(settings.PreparedOrderExpirySeconds);
        PreparedOrderState state = settings.RequireManualApproval ? PreparedOrderState.PendingApproval : PreparedOrderState.Approved;
        string hash = ComputeHash(preparationId, requestId, clientOrderId, validation.Order, environment, settings, connectionGenerationId, created, expires, validation.Warnings);
        var order = new PreparedOrder(
            preparationId,
            requestId,
            clientOrderId,
            validation.Order,
            environment,
            settings,
            state,
            created,
            expires,
            hash,
            state == PreparedOrderState.Approved ? hash : null,
            state == PreparedOrderState.Approved ? created : null,
            null,
            validation.Warnings,
            ConnectionGenerationId: connectionGenerationId);

        if (!_requestIds.TryAdd(requestId, preparationId)) return Failure("DUPLICATE_REQUEST", "This clientRequestId has already been used in the current session.");
        if (!_orders.TryAdd(preparationId, order))
        {
            _requestIds.TryRemove(requestId, out _);
            return Failure("INTERNAL_ERROR", "The prepared simulation could not be stored.");
        }
        RaiseChanged(order);
        return new(true, "OK", state == PreparedOrderState.Approved ? "Plan prepared and auto-approved by the current Demo policy." : "Plan prepared and awaiting desktop approval.", order);
    }

    /// <summary>Gets a prepared order, atomically expiring it when necessary.</summary>
    public PreparedOrder? Get(Guid preparationId) => _orders.TryGetValue(preparationId, out PreparedOrder? order) ? ExpireIfNeeded(order) : null;

    /// <summary>Gets all session orders, review-needed first and then newest.</summary>
    public IReadOnlyList<PreparedOrder> GetAll() => _orders.Values.Select(ExpireIfNeeded).OrderBy(order => order.State == PreparedOrderState.PendingApproval ? 0 : 1).ThenByDescending(order => order.CreatedAtUtc).ToArray();

    /// <summary>Gets only unexpired orders requiring approval.</summary>
    public IReadOnlyList<PreparedOrder> GetPending() => GetAll().Where(order => order.State == PreparedOrderState.PendingApproval).ToArray();

    /// <summary>Expires every unexecuted plan during application shutdown.</summary>
    public void ExpireAllUnexecuted()
    {
        foreach (PreparedOrder order in _orders.Values)
        {
            if (order.State is not (PreparedOrderState.PendingApproval or PreparedOrderState.Approved)) continue;
            PreparedOrder expired = order with { State = PreparedOrderState.Expired };
            if (_orders.TryUpdate(order.PreparationId, expired, order)) RaiseChanged(expired);
        }
    }

    /// <summary>Approves a pending order only when its immutable hash still matches.</summary>
    public PreparedOrderResult Approve(Guid preparationId, string expectedHash) => Transition(preparationId, expectedHash, PreparedOrderState.Approved);

    /// <summary>Rejects a pending order only when its immutable hash still matches.</summary>
    public PreparedOrderResult Reject(Guid preparationId, string expectedHash) => Transition(preparationId, expectedHash, PreparedOrderState.Rejected);

    /// <summary>Atomically claims an approved plan for one execution attempt.</summary>
    public PreparedOrderResult BeginExecution(Guid preparationId, string expectedHash)
    {
        while (_orders.TryGetValue(preparationId, out PreparedOrder? current))
        {
            current = ExpireIfNeeded(current);
            if (current.State == PreparedOrderState.Expired) return new(false, "ORDER_EXPIRED", "The prepared order has expired.", current);
            if (!CryptographicEquals(current.ImmutableHash, expectedHash) || !CryptographicEquals(current.ApprovedHash, expectedHash)) return new(false, "APPROVAL_REQUIRED", "The immutable plan is not currently approved.", current);
            if (current.State != PreparedOrderState.Approved) return new(false, "DUPLICATE_REQUEST", $"A {current.State} order cannot begin execution.", current);
            PreparedOrder executing = current with { State = PreparedOrderState.Executing, ExecutionStartedAtUtc = timeProvider.GetUtcNow() };
            if (_orders.TryUpdate(preparationId, executing, current)) { RaiseChanged(executing); return new(true, "OK", "Prepared order claimed for execution.", executing); }
        }
        return Failure("VALIDATION_FAILED", "The prepared order was not found.");
    }

    /// <summary>Stores a reconciled execution result.</summary>
    public PreparedOrderResult CompleteExecution(Guid preparationId, OrderSubmissionResult submission) => FinishExecution(preparationId, PreparedOrderState.Executed, submission, "Execution reconciled.");

    /// <summary>Stores a failed or unknown execution result.</summary>
    public PreparedOrderResult FailExecution(Guid preparationId, OrderSubmissionResult? submission, string message) => FinishExecution(preparationId, PreparedOrderState.Failed, submission, message);

    private PreparedOrderResult Transition(Guid preparationId, string expectedHash, PreparedOrderState target)
    {
        while (_orders.TryGetValue(preparationId, out PreparedOrder? current))
        {
            current = ExpireIfNeeded(current);
            if (!CryptographicEquals(current.ImmutableHash, expectedHash)) return new(false, "APPROVAL_REJECTED", "The immutable order hash does not match.", current);
            if (current.State == PreparedOrderState.Expired) return new(false, "ORDER_EXPIRED", "The prepared plan has expired.", current);
            if (current.State != PreparedOrderState.PendingApproval) return new(false, "APPROVAL_REJECTED", $"A {current.State} plan cannot change approval state.", current);
            DateTimeOffset now = timeProvider.GetUtcNow();
            PreparedOrder updated = target == PreparedOrderState.Approved
                ? current with { State = target, ApprovedHash = current.ImmutableHash, ApprovedAtUtc = now }
                : current with { State = target, RejectedAtUtc = now };
            if (_orders.TryUpdate(preparationId, updated, current))
            {
                RaiseChanged(updated);
                return new(true, "OK", target == PreparedOrderState.Approved ? "Plan approved and eligible for the gated Demo execution flow." : "Plan rejected.", updated);
            }
        }
        return Failure("VALIDATION_FAILED", "The prepared plan was not found.");
    }

    private PreparedOrder ExpireIfNeeded(PreparedOrder order)
    {
        if (order.State is not (PreparedOrderState.PendingApproval or PreparedOrderState.Approved) || timeProvider.GetUtcNow() < order.ExpiresAtUtc) return order;
        PreparedOrder expired = order with { State = PreparedOrderState.Expired };
        if (_orders.TryUpdate(order.PreparationId, expired, order))
        {
            RaiseChanged(expired);
            return expired;
        }
        return _orders.TryGetValue(order.PreparationId, out PreparedOrder? latest) ? latest : expired;
    }

    private PreparedOrderResult FinishExecution(Guid preparationId, PreparedOrderState state, OrderSubmissionResult? submission, string message)
    {
        while (_orders.TryGetValue(preparationId, out PreparedOrder? current))
        {
            if (current.State != PreparedOrderState.Executing) return new(false, "DUPLICATE_REQUEST", $"A {current.State} order cannot finish execution.", current);
            PreparedOrder completed = current with { State = state, Submission = submission, ExecutionCompletedAtUtc = timeProvider.GetUtcNow() };
            if (_orders.TryUpdate(preparationId, completed, current))
            {
                RaiseChanged(completed);
                return new(state == PreparedOrderState.Executed, state == PreparedOrderState.Executed ? "OK" : "ORDER_STATE_UNKNOWN", message, completed);
            }
        }
        return Failure("VALIDATION_FAILED", "The prepared order was not found.");
    }

    private static string ComputeHash(Guid preparationId, string requestId, string clientOrderId, NormalizedOrder order, TradingEnvironment environment, RiskSettingsSnapshot settings, Guid connectionGenerationId, DateTimeOffset created, DateTimeOffset expires, IReadOnlyList<string> warnings)
    {
        static string Decimal(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? "null";
        var text = new StringBuilder()
            .Append(preparationId.ToString("N")).Append('|').Append(requestId).Append('|').Append(clientOrderId).Append('|')
            .Append(order.Symbol).Append('|').Append(order.Side).Append('|').Append(order.OrderType).Append('|')
            .Append(Decimal(order.RequestedQuantity)).Append('|').Append(Decimal(order.Quantity)).Append('|')
            .Append(Decimal(order.RequestedLimitPrice)).Append('|').Append(Decimal(order.LimitPrice)).Append('|').Append(Decimal(order.EstimatedEntryPrice)).Append('|')
            .Append(Decimal(order.RequestedStopLoss)).Append('|').Append(Decimal(order.StopLoss)).Append('|')
            .Append(Decimal(order.RequestedTakeProfit)).Append('|').Append(Decimal(order.TakeProfit)).Append('|').Append(Decimal(order.Leverage)).Append('|')
            .Append(Decimal(order.Risk.EstimatedNotionalUsd)).Append('|').Append(Decimal(order.Risk.EstimatedRiskUsd)).Append('|')
            .Append(Decimal(order.Risk.EstimatedRewardUsd)).Append('|').Append(Decimal(order.Risk.RiskRewardRatio)).Append('|').Append(Decimal(order.Risk.AccountRiskPercent)).Append('|')
            .Append(order.UserNote?.Replace("|", "||", StringComparison.Ordinal) ?? "null").Append('|').Append(environment).Append('|')
            .Append(string.Join(',', settings.AllowedSymbols)).Append('|').Append(Decimal(settings.MaxRiskPerTradePercent)).Append('|').Append(Decimal(settings.MaxOrderNotionalUsd)).Append('|')
            .Append(settings.MaxOpenPositions).Append('|').Append(Decimal(settings.MaxLeverage)).Append('|').Append(Decimal(settings.MaxMarketPriceDeviationPercent)).Append('|')
            .Append(settings.RequireStopLoss).Append('|').Append(settings.RequireManualApproval).Append('|').Append(settings.PreparedOrderExpirySeconds).Append('|')
            .Append(connectionGenerationId.ToString("N")).Append('|')
            .Append(created.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|').Append(expires.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|').Append(string.Join("||", warnings));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static bool CryptographicEquals(string? left, string? right)
    {
        if (left is null || right is null) return false;
        if (left.Length != right.Length) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }

    private void RaiseChanged(PreparedOrder order)
    {
        try { Changed?.Invoke(this, order); } catch { }
    }

    private static PreparedOrderResult Failure(string code, string message) => new(false, code, message, null);
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.CultureInvariant)] private static partial Regex ClientRequestPattern();
}
