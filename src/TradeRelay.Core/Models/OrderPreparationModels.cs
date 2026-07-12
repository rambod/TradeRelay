using TradeRelay.Core.Settings;

namespace TradeRelay.Core.Models;

/// <summary>Supported simulated order types.</summary>
public enum OrderType { Market, Limit }

/// <summary>Requests validation or preparation of a simulated order.</summary>
public sealed record PrepareOrderRequest(
    string? ClientRequestId,
    string Symbol,
    TradeSide Side,
    OrderType OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? RequestedLeverage,
    string? UserNote);

/// <summary>Captures the risk limits used to create an immutable plan.</summary>
public sealed record RiskSettingsSnapshot(
    IReadOnlyList<string> AllowedSymbols,
    decimal MaxRiskPerTradePercent,
    decimal MaxOrderNotionalUsd,
    int MaxOpenPositions,
    decimal MaxLeverage,
    bool RequireStopLoss,
    bool RequireManualApproval,
    int PreparedOrderExpirySeconds)
{
    /// <summary>Creates an immutable snapshot for an environment.</summary>
    public static RiskSettingsSnapshot Create(RiskSettings settings, TradingEnvironment environment) => new(
        settings.AllowedSymbols.Order(StringComparer.Ordinal).ToArray(),
        settings.MaxRiskPerTradePercent,
        settings.MaxOrderNotionalUsd,
        settings.MaxOpenPositions,
        settings.MaxLeverage,
        settings.RequireStopLoss,
        environment == TradingEnvironment.Live ? settings.RequireManualApprovalForLive : settings.RequireManualApprovalForDemo,
        settings.PreparedOrderExpirySeconds);
}

/// <summary>Contains normalized risk estimates; values are null when risk cannot be calculated.</summary>
public sealed record RiskEstimate(
    decimal EstimatedNotionalUsd,
    decimal? EstimatedRiskUsd,
    decimal? EstimatedRewardUsd,
    decimal? RiskRewardRatio,
    decimal? AccountRiskPercent);

/// <summary>Contains the exchange-normalized simulated order.</summary>
public sealed record NormalizedOrder(
    string Symbol,
    TradeSide Side,
    OrderType OrderType,
    decimal RequestedQuantity,
    decimal Quantity,
    decimal? RequestedLimitPrice,
    decimal? LimitPrice,
    decimal EstimatedEntryPrice,
    decimal? RequestedStopLoss,
    decimal? StopLoss,
    decimal? RequestedTakeProfit,
    decimal? TakeProfit,
    decimal Leverage,
    RiskEstimate Risk,
    string? UserNote);

/// <summary>Returns normalized values and validation findings without storing an order.</summary>
public sealed record OrderValidationResult(
    bool Valid,
    NormalizedOrder? Order,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>Returns a risk-based position-size calculation.</summary>
public sealed record PositionSizeResult(
    bool Valid,
    decimal RawQuantity,
    decimal NormalizedQuantity,
    decimal EstimatedNotionalUsd,
    decimal EstimatedRiskUsd,
    decimal AccountRiskPercent,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>Represents one immutable prepared plan and its execution lifecycle.</summary>
public sealed record PreparedOrder(
    Guid PreparationId,
    string ClientRequestId,
    string ClientOrderId,
    NormalizedOrder Order,
    TradingEnvironment Environment,
    RiskSettingsSnapshot RiskSettings,
    PreparedOrderState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string ImmutableHash,
    string? ApprovedHash,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    IReadOnlyList<string> Warnings,
    DateTimeOffset? ExecutionStartedAtUtc = null,
    DateTimeOffset? ExecutionCompletedAtUtc = null,
    OrderSubmissionResult? Submission = null);

/// <summary>Represents an atomic prepared-order store operation.</summary>
public sealed record PreparedOrderResult(bool Success, string Code, string Message, PreparedOrder? Order);

/// <summary>Reports whether non-secret risk settings are valid.</summary>
public sealed record RiskSettingsValidationResult(bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
