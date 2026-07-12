using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class TradingTools(OrderExecutionService execution, AppSettings settings, TimeProvider timeProvider)
{
    [McpServerTool(Name = "execute_prepared_order", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Executes one approved, unexpired immutable plan against Bybit Demo exactly once and reconciles its state. Live is unavailable.")]
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
    [Description("Cancels one explicit Bybit Demo order ID and reconciles the result.")]
    public async Task<ToolResult<OperationResult>> CancelOrderAsync(string symbol, string exchangeOrderId, CancellationToken cancellationToken = default) => await RunOperationAsync((id, ct) => execution.CancelAsync(symbol, exchangeOrderId, id, ct), cancellationToken).ConfigureAwait(false);

    [McpServerTool(Name = "cancel_all_orders", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Cancels active Bybit Demo USDT-linear orders only after explicit user intent. confirm must be true; symbol is optional.")]
    public async Task<ToolResult<OperationResult>> CancelAllOrdersAsync(bool confirm, string? symbol = null, CancellationToken cancellationToken = default) => await RunOperationAsync((id, ct) => execution.CancelAllAsync(confirm, symbol, id, ct), cancellationToken).ConfigureAwait(false);

    [McpServerTool(Name = "close_position", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Closes all or part of one Bybit Demo position with a reduce-only market order; it cannot reverse the position.")]
    public async Task<ToolResult<OrderSubmissionResult>> ClosePositionAsync(string symbol, decimal? quantity = null, CancellationToken cancellationToken = default)
    {
        string correlationId = ToolResponse.NewCorrelationId();
        try
        {
            var result = await execution.CloseAsync(symbol, quantity, correlationId, cancellationToken).ConfigureAwait(false);
            return ToolResponse.Correlated(result.Success, result.Code, result.Message, result.Data, correlationId, settings.Bybit.Environment, timeProvider);
        }
        catch (ProviderException exception) { return ToolResponse.Correlated<OrderSubmissionResult>(false, exception.Code, exception.Message, null, correlationId, settings.Bybit.Environment, timeProvider); }
    }

    [McpServerTool(Name = "set_trading_stop", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Normalizes and applies full-position stop-loss and optional take-profit protection to one Bybit Demo position.")]
    public async Task<ToolResult<OperationResult>> SetTradingStopAsync(string symbol, decimal stopLoss, decimal? takeProfit = null, CancellationToken cancellationToken = default) => await RunOperationAsync((id, ct) => execution.SetTradingStopAsync(symbol, stopLoss, takeProfit, id, ct), cancellationToken).ConfigureAwait(false);

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

