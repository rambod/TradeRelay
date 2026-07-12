using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.Services;

internal sealed class OrderPreparationService(
    ExchangeConnectionManager connectionManager,
    RiskEngine riskEngine,
    PreparedOrderStore preparedOrderStore,
    AppSettings settings)
{
    public async Task<OrderValidationResult> ValidateAsync(PrepareOrderRequest request, CancellationToken cancellationToken)
    {
        ITradingAccountProvider accountProvider = connectionManager.Account ?? throw new ProviderException("CREDENTIALS_MISSING", "Load and validate exchange credentials before calculating account risk.");
        string symbol = RiskEngine.NormalizeSymbol(request.Symbol);
        Task<TickerSnapshot> tickerTask = connectionManager.MarketData.GetTickerAsync(symbol, cancellationToken);
        Task<InstrumentInfo> instrumentTask = connectionManager.MarketData.GetInstrumentInfoAsync(symbol, cancellationToken);
        Task<AccountSummary> accountTask = accountProvider.GetAccountSummaryAsync(cancellationToken);
        Task<IReadOnlyList<PositionSnapshot>> positionsTask = accountProvider.GetPositionsAsync(null, cancellationToken);
        await Task.WhenAll(tickerTask, instrumentTask, accountTask, positionsTask).ConfigureAwait(false);
        ApiCredentialInfo credential = connectionManager.Snapshot.CredentialInfo ?? throw new ProviderException("CREDENTIALS_MISSING", "Credential permission information is unavailable.");
        return riskEngine.ValidateOrder(request, settings.Risk, await instrumentTask.ConfigureAwait(false), await tickerTask.ConfigureAwait(false), await accountTask.ConfigureAwait(false), (await positionsTask.ConfigureAwait(false)).Count, credential);
    }

    public async Task<PreparedOrderResult> PrepareAsync(PrepareOrderRequest request, CancellationToken cancellationToken)
    {
        OrderValidationResult validation = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.Valid) return new(false, validation.Errors.Any(IsRiskError) ? "RISK_LIMIT_EXCEEDED" : "VALIDATION_FAILED", "The simulated order did not pass validation.", null);
        TradingEnvironment environment = connectionManager.Snapshot.Environment;
        return preparedOrderStore.Add(request.ClientRequestId, validation, environment, RiskSettingsSnapshot.Create(settings.Risk, environment));
    }

    public async Task<OrderValidationResult> RevalidateAsync(PreparedOrder prepared, CancellationToken cancellationToken)
    {
        RiskSettingsSnapshot snapshot = prepared.RiskSettings;
        var riskSettings = new RiskSettings
        {
            AllowedSymbols = new HashSet<string>(snapshot.AllowedSymbols, StringComparer.Ordinal),
            MaxRiskPerTradePercent = snapshot.MaxRiskPerTradePercent,
            MaxOrderNotionalUsd = snapshot.MaxOrderNotionalUsd,
            MaxOpenPositions = snapshot.MaxOpenPositions,
            MaxLeverage = snapshot.MaxLeverage,
            RequireStopLoss = snapshot.RequireStopLoss,
            RequireManualApprovalForDemo = snapshot.RequireManualApproval,
            RequireManualApprovalForLive = true,
            PreparedOrderExpirySeconds = snapshot.PreparedOrderExpirySeconds
        };
        NormalizedOrder order = prepared.Order;
        var request = new PrepareOrderRequest(prepared.ClientRequestId, order.Symbol, order.Side, order.OrderType, order.RequestedQuantity, order.RequestedLimitPrice, order.RequestedStopLoss, order.RequestedTakeProfit, order.Leverage, order.UserNote);
        ITradingAccountProvider accountProvider = connectionManager.Account ?? throw new ProviderException("CREDENTIALS_MISSING", "The authenticated account is unavailable.");
        Task<TickerSnapshot> tickerTask = connectionManager.MarketData.GetTickerAsync(order.Symbol, cancellationToken);
        Task<InstrumentInfo> instrumentTask = connectionManager.MarketData.GetInstrumentInfoAsync(order.Symbol, cancellationToken);
        Task<AccountSummary> accountTask = accountProvider.GetAccountSummaryAsync(cancellationToken);
        Task<IReadOnlyList<PositionSnapshot>> positionsTask = accountProvider.GetPositionsAsync(null, cancellationToken);
        await Task.WhenAll(tickerTask, instrumentTask, accountTask, positionsTask).ConfigureAwait(false);
        ApiCredentialInfo credential = connectionManager.Snapshot.CredentialInfo ?? throw new ProviderException("CREDENTIALS_MISSING", "Credential permission information is unavailable.");
        return riskEngine.ValidateOrder(request, riskSettings, await instrumentTask.ConfigureAwait(false), await tickerTask.ConfigureAwait(false), await accountTask.ConfigureAwait(false), (await positionsTask.ConfigureAwait(false)).Count, credential);
    }

    public async Task<PositionSizeResult> CalculatePositionSizeAsync(string symbol, decimal entryPrice, decimal stopLoss, decimal accountRiskPercent, CancellationToken cancellationToken)
    {
        ITradingAccountProvider accountProvider = connectionManager.Account ?? throw new ProviderException("CREDENTIALS_MISSING", "Load and validate exchange credentials before calculating position size.");
        symbol = RiskEngine.NormalizeSymbol(symbol);
        Task<InstrumentInfo> instrumentTask = connectionManager.MarketData.GetInstrumentInfoAsync(symbol, cancellationToken);
        Task<AccountSummary> accountTask = accountProvider.GetAccountSummaryAsync(cancellationToken);
        await Task.WhenAll(instrumentTask, accountTask).ConfigureAwait(false);
        InstrumentInfo instrument = await instrumentTask.ConfigureAwait(false);
        AccountSummary account = await accountTask.ConfigureAwait(false);
        return riskEngine.CalculatePositionSize(account.TotalEquity, entryPrice, stopLoss, accountRiskPercent, instrument.QuantityStep, instrument.MinimumQuantity, instrument.MaximumMarketQuantity ?? instrument.MaximumQuantity, settings.Risk);
    }

    private static bool IsRiskError(string error) => error.Contains("risk", StringComparison.OrdinalIgnoreCase) || error.Contains("notional", StringComparison.OrdinalIgnoreCase) || error.Contains("leverage", StringComparison.OrdinalIgnoreCase) || error.Contains("position", StringComparison.OrdinalIgnoreCase);
}
