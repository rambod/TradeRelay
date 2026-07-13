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

internal interface IExchangeSessionCoordinator
{
    IReadOnlyList<ExchangeProfileKey> ConnectedProfiles { get; }
    bool TryGet(ExchangeProfileKey profile, out ExchangeConnectionManager? manager);
}

internal sealed class ExchangeSessionCoordinator(ExchangeConnectionManager bybit) : IExchangeSessionCoordinator
{
    public IReadOnlyList<ExchangeProfileKey> ConnectedProfiles => bybit.Snapshot.CredentialLoaded
        ? [new ExchangeProfileKey(new ExchangeId("bybit"), bybit.Snapshot.Environment)]
        : [];

    public bool TryGet(ExchangeProfileKey profile, out ExchangeConnectionManager? manager)
    {
        bool found = profile.Exchange == new ExchangeId("bybit") && profile.Environment == bybit.Snapshot.Environment;
        manager = found ? bybit : null;
        return found;
    }
}
