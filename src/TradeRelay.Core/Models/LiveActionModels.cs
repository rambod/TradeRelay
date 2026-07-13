namespace TradeRelay.Core.Models;

/// <summary>Identifies a destructive Live action requiring desktop confirmation.</summary>
public enum LiveActionType { CancelAllOrders, ClosePosition }

/// <summary>Identifies the lifecycle of a Live action confirmation.</summary>
public enum LiveActionConfirmationState { Pending, Approved, Rejected, Expired, Executing, Completed, Failed }

/// <summary>Contains immutable parameters for one destructive Live action.</summary>
public sealed record LiveActionRequest(
    LiveActionType Action,
    string? Symbol,
    decimal? Quantity,
    string Scope,
    int CurrentMatchingCount);

/// <summary>Represents a session-only, desktop-approved Live action confirmation.</summary>
public sealed record LiveActionConfirmation(
    Guid ConfirmationId,
    string ClientRequestId,
    LiveActionRequest Request,
    TradingEnvironment Environment,
    Guid ConnectionGenerationId,
    Guid TradingSessionId,
    LiveActionConfirmationState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string ImmutableHash,
    string? ApprovedHash,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultCode,
    string? ResultMessage);

/// <summary>Contains the result of a Live confirmation-store operation.</summary>
public sealed record LiveActionConfirmationResult(
    bool Success,
    string Code,
    string Message,
    LiveActionConfirmation? Confirmation);

/// <summary>Returns either a confirmation request or the completed provider result.</summary>
public sealed record LiveActionOutcome<T>(LiveActionConfirmation? Confirmation, T? ProviderResult)
    where T : class;
