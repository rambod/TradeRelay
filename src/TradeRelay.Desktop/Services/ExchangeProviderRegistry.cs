using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Desktop.Services;

internal sealed class ExchangeProviderRegistry : IExchangeProviderRegistry
{
    private readonly IReadOnlyDictionary<ExchangeId, IExchangeProviderFactory> _factories;

    public ExchangeProviderRegistry(IEnumerable<IExchangeProviderFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _factories = factories.ToDictionary(factory => factory.Descriptor.Id);
        Descriptors = _factories.Values.Select(factory => factory.Descriptor).OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<ExchangeProviderDescriptor> Descriptors { get; }

    public bool TryGetFactory(ExchangeId exchange, out IExchangeProviderFactory? factory) => _factories.TryGetValue(exchange, out factory);
}

internal sealed record ProviderSessionAccess(
    ExchangeProviderDescriptor Descriptor,
    TradingEnvironment Environment,
    IMarketDataProvider MarketData,
    ITradingAccountProvider? Account,
    IExchangeHistoryProvider? History,
    IExchangeStream? Stream,
    ProviderConnectionSnapshot Snapshot);

internal interface IExchangeSessionCoordinator
{
    event EventHandler<ProviderSessionAccess>? StateChanged;
    IReadOnlyList<ExchangeProfileKey> ConnectedProfiles { get; }
    IReadOnlyList<ProviderSessionAccess> Sessions { get; }
    bool TryResolve(string? exchange, out ProviderSessionAccess? session, out string code, out string message);
    Task<ExchangeConnectionResult> TestAsync(ExchangeId exchange, ExchangeCredentialSet credentials, CancellationToken cancellationToken);
    Task<ExchangeConnectionResult> SaveAsync(ExchangeId exchange, ExchangeCredentialSet credentials, bool remember, CancellationToken cancellationToken);
    Task DeleteAsync(ExchangeId exchange, CancellationToken cancellationToken);
    Task SelectAsync(ExchangeId exchange, CancellationToken cancellationToken);
}
