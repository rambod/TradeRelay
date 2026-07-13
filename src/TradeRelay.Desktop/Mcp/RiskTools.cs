using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class RiskTools(
    OrderPreparationService preparationService,
    PreparedOrderStore preparedOrderStore,
    AuditLogService audit,
    AppSettings settings,
    TimeProvider timeProvider)
{
    [McpServerTool(Name = "get_risk_settings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns the effective non-secret TradeRelay risk settings for the current environment.")]
    public ToolResult<RiskSettingsSnapshot> GetRiskSettings() => ToolResponse.Success(RiskSettingsSnapshot.Create(settings.Risk, settings.Bybit.Environment), "Risk settings retrieved.", settings.Bybit.Environment, timeProvider);

    [McpServerTool(Name = "calculate_position_size", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Calculates a risk-based, instrument-normalized position size. This never stores or places an order.")]
    public async Task<ToolResult<PositionSizeResult>> CalculatePositionSizeAsync(string symbol, decimal entryPrice, decimal stopLoss, decimal accountRiskPercent, CancellationToken cancellationToken)
    {
        try
        {
            PositionSizeResult result = await preparationService.CalculatePositionSizeAsync(symbol, entryPrice, stopLoss, accountRiskPercent, cancellationToken).ConfigureAwait(false);
            return ToolResponse.Result(result.Valid, result.Valid ? "OK" : "RISK_LIMIT_EXCEEDED", result.Valid ? "Position size calculated." : "Position size could not pass the current risk limits.", result, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Failure<PositionSizeResult>(exception.Code, exception.Message, settings.Bybit.Environment, timeProvider); }
    }

    [McpServerTool(Name = "validate_order", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Validates and normalizes a non-executable simulated USDT perpetual order without storing it.")]
    public async Task<ToolResult<OrderValidationResult>> ValidateOrderAsync(string symbol, string side, string orderType, decimal quantity, decimal? limitPrice = null, decimal? stopLoss = null, decimal? takeProfit = null, decimal? requestedLeverage = null, string? userNote = null, CancellationToken cancellationToken = default)
    {
        if (!TryParse(side, orderType, out TradeSide parsedSide, out OrderType parsedType)) return ToolResponse.Failure<OrderValidationResult>("VALIDATION_FAILED", "side must be Buy or Sell and orderType must be Market or Limit.", settings.Bybit.Environment, timeProvider);
        try
        {
            OrderValidationResult result = await preparationService.ValidateAsync(new PrepareOrderRequest(null, symbol, parsedSide, parsedType, quantity, limitPrice, stopLoss, takeProfit, requestedLeverage, userNote), cancellationToken).ConfigureAwait(false);
            return ToolResponse.Result(result.Valid, result.Valid ? "OK" : result.Errors.Any(IsRiskError) ? "RISK_LIMIT_EXCEEDED" : "VALIDATION_FAILED", result.Valid ? "Simulated order is valid and normalized." : "Simulated order failed validation.", result, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Failure<OrderValidationResult>(exception.Code, exception.Message, settings.Bybit.Environment, timeProvider); }
    }

    [McpServerTool(Name = "prepare_order", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Creates an expiring immutable local plan. This tool does not place an order; execution is a separate environment-aware gated operation.")]
    public async Task<ToolResult<PreparedOrderResult>> PrepareOrderAsync(string clientRequestId, string symbol, string side, string orderType, decimal quantity, decimal? limitPrice = null, decimal? stopLoss = null, decimal? takeProfit = null, decimal? requestedLeverage = null, string? userNote = null, CancellationToken cancellationToken = default)
    {
        string correlationId = ToolResponse.NewCorrelationId();
        if (!TryParse(side, orderType, out TradeSide parsedSide, out OrderType parsedType)) return ToolResponse.Correlated<PreparedOrderResult>(false, "VALIDATION_FAILED", "side must be Buy or Sell and orderType must be Market or Limit.", null, correlationId, settings.Bybit.Environment, timeProvider);
        try
        {
            PreparedOrderResult result = await preparationService.PrepareAsync(new PrepareOrderRequest(clientRequestId, symbol, parsedSide, parsedType, quantity, limitPrice, stopLoss, takeProfit, requestedLeverage, userNote), cancellationToken).ConfigureAwait(false);
            await audit.TryWriteAsync(audit.Create("prepare_order", "order_prepared", result.Success ? "OK" : "FAILED", settings.Bybit.Environment, correlationId, result.Order?.Order.Symbol ?? RiskEngine.NormalizeSymbol(symbol), result.Order?.PreparationId, result.Order?.ClientOrderId, approvalState: result.Order?.State.ToString(), errorCode: result.Success ? null : result.Code), cancellationToken).ConfigureAwait(false);
            return ToolResponse.Correlated(result.Success, result.Code, result.Message, result, correlationId, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Correlated<PreparedOrderResult>(false, exception.Code, exception.Message, null, correlationId, settings.Bybit.Environment, timeProvider); }
    }

    [McpServerTool(Name = "get_prepared_order", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gets one immutable prepared plan and its current approval or execution state by preparation ID.")]
    public ToolResult<PreparedOrder> GetPreparedOrder(string preparationId)
    {
        if (!Guid.TryParse(preparationId, out Guid id)) return ToolResponse.Failure<PreparedOrder>("VALIDATION_FAILED", "preparationId is invalid.", settings.Bybit.Environment, timeProvider);
        PreparedOrder? order = preparedOrderStore.Get(id);
        if (order is null) return ToolResponse.Failure<PreparedOrder>("VALIDATION_FAILED", "Prepared plan not found.", settings.Bybit.Environment, timeProvider);
        return ToolResponse.Result(order.State != PreparedOrderState.Expired, order.State == PreparedOrderState.Expired ? "ORDER_EXPIRED" : "OK", order.State == PreparedOrderState.Expired ? "Prepared plan has expired." : "Prepared plan retrieved.", order, settings.Bybit.Environment, timeProvider);
    }

    [McpServerTool(Name = "get_pending_approvals", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gets unexpired plans awaiting human approval. No credentials or internal tokens are returned.")]
    public ToolResult<IReadOnlyList<PreparedOrder>> GetPendingApprovals() => ToolResponse.Success(preparedOrderStore.GetPending(), "Pending approval plans retrieved.", settings.Bybit.Environment, timeProvider);

    private static bool TryParse(string side, string orderType, out TradeSide parsedSide, out OrderType parsedType)
    {
        bool sideValid = Enum.TryParse(side, true, out parsedSide) && Enum.IsDefined(parsedSide);
        bool typeValid = Enum.TryParse(orderType, true, out parsedType) && Enum.IsDefined(parsedType);
        return sideValid && typeValid;
    }
    private static bool IsRiskError(string error) => error.Contains("risk", StringComparison.OrdinalIgnoreCase) || error.Contains("notional", StringComparison.OrdinalIgnoreCase) || error.Contains("leverage", StringComparison.OrdinalIgnoreCase) || error.Contains("position", StringComparison.OrdinalIgnoreCase);
}
