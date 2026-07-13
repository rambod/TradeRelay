using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class TradingTools(
    OrderExecutionService execution,
    LiveActionConfirmationStore liveConfirmations,
    AppSettings settings,
    TimeProvider timeProvider)
{
    [McpServerTool(Name = "execute_prepared_order", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Executes one approved, unexpired immutable plan against the selected Bybit environment exactly once and reconciles its state. Live additionally requires explicit desktop session enablement.")]
    public async Task<ToolResult<OrderSubmissionResult>> ExecutePreparedOrderAsync(string preparationId, CancellationToken cancellationToken = default)
    {
        string correlationId = ToolResponse.NewCorrelationId();
        if (!Guid.TryParse(preparationId, out Guid id)) return ToolResponse.Correlated<OrderSubmissionResult>(false, "VALIDATION_FAILED", "preparationId is invalid.", null, correlationId, settings.Bybit.Environment, timeProvider);
        try
        {
            var result = await execution.ExecuteAsync(id, correlationId, cancellationToken).ConfigureAwait(false);
            return ToolResponse.Correlated(result.Success, result.Code, result.Message, result.Data, correlationId, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Correlated<OrderSubmissionResult>(false, exception.Code, exception.Message, null, correlationId, settings.Bybit.Environment, timeProvider); }
    }

    [McpServerTool(Name = "cancel_order", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Cancels one explicit Bybit order ID in the selected enabled environment and reconciles the result.")]
    public async Task<ToolResult<OperationResult>> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken = default) => await RunOperationAsync((id, ct) => execution.CancelAsync(symbol, exchangeOrderId, id, ct), cancellationToken).ConfigureAwait(false);

    [McpServerTool(Name = "cancel_all_orders", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Cancels active USDT-linear orders only after explicit user intent. confirm must be true. Live requires a clientRequestId and a separately approved liveConfirmationId.")]
    public async Task<ToolResult<LiveActionOutcome<OperationResult>>> CancelAllOrdersAsync(
        bool confirm,
        string? symbol = null,
        string? clientRequestId = null,
        string? liveConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = ToolResponse.NewCorrelationId();
        try
        {
            var result = await execution.CancelAllAsync(confirm, symbol, clientRequestId, liveConfirmationId, correlationId, cancellationToken).ConfigureAwait(false);
            return ToolResponse.Correlated(result.Success, result.Code, result.Message, result.Data, correlationId, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Correlated<LiveActionOutcome<OperationResult>>(false, exception.Code, exception.Message, null, correlationId, settings.Bybit.Environment, timeProvider); }
    }

    [McpServerTool(Name = "close_position", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Closes all or part of one Bybit position with a reduce-only market order. Live requires a clientRequestId and a separately approved liveConfirmationId.")]
    public async Task<ToolResult<LiveActionOutcome<OrderSubmissionResult>>> ClosePositionAsync(
        string symbol,
        decimal? quantity = null,
        string? clientRequestId = null,
        string? liveConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = ToolResponse.NewCorrelationId();
        try
        {
            var result = await execution.CloseAsync(symbol, quantity, clientRequestId, liveConfirmationId, correlationId, cancellationToken).ConfigureAwait(false);
            return ToolResponse.Correlated(result.Success, result.Code, result.Message, result.Data, correlationId, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Correlated<LiveActionOutcome<OrderSubmissionResult>>(false, exception.Code, exception.Message, null, correlationId, settings.Bybit.Environment, timeProvider); }
    }

    [McpServerTool(Name = "set_trading_stop", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Normalizes and applies full-position stop-loss and optional take-profit protection to one Bybit position in the selected enabled environment.")]
    public async Task<ToolResult<OperationResult>> SetTradingStopAsync(string symbol, decimal stopLoss, decimal? takeProfit = null, CancellationToken cancellationToken = default) => await RunOperationAsync((id, ct) => execution.SetTradingStopAsync(symbol, stopLoss, takeProfit, id, ct), cancellationToken).ConfigureAwait(false);

    [McpServerTool(Name = "get_live_action_confirmation", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gets one non-secret Live action confirmation by ID without approving or executing it.")]
    public ToolResult<LiveActionConfirmation> GetLiveActionConfirmation(string liveConfirmationId)
    {
        if (!Guid.TryParse(liveConfirmationId, out Guid id)) return ToolResponse.Failure<LiveActionConfirmation>("VALIDATION_FAILED", "liveConfirmationId is invalid.", settings.Bybit.Environment, timeProvider);
        LiveActionConfirmation? confirmation = liveConfirmations.Get(id);
        return confirmation is null
            ? ToolResponse.Failure<LiveActionConfirmation>("VALIDATION_FAILED", "Live action confirmation not found.", settings.Bybit.Environment, timeProvider)
            : ToolResponse.Success(confirmation, "Live action confirmation retrieved.", settings.Bybit.Environment, timeProvider);
    }

    [McpServerTool(Name = "get_pending_live_confirmations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gets unexpired destructive Live actions awaiting desktop approval. No credentials or local authentication data are returned.")]
    public ToolResult<IReadOnlyList<LiveActionConfirmation>> GetPendingLiveConfirmations() =>
        ToolResponse.Success(liveConfirmations.GetPending(), "Pending Live action confirmations retrieved.", settings.Bybit.Environment, timeProvider);

    private async Task<ToolResult<OperationResult>> RunOperationAsync(Func<string, CancellationToken, Task<(bool Success, string Code, string Message, OperationResult? Data)>> action, CancellationToken cancellationToken)
    {
        string correlationId = ToolResponse.NewCorrelationId();
        try
        {
            var result = await action(correlationId, cancellationToken).ConfigureAwait(false);
            return ToolResponse.Correlated(result.Success, result.Code, result.Message, result.Data, correlationId, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Correlated<OperationResult>(false, exception.Code, exception.Message, null, correlationId, settings.Bybit.Environment, timeProvider); }
    }
}
