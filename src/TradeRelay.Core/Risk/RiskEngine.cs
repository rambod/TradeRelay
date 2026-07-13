using System.Text.RegularExpressions;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;

namespace TradeRelay.Core.Risk;

/// <summary>Validates and normalizes simulated linear USDT perpetual orders.</summary>
public sealed partial class RiskEngine
{
    /// <summary>Validates risk settings before persistence.</summary>
    public RiskSettingsValidationResult ValidateSettings(RiskSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();
        var warnings = new List<string>();
        string[] symbols = settings.AllowedSymbols.Select(NormalizeSymbol).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (symbols.Length == 0 || symbols.Any(symbol => !SymbolPattern().IsMatch(symbol))) errors.Add("Add at least one valid USDT perpetual symbol, one per line.");
        if (settings.MaxRiskPerTradePercent is <= 0m or > 100m) errors.Add("Maximum risk per trade must be greater than 0 and no more than 100 percent.");
        if (settings.MaxOrderNotionalUsd <= 0m) errors.Add("Maximum order notional must be greater than 0.");
        if (settings.MaxOpenPositions is < 1 or > 100) errors.Add("Maximum open positions must be between 1 and 100.");
        if (settings.MaxLeverage <= 0m) errors.Add("Maximum leverage must be greater than 0.");
        if (settings.MaxMarketPriceDeviationPercent is <= 0m or > 10m) errors.Add("Maximum Live market-price deviation must be greater than 0 and no more than 10 percent.");
        if (settings.PreparedOrderExpirySeconds is < 30 or > 3600) errors.Add("Prepared-order expiration must be between 30 and 3600 seconds.");
        if (!settings.RequireStopLoss) warnings.Add("Stop loss is optional. Plans without one have unknown estimated risk.");
        return new(errors.Count == 0, errors, warnings);
    }

    /// <summary>Validates and normalizes a simulated order.</summary>
    public OrderValidationResult ValidateOrder(
        PrepareOrderRequest request,
        RiskSettings settings,
        InstrumentInfo instrument,
        TickerSnapshot ticker,
        AccountSummary account,
        int openPositionCount,
        ApiCredentialInfo credentialInfo)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        var warnings = new List<string>();
        string symbol = NormalizeSymbol(request.Symbol);
        if (!SymbolPattern().IsMatch(symbol)) errors.Add("Symbol must be a valid USDT perpetual symbol.");
        if (!settings.AllowedSymbols.Contains(symbol)) errors.Add($"{symbol} is not in the allowed-symbol list.");
        if (!string.Equals(instrument.Status, "Trading", StringComparison.OrdinalIgnoreCase)) errors.Add("The instrument is not currently trading.");
        if (request.Quantity <= 0m) errors.Add("Quantity must be greater than 0.");
        if (!Enum.IsDefined(request.Side)) errors.Add("Order side is invalid.");
        if (!Enum.IsDefined(request.OrderType)) errors.Add("Order type is invalid.");
        if (instrument.TickSize <= 0m || instrument.QuantityStep <= 0m) errors.Add("Instrument precision is unavailable.");
        if (openPositionCount >= settings.MaxOpenPositions) errors.Add("The maximum open-position count has been reached.");

        decimal quantity = request.Quantity > 0m && instrument.QuantityStep > 0m ? DecimalNormalizer.RoundDownToStep(request.Quantity, instrument.QuantityStep) : 0m;
        if (quantity != request.Quantity) warnings.Add($"Quantity was rounded down from {request.Quantity} to {quantity}.");
        decimal maximumQuantity = request.OrderType == OrderType.Market ? instrument.MaximumMarketQuantity ?? instrument.MaximumQuantity : instrument.MaximumQuantity;
        if (quantity < instrument.MinimumQuantity) errors.Add($"Normalized quantity is below the instrument minimum of {instrument.MinimumQuantity}.");
        if (quantity > maximumQuantity) errors.Add($"Normalized quantity exceeds the instrument maximum of {maximumQuantity}.");

        decimal? limitPrice = null;
        decimal entryPrice = 0m;
        if (request.OrderType == OrderType.Limit)
        {
            if (request.LimitPrice is null or <= 0m) errors.Add("A positive limit price is required for a limit order.");
            else if (instrument.TickSize > 0m)
            {
                limitPrice = DecimalNormalizer.RoundToTick(request.LimitPrice.Value, instrument.TickSize);
                entryPrice = limitPrice.Value;
                if (limitPrice != request.LimitPrice) warnings.Add($"Limit price was normalized from {request.LimitPrice} to {limitPrice}.");
            }
        }
        else if (instrument.TickSize > 0m)
        {
            decimal marketPrice = request.Side == TradeSide.Buy ? ticker.AskPrice ?? ticker.LastPrice : ticker.BidPrice ?? ticker.LastPrice;
            entryPrice = DecimalNormalizer.RoundToTick(marketPrice, instrument.TickSize);
        }

        decimal? stopLoss = null;
        if (request.StopLoss is null or <= 0m)
        {
            if (settings.RequireStopLoss) errors.Add("A positive stop loss is required by the current risk settings.");
            else warnings.Add("No stop loss was provided. Estimated risk and account-risk percentage are unknown.");
        }
        else if (instrument.TickSize > 0m)
        {
            stopLoss = request.Side == TradeSide.Buy
                ? DecimalNormalizer.RoundDownToStep(request.StopLoss.Value, instrument.TickSize)
                : DecimalNormalizer.RoundUpToStep(request.StopLoss.Value, instrument.TickSize);
            if (stopLoss != request.StopLoss) warnings.Add($"Stop loss was conservatively normalized from {request.StopLoss} to {stopLoss}.");
        }

        decimal? takeProfit = request.TakeProfit is > 0m && instrument.TickSize > 0m
            ? DecimalNormalizer.RoundToTick(request.TakeProfit.Value, instrument.TickSize)
            : null;
        if (request.TakeProfit is <= 0m) errors.Add("Take profit must be positive when provided.");
        if (takeProfit != request.TakeProfit && request.TakeProfit is not null) warnings.Add($"Take profit was normalized from {request.TakeProfit} to {takeProfit}.");

        if (entryPrice > 0m && stopLoss is not null && (request.Side == TradeSide.Buy ? stopLoss >= entryPrice : stopLoss <= entryPrice))
            errors.Add(request.Side == TradeSide.Buy ? "A Buy stop loss must be below entry." : "A Sell stop loss must be above entry.");
        if (entryPrice > 0m && takeProfit is not null && (request.Side == TradeSide.Buy ? takeProfit <= entryPrice : takeProfit >= entryPrice))
            errors.Add(request.Side == TradeSide.Buy ? "A Buy take profit must be above entry." : "A Sell take profit must be below entry.");

        decimal leverage = request.RequestedLeverage ?? 1m;
        if (leverage <= 0m) errors.Add("Leverage must be greater than 0.");
        if (leverage > settings.MaxLeverage) errors.Add($"Requested leverage exceeds the configured maximum of {settings.MaxLeverage}.");
        if (instrument.MaximumLeverage is not null && leverage > instrument.MaximumLeverage) errors.Add($"Requested leverage exceeds the instrument maximum of {instrument.MaximumLeverage}.");

        decimal notional = entryPrice * quantity;
        if (notional > settings.MaxOrderNotionalUsd) errors.Add($"Estimated notional exceeds the configured maximum of {settings.MaxOrderNotionalUsd} USD.");
        if (instrument.MinimumNotional is not null && notional < instrument.MinimumNotional) errors.Add($"Estimated notional is below the instrument minimum of {instrument.MinimumNotional} USD.");
        if (account.TotalEquity <= 0m) errors.Add("Account equity must be positive for risk validation.");

        decimal? estimatedRisk = stopLoss is null ? null : decimal.Abs(entryPrice - stopLoss.Value) * quantity;
        decimal? accountRisk = estimatedRisk is null || account.TotalEquity <= 0m ? null : estimatedRisk.Value / account.TotalEquity * 100m;
        if (accountRisk > settings.MaxRiskPerTradePercent) errors.Add($"Estimated account risk exceeds the configured maximum of {settings.MaxRiskPerTradePercent} percent.");
        decimal? reward = takeProfit is null ? null : decimal.Abs(takeProfit.Value - entryPrice) * quantity;
        decimal? ratio = estimatedRisk is > 0m && reward is not null ? reward.Value / estimatedRisk.Value : null;

        if (!credentialInfo.HasTradingPermission) warnings.Add("The loaded API key cannot trade. This plan remains a read-only simulation.");
        if (account.Environment == TradingEnvironment.Live) warnings.Add("Live plan: execution requires explicit session enablement and the configured approval policy.");
        else warnings.Add("Demo plan: execution still requires explicit session enablement and the centralized trading gate.");

        var normalized = new NormalizedOrder(symbol, request.Side, request.OrderType, request.Quantity, quantity, request.LimitPrice, limitPrice, entryPrice, request.StopLoss, stopLoss, request.TakeProfit, takeProfit, leverage, new RiskEstimate(notional, estimatedRisk, reward, ratio, accountRisk), NormalizeNote(request.UserNote));
        return new(errors.Count == 0, normalized, errors, warnings);
    }

    /// <summary>Calculates a normalized quantity from an account-risk percentage.</summary>
    public PositionSizeResult CalculatePositionSize(decimal accountEquity, decimal entryPrice, decimal stopLoss, decimal accountRiskPercent, decimal quantityStep, decimal minimumQuantity, decimal maximumQuantity, RiskSettings settings)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (accountEquity <= 0m) errors.Add("Account equity must be greater than 0.");
        if (entryPrice <= 0m) errors.Add("Entry price must be greater than 0.");
        if (stopLoss <= 0m || stopLoss == entryPrice) errors.Add("Stop loss must be positive and different from entry price.");
        if (accountRiskPercent <= 0m || accountRiskPercent > settings.MaxRiskPerTradePercent) errors.Add($"Account risk percent must be greater than 0 and no more than {settings.MaxRiskPerTradePercent}.");
        if (quantityStep <= 0m) errors.Add("Instrument quantity precision is unavailable.");
        if (errors.Count > 0) return new(false, 0m, 0m, 0m, 0m, accountRiskPercent, errors, warnings);
        decimal riskBudget = accountEquity * accountRiskPercent / 100m;
        decimal raw = riskBudget / decimal.Abs(entryPrice - stopLoss);
        decimal normalized = DecimalNormalizer.RoundDownToStep(raw, quantityStep);
        if (normalized < minimumQuantity) errors.Add($"Calculated quantity is below the instrument minimum of {minimumQuantity}.");
        if (normalized > maximumQuantity) errors.Add($"Calculated quantity exceeds the instrument maximum of {maximumQuantity}.");
        decimal notional = normalized * entryPrice;
        if (notional > settings.MaxOrderNotionalUsd) errors.Add($"Calculated notional exceeds the configured maximum of {settings.MaxOrderNotionalUsd} USD.");
        if (normalized != raw) warnings.Add($"Quantity was rounded down from {raw} to {normalized}.");
        return new(errors.Count == 0, raw, normalized, notional, decimal.Abs(entryPrice - stopLoss) * normalized, accountRiskPercent, errors, warnings);
    }

    /// <summary>Normalizes an allowed symbol.</summary>
    public static string NormalizeSymbol(string? symbol) => symbol?.Trim().ToUpperInvariant() ?? string.Empty;
    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    [GeneratedRegex("^[A-Z0-9]{1,26}USDT$", RegexOptions.CultureInvariant)] private static partial Regex SymbolPattern();
}
