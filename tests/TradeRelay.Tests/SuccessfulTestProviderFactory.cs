using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Tests;

internal sealed class SuccessfulTestProviderFactory : IExchangeProviderFactory
{
    public string ProviderName => "Bybit";
    public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment) => new MarketData();
    public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentials credentials) => new Connection(environment);

    private sealed class Connection(TradingEnvironment environment) : IExchangeProviderConnection
    {
        public ITradingAccountProvider Account { get; } = new Account(environment);
        public IExchangeStream Stream { get; } = new Stream();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Account(TradingEnvironment environment) : ITradingAccountProvider
    {
        public Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(new AccountSummary(1000m, 900m, 10m, 5m, environment, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WalletBalance>>([new("USDT", 1000m, 1000m, 900m, 10m, 1000m)]);
        public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PositionSnapshot>>([]);
        public Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderSnapshot>>([]);
        public Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new ApiCredentialInfo(true, true, true, false, true, 30, DateTimeOffset.UtcNow.AddDays(30), false, environment, []));
    }

    private sealed class Stream : IExchangeStream
    {
        public bool IsConnected { get; private set; }
        public event EventHandler<OrderUpdate>? OrderUpdated { add { } remove { } }
        public event EventHandler<ExecutionUpdate>? ExecutionUpdated { add { } remove { } }
        public event EventHandler<PositionUpdate>? PositionUpdated { add { } remove { } }
        public Task ConnectAsync(CancellationToken cancellationToken) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }
    }

    private sealed class MarketData : IMarketDataProvider
    {
        public Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(new TickerSnapshot(symbol.ToUpperInvariant(), 100m, 99m, 101m, 110m, 90m, 1000m, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Candle>>([new(symbol.ToUpperInvariant(), interval, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, 99m, 101m, 98m, 100m, 10m)]);
        public Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(new InstrumentInfo(symbol.ToUpperInvariant(), "Trading", .1m, .001m, .001m, 100m, 5m, 100m, "LinearPerpetual"));
        public Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) => Task.FromResult(new OrderBookSnapshot(symbol.ToUpperInvariant(), [new(99m, 1m)], [new(101m, 1m)], DateTimeOffset.UtcNow));
    }
}
