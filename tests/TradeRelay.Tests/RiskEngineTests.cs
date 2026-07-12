using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;
using Xunit;

namespace TradeRelay.Tests;

public sealed class RiskEngineTests
{
    private readonly RiskEngine _engine = new();

    [Fact]
    public void DecimalNormalization_IsExplicitAndConservative()
    {
        Assert.Equal(1.23m, DecimalNormalizer.RoundDownToStep(1.239m, .01m));
        Assert.Equal(1.24m, DecimalNormalizer.RoundToTick(1.235m, .01m));
        Assert.Equal(1.24m, DecimalNormalizer.RoundUpToStep(1.231m, .01m));
    }

    [Fact]
    public void ValidLongLimitOrder_NormalizesAndCalculatesRisk()
    {
        OrderValidationResult result = Validate(Request(TradeSide.Buy, limit: 100.04m, stop: 90.06m, takeProfit: 120.04m, quantity: 1.234m));

        Assert.True(result.Valid);
        Assert.Equal(1.23m, result.Order?.Quantity);
        Assert.Equal(100m, result.Order?.LimitPrice);
        Assert.Equal(90m, result.Order?.StopLoss);
        Assert.Equal(120m, result.Order?.TakeProfit);
        Assert.Equal(12.3m, result.Order?.Risk.EstimatedRiskUsd);
        Assert.Equal(2m, result.Order?.Risk.RiskRewardRatio);
    }

    [Theory]
    [InlineData(TradeSide.Buy, 110, false)]
    [InlineData(TradeSide.Buy, 90, true)]
    [InlineData(TradeSide.Sell, 110, true)]
    [InlineData(TradeSide.Sell, 90, false)]
    public void StopLossDirection_IsEnforced(TradeSide side, decimal stop, bool expectedValid)
    {
        OrderValidationResult result = Validate(Request(side, stop: stop, takeProfit: side == TradeSide.Buy ? 120m : 80m));
        Assert.Equal(expectedValid, result.Valid);
    }

    [Theory]
    [InlineData(TradeSide.Buy, 90, false)]
    [InlineData(TradeSide.Buy, 120, true)]
    [InlineData(TradeSide.Sell, 120, false)]
    [InlineData(TradeSide.Sell, 80, true)]
    public void TakeProfitDirection_IsEnforced(TradeSide side, decimal takeProfit, bool expectedValid)
    {
        OrderValidationResult result = Validate(Request(side, stop: side == TradeSide.Buy ? 90m : 110m, takeProfit: takeProfit));
        Assert.Equal(expectedValid, result.Valid);
    }

    [Fact]
    public void MissingOptionalStopLoss_ProducesUnknownRiskAndWarning()
    {
        RiskSettings settings = Settings();
        settings = Copy(settings, requireStopLoss: false);
        OrderValidationResult result = Validate(Request(TradeSide.Buy, stop: null), settings);

        Assert.True(result.Valid);
        Assert.Null(result.Order?.Risk.EstimatedRiskUsd);
        Assert.Null(result.Order?.Risk.AccountRiskPercent);
        Assert.Contains(result.Warnings, warning => warning.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingRequiredStopLoss_IsRejected()
    {
        OrderValidationResult result = Validate(Request(TradeSide.Buy, stop: null));
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, error => error.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RiskNotionalLeverageSymbolAndPositionLimits_AreEnforced()
    {
        RiskSettings settings = Copy(Settings(), maxNotional: 50m, maxLeverage: 2m, maxPositions: 1);
        OrderValidationResult result = _engine.ValidateOrder(
            Request(TradeSide.Buy, symbol: "DOGEUSDT", quantity: 2m, leverage: 3m), settings, Instrument(), Ticker(), Account(), 1, Credential());

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, error => error.Contains("allowed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("notional", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("leverage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("position", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InstrumentMinimumsAndMarketMaximum_AreEnforced()
    {
        OrderValidationResult tooSmall = Validate(Request(TradeSide.Buy, quantity: .001m), instrument: Instrument(minimumQuantity: .01m, minimumNotional: 5m));
        OrderValidationResult tooLargeMarket = Validate(Request(TradeSide.Buy, type: OrderType.Market, quantity: 6m), instrument: Instrument(maximumMarketQuantity: 5m));

        Assert.False(tooSmall.Valid);
        Assert.False(tooLargeMarket.Valid);
        Assert.Contains(tooLargeMarket.Errors, error => error.Contains("maximum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MarketEntry_UsesAskForBuyAndBidForSell()
    {
        Assert.Equal(101m, Validate(Request(TradeSide.Buy, type: OrderType.Market)).Order?.EstimatedEntryPrice);
        Assert.Equal(99m, Validate(Request(TradeSide.Sell, type: OrderType.Market, stop: 110m, takeProfit: 80m)).Order?.EstimatedEntryPrice);
    }

    [Fact]
    public void ZeroEquity_IsRejected()
    {
        OrderValidationResult result = Validate(Request(TradeSide.Buy), account: Account(equity: 0m));
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, error => error.Contains("equity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PositionSize_UsesRiskBudgetAndRoundsDown()
    {
        PositionSizeResult result = _engine.CalculatePositionSize(1000m, 100m, 90m, 0.25m, .01m, .01m, 10m, Settings());
        Assert.True(result.Valid);
        Assert.Equal(.25m, result.RawQuantity);
        Assert.Equal(.25m, result.NormalizedQuantity);
        Assert.Equal(25m, result.EstimatedNotionalUsd);
        Assert.Equal(2.5m, result.EstimatedRiskUsd);
    }

    [Fact]
    public void RiskSettingsValidation_RejectsInvalidRangesAndWarnsWithoutStop()
    {
        var settings = new RiskSettings { AllowedSymbols = [], MaxRiskPerTradePercent = 0m, MaxOrderNotionalUsd = 0m, MaxOpenPositions = 0, MaxLeverage = 0m, RequireStopLoss = false, PreparedOrderExpirySeconds = 10 };
        RiskSettingsValidationResult result = _engine.ValidateSettings(settings);
        Assert.False(result.Valid);
        Assert.NotEmpty(result.Errors);
        Assert.NotEmpty(result.Warnings);
    }

    private OrderValidationResult Validate(PrepareOrderRequest request, RiskSettings? settings = null, InstrumentInfo? instrument = null, AccountSummary? account = null) =>
        _engine.ValidateOrder(request, settings ?? Settings(), instrument ?? Instrument(), Ticker(), account ?? Account(), 0, Credential());

    private static PrepareOrderRequest Request(TradeSide side, string symbol = "BTCUSDT", OrderType type = OrderType.Limit, decimal quantity = 1m, decimal limit = 100m, decimal? stop = 90m, decimal? takeProfit = 120m, decimal? leverage = 1m) =>
        new("request-1", symbol, side, type, quantity, type == OrderType.Limit ? limit : null, stop, takeProfit, leverage, null);

    private static RiskSettings Settings() => new() { AllowedSymbols = new(["BTCUSDT"], StringComparer.Ordinal), MaxRiskPerTradePercent = 5m, MaxOrderNotionalUsd = 1000m, MaxOpenPositions = 2, MaxLeverage = 5m, PreparedOrderExpirySeconds = 120 };
    private static RiskSettings Copy(RiskSettings source, decimal? maxNotional = null, decimal? maxLeverage = null, int? maxPositions = null, bool? requireStopLoss = null) => new() { AllowedSymbols = new(source.AllowedSymbols, StringComparer.Ordinal), MaxRiskPerTradePercent = source.MaxRiskPerTradePercent, MaxOrderNotionalUsd = maxNotional ?? source.MaxOrderNotionalUsd, MaxOpenPositions = maxPositions ?? source.MaxOpenPositions, MaxLeverage = maxLeverage ?? source.MaxLeverage, RequireStopLoss = requireStopLoss ?? source.RequireStopLoss, RequireManualApprovalForDemo = source.RequireManualApprovalForDemo, RequireManualApprovalForLive = source.RequireManualApprovalForLive, PreparedOrderExpirySeconds = source.PreparedOrderExpirySeconds };
    private static InstrumentInfo Instrument(decimal minimumQuantity = .01m, decimal maximumMarketQuantity = 50m, decimal? minimumNotional = 1m) => new("BTCUSDT", "Trading", .1m, .01m, minimumQuantity, 100m, maximumMarketQuantity, minimumNotional, 100m, "LinearPerpetual");
    private static TickerSnapshot Ticker() => new("BTCUSDT", 100m, 99m, 101m, 110m, 90m, 1000m, DateTimeOffset.UtcNow);
    private static AccountSummary Account(decimal equity = 1000m) => new(equity, equity, 0m, 0m, TradingEnvironment.Demo, DateTimeOffset.UtcNow);
    private static ApiCredentialInfo Credential() => new(true, false, true, false, true, 30, null, false, TradingEnvironment.Demo, []);
}
