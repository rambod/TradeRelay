using Microsoft.Extensions.Logging.Abstractions;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Security;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Security;
using TradeRelay.Desktop.Services;
using TradeRelay.Desktop.ViewModels;
using Xunit;

namespace TradeRelay.Tests;

public sealed class ExchangeSessionCoordinatorTests
{
    [Fact]
    public async Task ConcurrentReadOnlySessionsRemainIsolatedAndSelectedProviderResolves()
    {
        string root = Path.Combine(Path.GetTempPath(), "TradeRelay.MultiSessionTests", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings(); var store = new ApplicationSettingsStore(new ApplicationDataPaths(root)); var secrets = new SessionCredentialStore(); var credentialStore = new CredentialStoreCoordinator(secrets, secrets);
        var bybitFactory = new FakeFactory("bybit", [TradingEnvironment.Demo, TradingEnvironment.Live], write: true, passphrase: false);
        var bybit = new ExchangeConnectionManager(settings, store, credentialStore, bybitFactory, NullLogger<ExchangeConnectionManager>.Instance);
        var binance = new FakeFactory("binance", [TradingEnvironment.Live], write: false, passphrase: false);
        var kucoin = new FakeFactory("kucoin", [TradingEnvironment.Live], write: false, passphrase: true);
        var registry = new ExchangeProviderRegistry([bybitFactory, binance, kucoin]);
        var coordinator = new ExchangeSessionCoordinator(bybit, registry, settings, store, credentialStore, NullLogger<ExchangeSessionCoordinator>.Instance);

        ExchangeConnectionResult[] results = await Task.WhenAll(
            coordinator.SaveAsync(new ExchangeId("binance"), Credentials(false), false, default),
            coordinator.SaveAsync(new ExchangeId("kucoin"), Credentials(true), false, default));

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Contains(coordinator.ConnectedProfiles, profile => profile.Exchange == new ExchangeId("binance"));
        Assert.Contains(coordinator.ConnectedProfiles, profile => profile.Exchange == new ExchangeId("kucoin"));
        Assert.True(coordinator.TryResolve("binance", out ProviderSessionAccess? binanceSession, out _, out _));
        Assert.NotNull(binanceSession?.Account);
        Assert.False(binanceSession!.Descriptor.Capabilities.HasFlag(ProviderCapabilities.TradingWrite));

        await coordinator.DeleteAsync(new ExchangeId("binance"), default);
        Assert.True(coordinator.TryResolve("kucoin", out ProviderSessionAccess? kucoinSession, out _, out _));
        Assert.NotNull(kucoinSession?.Account);
        Assert.Null(coordinator.Sessions.Single(item => item.Descriptor.Id == new ExchangeId("binance")).Account);
        using var viewModel = new ProviderConnectionsViewModel(registry, coordinator, new ImmediateDispatcher());
        Assert.Equal(2, viewModel.CredentialFields.Count);
        viewModel.SelectedExchange = "kucoin";
        Assert.Equal([ExchangeCredentials.ApiKeyField, ExchangeCredentials.ApiSecretField, ExchangeCredentials.PassphraseField], viewModel.CredentialFields.Select(item => item.Name));
        await coordinator.SelectAsync(new ExchangeId("kucoin"), default);
        await coordinator.StopAsync(default);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static ExchangeCredentialSet Credentials(bool passphrase)
    {
        var values = new Dictionary<string, string> { [ExchangeCredentials.ApiKeyField] = "test-key", [ExchangeCredentials.ApiSecretField] = "test-secret" };
        if (passphrase) values[ExchangeCredentials.PassphraseField] = "test-passphrase";
        return new ExchangeCredentialSet(values);
    }

    private sealed class FakeFactory(string id, IReadOnlyList<TradingEnvironment> environments, bool write, bool passphrase) : IExchangeProviderFactory
    {
        public string ProviderName => id;
        public ExchangeProviderDescriptor Descriptor { get; } = new(new ExchangeId(id), char.ToUpperInvariant(id[0]) + id[1..], ProviderCapabilities.MarketData | ProviderCapabilities.AccountRead | ProviderCapabilities.PrivateStream | ProviderCapabilities.History | (write ? ProviderCapabilities.TradingWrite : ProviderCapabilities.None), environments, passphrase ? [new(ExchangeCredentials.ApiKeyField, "API key", false), new(ExchangeCredentials.ApiSecretField, "API secret", true), new(ExchangeCredentials.PassphraseField, "Passphrase", true)] : [new(ExchangeCredentials.ApiKeyField, "API key", false), new(ExchangeCredentials.ApiSecretField, "API secret", true)]);
        public IMarketDataProvider CreateMarketDataProvider(TradingEnvironment environment) => new FakeMarket();
        public IExchangeProviderConnection CreateConnection(TradingEnvironment environment, ExchangeCredentialSet credentials) { foreach (CredentialFieldDescriptor field in Descriptor.CredentialFields) _ = credentials[field.Name]; return new FakeConnection(environment); }
    }
    private sealed class FakeConnection(TradingEnvironment environment) : IExchangeProviderConnection
    {
        public ITradingAccountProvider Account { get; } = new FakeAccount(environment);
        public IExchangeTradingProvider Trading { get; } = new StubTradingProvider();
        public IExchangeStream Stream { get; } = new FakeStream();
        public IExchangeHistoryProvider? History { get; } = new FakeHistory();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeAccount(TradingEnvironment environment) : ITradingAccountProvider
    {
        public Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(new AccountSummary(1000m, 1000m, 0m, 0m, environment, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WalletBalance>>([]);
        public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PositionSnapshot>>([]);
        public Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderSnapshot>>([]);
        public Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new ApiCredentialInfo(true, false, true, false, true, null, null, false, environment, []));
    }
    private sealed class FakeMarket : IMarketDataProvider
    {
        public Task<TickerSnapshot> GetTickerAsync(string symbol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, CandleInterval interval, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstrumentInfo> GetInstrumentInfoAsync(string symbol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OrderBookSnapshot> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) => throw new NotSupportedException();
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
    private sealed class FakeHistory : IExchangeHistoryProvider
    {
        public Task<IReadOnlyList<HistoricalOrder>> GetOrderHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoricalOrder>>([]);
        public Task<IReadOnlyList<HistoricalExecution>> GetExecutionHistoryAsync(ExchangeHistoryQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoricalExecution>>([]);
    }
    private sealed class ImmediateDispatcher : IUiDispatcher { public void Post(Action action) => action(); }
}
