namespace TradeRelay.Core.Models;

/// <summary>Supported candle intervals.</summary>
public enum CandleInterval { OneMinute, ThreeMinutes, FiveMinutes, FifteenMinutes, ThirtyMinutes, OneHour, TwoHours, FourHours, SixHours, TwelveHours, OneDay, OneWeek }

/// <summary>Normalized order side.</summary>
public enum TradeSide { Buy, Sell }

/// <summary>Represents a ticker snapshot.</summary>
public sealed record TickerSnapshot(string Symbol, decimal LastPrice, decimal? BidPrice, decimal? AskPrice, decimal? HighPrice, decimal? LowPrice, decimal? Volume, DateTimeOffset TimestampUtc);

/// <summary>Represents one OHLCV candle.</summary>
public sealed record Candle(string Symbol, CandleInterval Interval, DateTimeOffset OpenTimeUtc, DateTimeOffset CloseTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>Represents normalized instrument metadata.</summary>
public sealed record InstrumentInfo(string Symbol, string Status, decimal TickSize, decimal QuantityStep, decimal MinimumQuantity, decimal MaximumQuantity, decimal? MaximumMarketQuantity, decimal? MinimumNotional, decimal? MaximumLeverage, string ContractType);

/// <summary>Represents one order-book level.</summary>
public sealed record OrderBookLevel(decimal Price, decimal Quantity);

/// <summary>Represents an order-book snapshot.</summary>
public sealed record OrderBookSnapshot(string Symbol, IReadOnlyList<OrderBookLevel> Bids, IReadOnlyList<OrderBookLevel> Asks, DateTimeOffset TimestampUtc);

/// <summary>Represents a unified account summary.</summary>
public sealed record AccountSummary(decimal TotalEquity, decimal AvailableBalance, decimal UnrealizedPnl, decimal? MarginUsagePercent, TradingEnvironment Environment, DateTimeOffset TimestampUtc);

/// <summary>Represents a normalized wallet balance.</summary>
public sealed record WalletBalance(string Asset, decimal Equity, decimal WalletBalanceAmount, decimal AvailableBalance, decimal UnrealizedPnl, decimal? UsdValue);

/// <summary>Represents a normalized open position.</summary>
public sealed record PositionSnapshot(string Symbol, TradeSide Side, decimal Size, decimal EntryPrice, decimal MarkPrice, decimal Leverage, decimal UnrealizedPnl, decimal? LiquidationPrice, decimal? StopLoss, decimal? TakeProfit, string PositionMode);

/// <summary>Represents a normalized open order.</summary>
public sealed record OrderSnapshot(string ExchangeOrderId, string? ClientOrderId, string Symbol, TradeSide Side, string Type, decimal? Price, decimal Quantity, decimal FilledQuantity, string Status, bool ReduceOnly, DateTimeOffset CreatedTimeUtc);

/// <summary>Represents non-secret API credential information.</summary>
public sealed record ApiCredentialInfo(bool IsReadOnly, bool HasTradingPermission, bool HasWalletPermission, bool HasWithdrawalPermission, bool IsIpBound, int? RemainingValidDays, DateTimeOffset? ExpiresAtUtc, bool IsMasterAccount, TradingEnvironment Environment, IReadOnlyList<string> Warnings)
{
    /// <summary>Gets a safe credential description.</summary>
    public string Summary => IsReadOnly ? "Read-only API key" : "Read/write API key";
}

/// <summary>Represents a provider connection result.</summary>
public sealed record ExchangeConnectionResult(bool Success, string Code, string Message, ApiCredentialInfo? CredentialInfo, ServiceHealthState RestHealth, ServiceHealthState StreamHealth);

/// <summary>Represents a normalized private order update.</summary>
public sealed record OrderUpdate(string Symbol, string ExchangeOrderId, string Status, DateTimeOffset TimestampUtc);

/// <summary>Represents a normalized private execution update.</summary>
public sealed record ExecutionUpdate(string Symbol, string ExchangeOrderId, decimal Price, decimal Quantity, DateTimeOffset TimestampUtc);

/// <summary>Represents a normalized private position update.</summary>
public sealed record PositionUpdate(string Symbol, TradeSide Side, decimal Size, DateTimeOffset TimestampUtc);

/// <summary>Represents a safe provider failure.</summary>
public sealed class ProviderException(string code, string message) : Exception(message)
{
    /// <summary>Gets the stable error code.</summary>
    public string Code { get; } = code;
}
