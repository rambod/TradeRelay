using TradeRelay.Core.Models;

namespace TradeRelay.Core.Providers;

/// <summary>Provides normalized public market data.</summary>
public interface IMarketDataProvider
{
    /// <summary>Gets a ticker.</summary>
    Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken);
    /// <summary>Gets candles.</summary>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken);
    /// <summary>Gets instrument metadata.</summary>
    Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken);
    /// <summary>Gets an order book.</summary>
    Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken);
}

/// <summary>Provides normalized authenticated account data.</summary>
public interface ITradingAccountProvider
{
    /// <summary>Gets the account summary.</summary>
    Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken);
    /// <summary>Gets wallet balances.</summary>
    Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken);
    /// <summary>Gets open positions.</summary>
    Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken);
    /// <summary>Gets open orders.</summary>
    Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken);
    /// <summary>Gets non-secret API-key information.</summary>
    Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken);
}

/// <summary>Provides normalized exchange write operations.</summary>
public interface IExchangeTradingProvider
{
    /// <summary>Places an already validated order.</summary>
    Task<OrderSubmissionResult> PlaceOrderAsync(ExchangeOrderRequest order, CancellationToken cancellationToken);
    /// <summary>Gets an order by exchange ID or client order ID.</summary>
    Task<OrderSubmissionResult?> GetOrderAsync(string symbol, string? exchangeOrderId, string? clientOrderId, CancellationToken cancellationToken);
    /// <summary>Cancels one explicit exchange order.</summary>
    Task<OperationResult> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken);
    /// <summary>Cancels active USDT-linear orders, optionally for one symbol.</summary>
    Task<OperationResult> CancelAllOrdersAsync(string? symbol, CancellationToken cancellationToken);
    /// <summary>Submits a reduce-only position close.</summary>
    Task<OrderSubmissionResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken);
    /// <summary>Sets full-position protection.</summary>
    Task<OperationResult> SetTradingStopAsync(TradingStopRequest request, CancellationToken cancellationToken);
}

/// <summary>Provides private exchange-stream state and normalized updates.</summary>
public interface IExchangeStream
{
    /// <summary>Gets whether the private stream is connected.</summary>
    bool IsConnected { get; }
    /// <summary>Raised when an order changes.</summary>
    event EventHandler<OrderUpdate>? OrderUpdated;
    /// <summary>Raised when an execution occurs.</summary>
    event EventHandler<ExecutionUpdate>? ExecutionUpdated;
    /// <summary>Raised when a position changes.</summary>
    event EventHandler<PositionUpdate>? PositionUpdated;
    /// <summary>Connects the private stream.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);
    /// <summary>Disconnects the private stream.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);
}

/// <summary>Creates one provider's public and authenticated services.</summary>
public interface IExchangeProviderFactory
{
    /// <summary>Gets the provider name.</summary>
    string ProviderName { get; }
    /// <summary>Gets provider metadata and capabilities.</summary>
    ExchangeProviderDescriptor Descriptor => new(
        new ExchangeId(ProviderName),
        ProviderName,
        ProviderCapabilities.MarketData | ProviderCapabilities.AccountRead | ProviderCapabilities.PrivateStream,
        [TradingEnvironment.Demo, TradingEnvironment.Live],
        [
            new CredentialFieldDescriptor(ExchangeCredentials.ApiKeyField, "API key", false),
            new CredentialFieldDescriptor(ExchangeCredentials.ApiSecretField, "API secret", true),
        ]);
    /// <summary>Creates public market data for an environment.</summary>
    IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment);
    /// <summary>Creates an authenticated provider connection.</summary>
    IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials);
}

/// <summary>Owns one authenticated provider connection.</summary>
public interface IExchangeProviderConnection : IAsyncDisposable
{
    /// <summary>Gets authenticated account data.</summary>
    ITradingAccountProvider Account { get; }
    /// <summary>Gets exchange write operations.</summary>
    IExchangeTradingProvider Trading { get; }
    /// <summary>Gets the private stream.</summary>
    IExchangeStream Stream { get; }
}
