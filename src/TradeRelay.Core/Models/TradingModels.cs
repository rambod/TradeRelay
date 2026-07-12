namespace TradeRelay.Core.Models;

/// <summary>Identifies an exchange write action evaluated by the trading gate.</summary>
public enum TradingAction { ExecutePreparedOrder, CancelOrder, CancelAllOrders, ClosePosition, SetTradingStop }

/// <summary>Normalized exchange order state.</summary>
public enum ExchangeOrderStatus { Pending, New, PartiallyFilled, Filled, Cancelled, Rejected, Unknown }

/// <summary>Contains an exchange-neutral order submission request.</summary>
public sealed record ExchangeOrderRequest(string ClientOrderId, string Symbol, TradeSide Side, OrderType OrderType, decimal Quantity, decimal? LimitPrice, decimal? StopLoss, decimal? TakeProfit, bool ReduceOnly, string PositionMode = "0");

/// <summary>Contains the reconciled state of a submitted order.</summary>
public sealed record OrderSubmissionResult(bool Accepted, bool Confirmed, string? ExchangeOrderId, string ClientOrderId, ExchangeOrderStatus Status, decimal OriginalQuantity, decimal FilledQuantity, decimal RemainingQuantity, decimal? AverageFillPrice, string Message, DateTimeOffset TimestampUtc);

/// <summary>Contains a normalized exchange operation outcome.</summary>
public sealed record OperationResult(bool Success, string Code, string Message, int? AffectedCount, ExchangeOrderStatus? Status, DateTimeOffset TimestampUtc);

/// <summary>Requests a reduce-only position close.</summary>
public sealed record ClosePositionRequest(string Symbol, TradeSide PositionSide, decimal Quantity, string PositionMode);

/// <summary>Requests full-position stop-loss and optional take-profit protection.</summary>
public sealed record TradingStopRequest(string Symbol, TradeSide PositionSide, decimal StopLoss, decimal? TakeProfit, string PositionMode);

/// <summary>Reports a centralized trading-gate decision.</summary>
public sealed record TradingGateResult(bool Allowed, string Code, string Message);

/// <summary>Represents session-only Demo trading state.</summary>
public sealed record TradingSessionSnapshot(bool Enabled, bool Ready, string StateLabel, string? LastError, DateTimeOffset TimestampUtc);

