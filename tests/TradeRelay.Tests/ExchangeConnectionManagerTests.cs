using System.Text.Json;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using Xunit;

namespace TradeRelay.Tests;

public sealed class ExchangeConnectionManagerTests
{
    [Fact]
    public async Task SaveAsync_RejectsWithdrawalPermissionWithoutRetainingConnection()
    {
        var factory = new FakeFactory(CredentialInfo(withdrawal: true));
        await using TestServerContext context = TestServerContext.Create(providerFactory: factory);

        ExchangeConnectionResult result = await context.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "demo-key", "demo-secret", false, default);

        Assert.False(result.Success);
        Assert.Equal("UNSAFE_API_PERMISSION", result.Code);
        Assert.False(context.ConnectionManager.Snapshot.CredentialLoaded);
        Assert.True(factory.LastConnection?.Disposed);
    }

    [Fact]
    public async Task SaveAndDelete_ExposeOnlyMaskedSafeConnectionState()
    {
        var factory = new FakeFactory(CredentialInfo(withdrawal: false));
        await using TestServerContext context = TestServerContext.Create(providerFactory: factory);

        ExchangeConnectionResult result = await context.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "very-secret-api-key", "very-secret-api-secret", false, default);
        string json = JsonSerializer.Serialize(context.ConnectionManager.Snapshot);

        Assert.True(result.Success);
        Assert.Equal(ServiceHealthState.Healthy, context.ConnectionManager.Snapshot.RestHealth);
        Assert.Equal("••••••-key", context.ConnectionManager.Snapshot.SavedKeyPreview);
        Assert.DoesNotContain("very-secret-api-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret-api-secret", json, StringComparison.Ordinal);
        Assert.False(factory.LastConnection?.Disposed);

        await context.ConnectionManager.DeleteAsync(default);
        Assert.True(factory.LastConnection?.Disposed);
        Assert.False(context.ConnectionManager.Snapshot.CredentialLoaded);
    }

    [Fact]
    public async Task TestAsync_DisposesTemporaryConnectionAndReportsStreamHealth()
    {
        var factory = new FakeFactory(CredentialInfo(withdrawal: false));
        await using TestServerContext context = TestServerContext.Create(providerFactory: factory);

        ExchangeConnectionResult result = await context.ConnectionManager.TestAsync(TradingEnvironment.Demo, "key", "secret", default);

        Assert.True(result.Success);
        Assert.Equal(ServiceHealthState.Healthy, result.StreamHealth);
        Assert.True(factory.LastConnection?.Disposed);
        Assert.False(context.ConnectionManager.Snapshot.CredentialLoaded);
    }

    private static ApiCredentialInfo CredentialInfo(bool withdrawal) => new(true, true, true, withdrawal, true, 30, DateTimeOffset.UtcNow.AddDays(30), false, TradingEnvironment.Demo, withdrawal ? ["Withdrawal permission is unsafe."] : []);

    private sealed class FakeFactory(ApiCredentialInfo info) : IExchangeProviderFactory
    {
        public string ProviderName => "Bybit";
        public FakeConnection? LastConnection { get; private set; }
        public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment) => new FakeMarketData();
        public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials) => LastConnection = new FakeConnection(info);
    }

    private sealed class FakeConnection(ApiCredentialInfo info) : IExchangeProviderConnection
    {
        public bool Disposed { get; private set; }
        public ITradingAccountProvider Account { get; } = new FakeAccount(info);
        public IExchangeTradingProvider Trading { get; } = new StubTradingProvider();
        public IExchangeStream Stream { get; } = new FakeStream();
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeAccount(ApiCredentialInfo info) : ITradingAccountProvider
    {
        public Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(new AccountSummary(1000, 900, 10, 5, info.Environment, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WalletBalance>>([]);
        public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PositionSnapshot>>([]);
        public Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderSnapshot>>([]);
        public Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken) => Task.FromResult(info);
    }

    private sealed class FakeStream : IExchangeStream
    {
        public bool IsConnected { get; private set; }
        public event EventHandler<OrderUpdate>? OrderUpdated { add { } remove { } }
        public event EventHandler<ExecutionUpdate>? ExecutionUpdated { add { } remove { } }
        public event EventHandler<PositionUpdate>? PositionUpdated { add { } remove { } }
        public Task ConnectAsync(CancellationToken cancellationToken) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }
    }

    private sealed class FakeMarketData : IMarketDataProvider
    {
        public Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(new TickerSnapshot(symbol, 1, 1, 1, 1, 1, 1, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Candle>>([]);
        public Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(new InstrumentInfo(symbol, "Trading", .1m, .001m, .001m, 100m, 50m, 5m, 100m, "LinearPerpetual"));
        public Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) => Task.FromResult(new OrderBookSnapshot(symbol, [], [], DateTimeOffset.UtcNow));
    }
}
