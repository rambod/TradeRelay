namespace TradeRelay.Core.Models;

/// <summary>Identifies whether history came from the exchange or TradeRelay observation.</summary>
public enum HistorySource { Exchange, TradeRelayObserved }

/// <summary>One normalized historical order returned by an exchange.</summary>
public sealed record HistoricalOrder(
    string ExchangeOrderId,
    string? ClientOrderId,
    string Symbol,
    TradeSide Side,
    string Type,
    decimal? Price,
    decimal Quantity,
    decimal FilledQuantity,
    decimal RemainingQuantity,
    string Status,
    bool ReduceOnly,
    DateTimeOffset CreatedTimeUtc,
    DateTimeOffset UpdatedTimeUtc,
    HistorySource Source = HistorySource.Exchange);

/// <summary>One normalized historical fill returned by an exchange.</summary>
public sealed record HistoricalExecution(
    string ExecutionId,
    string ExchangeOrderId,
    string? ClientOrderId,
    string Symbol,
    TradeSide Side,
    decimal Price,
    decimal Quantity,
    decimal? Fee,
    string? FeeAsset,
    bool? IsMaker,
    DateTimeOffset TimestampUtc,
    HistorySource Source = HistorySource.Exchange);

/// <summary>A bounded provider-history query.</summary>
public sealed record ExchangeHistoryQuery(string? Symbol = null, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, int Limit = 100);

/// <summary>Classifies lifecycle events observed by TradeRelay.</summary>
public enum TradingLifecycleKind { Control, Position, Order, Execution, Protection, Reconciliation, SafeError }

/// <summary>One safe, versioned lifecycle event observed by TradeRelay.</summary>
public sealed record TradingLifecycleEvent(
    int SchemaVersion,
    Guid EventId,
    string CorrelationId,
    DateTimeOffset TimestampUtc,
    ExchangeId Exchange,
    TradingEnvironment Environment,
    TradingLifecycleKind Kind,
    string Action,
    string Result,
    string? Symbol = null,
    TradeSide? Side = null,
    string? ExchangeOrderId = null,
    string? ClientOrderId = null,
    decimal? Quantity = null,
    decimal? Price = null,
    string? State = null,
    string? ErrorCode = null,
    string Source = "TradeRelayObserved");

/// <summary>A page of retained audit or lifecycle history.</summary>
public sealed record AuditHistoryPage(IReadOnlyList<AuditEvent> Events, int Page, int PageSize, bool HasMore, string? Warning);

/// <summary>A grouped, safe runtime error summary.</summary>
public sealed record RuntimeErrorSummary(
    string Code,
    string Category,
    string Provider,
    string AffectedAction,
    string? ExceptionType,
    int OccurrenceCount,
    DateTimeOffset FirstUtc,
    DateTimeOffset LastUtc,
    IReadOnlyList<string> CorrelationIds,
    IReadOnlyList<string> RelatedIds,
    string RecoveryGuidance);
