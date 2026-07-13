namespace TradeRelay.Core.Models;

/// <summary>Represents one non-secret append-only activity event.</summary>
public sealed record AuditEvent(Guid EventId, string CorrelationId, DateTimeOffset TimestampUtc, TradingEnvironment Environment, string Tool, string Action, string Result, string? Symbol = null, Guid? PreparationId = null, string? ClientOrderId = null, string? ExchangeOrderId = null, string? ApprovalState = null, string? RiskSummary = null, string? ProviderResult = null, string? FinalStatus = null, string? ErrorCode = null, int SchemaVersion = 1, string Exchange = "bybit", string Source = "TradeRelayObserved");

/// <summary>Reports current append-only audit health.</summary>
public sealed record AuditHealthSnapshot(bool Healthy, string? LastError, DateTimeOffset TimestampUtc);
