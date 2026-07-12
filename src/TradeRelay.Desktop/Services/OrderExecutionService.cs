using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Risk;

namespace TradeRelay.Desktop.Services;

internal sealed class OrderExecutionService(
    ExchangeConnectionManager connections,
    TradingGate gate,
    OrderPreparationService preparation,
    PreparedOrderStore store,
    AuditLogService audit,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(5);

    public async Task<(bool Success, string Code, string Message, OrderSubmissionResult? Data)> ExecuteAsync(Guid preparationId, string correlationId, CancellationToken cancellationToken)
    {
        PreparedOrder? current = store.Get(preparationId);
        if (current is null) return Failure<OrderSubmissionResult>("VALIDATION_FAILED", "Prepared order not found.");
        TradingGateResult allowed = gate.Check(TradingAction.ExecutePreparedOrder, current);
        if (!allowed.Allowed) { await AuditDeniedAsync("execute_prepared_order", current.Order.Symbol, allowed, correlationId, preparationId, cancellationToken).ConfigureAwait(false); return Failure<OrderSubmissionResult>(allowed.Code, allowed.Message); }
        await audit.WriteRequiredAsync(audit.Create("execute_prepared_order", "execution_requested", "STARTED", current.Environment, correlationId, current.Order.Symbol, current.PreparationId, current.ClientOrderId, approvalState: current.State.ToString(), riskSummary: Risk(current)), cancellationToken).ConfigureAwait(false);

        PreparedOrderResult claim = store.BeginExecution(preparationId, current.ImmutableHash);
        if (!claim.Success || claim.Order is null) return Failure<OrderSubmissionResult>(claim.Code, claim.Message);
        current = claim.Order;
        try
        {
            OrderValidationResult validation = await preparation.RevalidateAsync(current, cancellationToken).ConfigureAwait(false);
            if (!validation.Valid || validation.Order is null)
            {
                store.FailExecution(preparationId, null, "Execution revalidation failed.");
                await AuditFinalAsync("execute_prepared_order", "FAILED", current, correlationId, null, "RISK_LIMIT_EXCEEDED", cancellationToken).ConfigureAwait(false);
                return Failure<OrderSubmissionResult>("RISK_LIMIT_EXCEEDED", "Current account or market state no longer passes the prepared risk plan.");
            }
            NormalizedOrder order = validation.Order;
            var request = new ExchangeOrderRequest(current.ClientOrderId, order.Symbol, order.Side, order.OrderType, order.Quantity, order.LimitPrice, order.StopLoss, order.TakeProfit, false);
            IExchangeTradingProvider provider = connections.Trading!;
            OrderSubmissionResult acknowledgement;
            try { acknowledgement = await provider.PlaceOrderAsync(request, cancellationToken).ConfigureAwait(false); }
            catch (ProviderException)
            {
                OrderSubmissionResult? ambiguous = await provider.GetOrderAsync(order.Symbol, null, current.ClientOrderId, cancellationToken).ConfigureAwait(false);
                if (ambiguous is null)
                {
                    var unknown = new OrderSubmissionResult(false, false, null, current.ClientOrderId, ExchangeOrderStatus.Unknown, order.Quantity, 0m, order.Quantity, null, "The order state could not be determined; TradeRelay did not retry submission.", timeProvider.GetUtcNow());
                    store.FailExecution(preparationId, unknown, unknown.Message);
                    await AuditFinalAsync("execute_prepared_order", "UNKNOWN", current, correlationId, unknown, "ORDER_STATE_UNKNOWN", cancellationToken).ConfigureAwait(false);
                    return (false, "ORDER_STATE_UNKNOWN", unknown.Message, unknown);
                }
                acknowledgement = ambiguous;
            }

            await audit.TryWriteAsync(audit.Create("execute_prepared_order", "provider_acknowledgement", "ACKNOWLEDGED", current.Environment, correlationId, order.Symbol, preparationId, current.ClientOrderId, acknowledgement.ExchangeOrderId, current.State.ToString(), Risk(current), acknowledgement.Message, acknowledgement.Status.ToString()), cancellationToken).ConfigureAwait(false);
            OrderSubmissionResult reconciled = acknowledgement.Confirmed ? acknowledgement : await ReconcileAsync(provider, connections.Stream, order.Symbol, acknowledgement, cancellationToken).ConfigureAwait(false);
            if (reconciled.Status == ExchangeOrderStatus.Unknown)
            {
                store.FailExecution(preparationId, reconciled, reconciled.Message);
                await AuditFinalAsync("execute_prepared_order", "UNKNOWN", current, correlationId, reconciled, "ORDER_STATE_UNKNOWN", cancellationToken).ConfigureAwait(false);
                return (false, "ORDER_STATE_UNKNOWN", reconciled.Message, reconciled);
            }
            store.CompleteExecution(preparationId, reconciled);
            await AuditFinalAsync("execute_prepared_order", "OK", current, correlationId, reconciled, null, cancellationToken).ConfigureAwait(false);
            return (true, "OK", "Demo order submitted and reconciled.", reconciled);
        }
        catch (OperationCanceledException) { throw; }
        catch (ProviderException exception)
        {
            store.FailExecution(preparationId, null, exception.Message);
            await AuditFinalAsync("execute_prepared_order", "FAILED", current, correlationId, null, exception.Code, cancellationToken).ConfigureAwait(false);
            return Failure<OrderSubmissionResult>(exception.Code, exception.Message);
        }
    }

    public async Task<(bool Success, string Code, string Message, OperationResult? Data)> CancelAsync(string symbol, string exchangeOrderId, string correlationId, CancellationToken cancellationToken)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(exchangeOrderId)) return Failure<OperationResult>("VALIDATION_FAILED", "exchangeOrderId is required.");
        TradingGateResult allowed = gate.Check(TradingAction.CancelOrder);
        if (!allowed.Allowed) return await DeniedOperationAsync("cancel_order", normalized, allowed, correlationId, cancellationToken).ConfigureAwait(false);
        await audit.WriteRequiredAsync(audit.Create("cancel_order", "cancel_requested", "STARTED", TradingEnvironment.Demo, correlationId, normalized, exchangeOrderId: exchangeOrderId), cancellationToken).ConfigureAwait(false);
        try
        {
            OperationResult result = await connections.Trading!.CancelOrderAsync(normalized, exchangeOrderId, cancellationToken).ConfigureAwait(false);
            OrderSubmissionResult? order = await connections.Trading.GetOrderAsync(normalized, exchangeOrderId, null, cancellationToken).ConfigureAwait(false);
            OperationResult reconciled = result with { Status = order?.Status ?? result.Status, Message = order is null ? result.Message : $"Cancellation reconciled as {order.Status}." };
            await audit.TryWriteAsync(audit.Create("cancel_order", "cancel_reconciled", "OK", TradingEnvironment.Demo, correlationId, normalized, exchangeOrderId: exchangeOrderId, providerResult: reconciled.Message, finalStatus: reconciled.Status?.ToString()), cancellationToken).ConfigureAwait(false);
            return (true, "OK", reconciled.Message, reconciled);
        }
        catch (ProviderException exception) { return Failure<OperationResult>(exception.Code, exception.Message); }
    }

    public async Task<(bool Success, string Code, string Message, OperationResult? Data)> CancelAllAsync(bool confirm, string? symbol, string correlationId, CancellationToken cancellationToken)
    {
        if (!confirm) return Failure<OperationResult>("VALIDATION_FAILED", "Set confirm=true only after the user explicitly requests cancel-all.");
        string? normalized = string.IsNullOrWhiteSpace(symbol) ? null : Normalize(symbol);
        TradingGateResult allowed = gate.Check(TradingAction.CancelAllOrders);
        if (!allowed.Allowed) return await DeniedOperationAsync("cancel_all_orders", normalized, allowed, correlationId, cancellationToken).ConfigureAwait(false);
        await audit.WriteRequiredAsync(audit.Create("cancel_all_orders", "cancel_all_requested", "STARTED", TradingEnvironment.Demo, correlationId, normalized), cancellationToken).ConfigureAwait(false);
        try
        {
            OperationResult result = await connections.Trading!.CancelAllOrdersAsync(normalized, cancellationToken).ConfigureAwait(false);
            int remaining = (await connections.Account!.GetOpenOrdersAsync(normalized, cancellationToken).ConfigureAwait(false)).Count;
            OperationResult reconciled = result with { Message = $"Cancel-all acknowledged; {remaining} matching active orders remain." };
            await audit.TryWriteAsync(audit.Create("cancel_all_orders", "cancel_all_reconciled", "OK", TradingEnvironment.Demo, correlationId, normalized, providerResult: reconciled.Message, finalStatus: remaining == 0 ? "Cancelled" : "Pending"), cancellationToken).ConfigureAwait(false);
            return (true, "OK", reconciled.Message, reconciled);
        }
        catch (ProviderException exception) { return Failure<OperationResult>(exception.Code, exception.Message); }
    }

    public async Task<(bool Success, string Code, string Message, OrderSubmissionResult? Data)> CloseAsync(string symbol, decimal? quantity, string correlationId, CancellationToken cancellationToken)
    {
        string normalized = Normalize(symbol);
        TradingGateResult allowed = gate.Check(TradingAction.ClosePosition);
        if (!allowed.Allowed) { await AuditDeniedAsync("close_position", normalized, allowed, correlationId, null, cancellationToken).ConfigureAwait(false); return Failure<OrderSubmissionResult>(allowed.Code, allowed.Message); }
        IReadOnlyList<PositionSnapshot> positions = await connections.Account!.GetPositionsAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (positions.Count != 1) return Failure<OrderSubmissionResult>("VALIDATION_FAILED", positions.Count == 0 ? "No open position exists for this symbol." : "The position is ambiguous and cannot be closed safely.");
        PositionSnapshot position = positions[0];
        InstrumentInfo instrument = await connections.MarketData.GetInstrumentInfoAsync(normalized, cancellationToken).ConfigureAwait(false);
        decimal closeQuantity = DecimalNormalizer.RoundDownToStep(Math.Min(quantity ?? position.Size, position.Size), instrument.QuantityStep);
        if (closeQuantity <= 0m) return Failure<OrderSubmissionResult>("VALIDATION_FAILED", "Close quantity must be positive.");
        await audit.WriteRequiredAsync(audit.Create("close_position", "close_requested", "STARTED", TradingEnvironment.Demo, correlationId, normalized), cancellationToken).ConfigureAwait(false);
        try
        {
            OrderSubmissionResult result = await connections.Trading!.ClosePositionAsync(new(normalized, position.Side, closeQuantity, position.PositionMode), cancellationToken).ConfigureAwait(false);
            OrderSubmissionResult reconciled = result.Confirmed ? result : await ReconcileAsync(connections.Trading, connections.Stream, normalized, result, cancellationToken).ConfigureAwait(false);
            await audit.TryWriteAsync(audit.Create("close_position", "close_reconciled", reconciled.Status == ExchangeOrderStatus.Unknown ? "UNKNOWN" : "OK", TradingEnvironment.Demo, correlationId, normalized, clientOrderId: reconciled.ClientOrderId, exchangeOrderId: reconciled.ExchangeOrderId, providerResult: reconciled.Message, finalStatus: reconciled.Status.ToString()), cancellationToken).ConfigureAwait(false);
            return (reconciled.Status != ExchangeOrderStatus.Unknown, reconciled.Status == ExchangeOrderStatus.Unknown ? "ORDER_STATE_UNKNOWN" : "OK", reconciled.Message, reconciled);
        }
        catch (ProviderException exception) { return Failure<OrderSubmissionResult>(exception.Code, exception.Message); }
    }

    public async Task<(bool Success, string Code, string Message, OperationResult? Data)> SetTradingStopAsync(string symbol, decimal stopLoss, decimal? takeProfit, string correlationId, CancellationToken cancellationToken)
    {
        string normalized = Normalize(symbol);
        TradingGateResult allowed = gate.Check(TradingAction.SetTradingStop);
        if (!allowed.Allowed) return await DeniedOperationAsync("set_trading_stop", normalized, allowed, correlationId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PositionSnapshot> positions = await connections.Account!.GetPositionsAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (positions.Count != 1) return Failure<OperationResult>("VALIDATION_FAILED", positions.Count == 0 ? "No open position exists for this symbol." : "The position is ambiguous.");
        PositionSnapshot position = positions[0];
        InstrumentInfo instrument = await connections.MarketData.GetInstrumentInfoAsync(normalized, cancellationToken).ConfigureAwait(false);
        decimal stop = position.Side == TradeSide.Buy ? DecimalNormalizer.RoundDownToStep(stopLoss, instrument.TickSize) : DecimalNormalizer.RoundUpToStep(stopLoss, instrument.TickSize);
        decimal? take = takeProfit is null ? null : DecimalNormalizer.RoundToTick(takeProfit.Value, instrument.TickSize);
        if (stop <= 0m || (position.Side == TradeSide.Buy ? stop >= position.MarkPrice : stop <= position.MarkPrice)) return Failure<OperationResult>("VALIDATION_FAILED", position.Side == TradeSide.Buy ? "A long-position stop must be below mark price." : "A short-position stop must be above mark price.");
        if (take is not null && (position.Side == TradeSide.Buy ? take <= position.MarkPrice : take >= position.MarkPrice)) return Failure<OperationResult>("VALIDATION_FAILED", position.Side == TradeSide.Buy ? "A long-position take profit must be above mark price." : "A short-position take profit must be below mark price.");
        await audit.WriteRequiredAsync(audit.Create("set_trading_stop", "stop_update_requested", "STARTED", TradingEnvironment.Demo, correlationId, normalized), cancellationToken).ConfigureAwait(false);
        try
        {
            OperationResult result = await connections.Trading!.SetTradingStopAsync(new(normalized, position.Side, stop, take, position.PositionMode), cancellationToken).ConfigureAwait(false);
            await audit.TryWriteAsync(audit.Create("set_trading_stop", "stop_update_acknowledged", "OK", TradingEnvironment.Demo, correlationId, normalized, providerResult: result.Message), cancellationToken).ConfigureAwait(false);
            return (true, "OK", result.Message, result);
        }
        catch (ProviderException exception) { return Failure<OperationResult>(exception.Code, exception.Message); }
    }

    private async Task<OrderSubmissionResult> ReconcileAsync(IExchangeTradingProvider provider, IExchangeStream? stream, string symbol, OrderSubmissionResult acknowledgement, CancellationToken cancellationToken)
    {
        if (stream is not null && acknowledgement.ExchangeOrderId is not null)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Updated(object? _, OrderUpdate update) { if (update.ExchangeOrderId == acknowledgement.ExchangeOrderId) signal.TrySetResult(); }
            stream.OrderUpdated += Updated;
            try { await Task.WhenAny(signal.Task, Task.Delay(StreamTimeout, timeProvider, cancellationToken)).ConfigureAwait(false); }
            finally { stream.OrderUpdated -= Updated; }
        }
        OrderSubmissionResult? reconciled = await provider.GetOrderAsync(symbol, acknowledgement.ExchangeOrderId, acknowledgement.ClientOrderId, cancellationToken).ConfigureAwait(false);
        return reconciled ?? acknowledgement with { Status = ExchangeOrderStatus.Unknown, Message = "The submission was acknowledged but its current state is unknown; TradeRelay did not retry it." };
    }

    private async Task AuditDeniedAsync(string tool, string? symbol, TradingGateResult result, string correlationId, Guid? preparationId, CancellationToken cancellationToken) => await audit.TryWriteAsync(audit.Create(tool, "gate_denied", "DENIED", TradingEnvironment.Demo, correlationId, symbol, preparationId, errorCode: result.Code, providerResult: result.Message), cancellationToken).ConfigureAwait(false);
    private async Task<(bool Success, string Code, string Message, OperationResult? Data)> DeniedOperationAsync(string tool, string? symbol, TradingGateResult result, string correlationId, CancellationToken cancellationToken) { await AuditDeniedAsync(tool, symbol, result, correlationId, null, cancellationToken).ConfigureAwait(false); return Failure<OperationResult>(result.Code, result.Message); }
    private async Task AuditFinalAsync(string tool, string result, PreparedOrder order, string correlationId, OrderSubmissionResult? submission, string? errorCode, CancellationToken cancellationToken) => await audit.TryWriteAsync(audit.Create(tool, "execution_reconciled", result, order.Environment, correlationId, order.Order.Symbol, order.PreparationId, order.ClientOrderId, submission?.ExchangeOrderId, order.State.ToString(), Risk(order), submission?.Message, submission?.Status.ToString(), errorCode), cancellationToken).ConfigureAwait(false);
    private static string Risk(PreparedOrder order) => $"notional={order.Order.Risk.EstimatedNotionalUsd};risk={order.Order.Risk.EstimatedRiskUsd?.ToString() ?? "unknown"};accountRisk={order.Order.Risk.AccountRiskPercent?.ToString() ?? "unknown"}";
    private static string Normalize(string symbol) { string value = RiskEngine.NormalizeSymbol(symbol); if (!value.EndsWith("USDT", StringComparison.Ordinal) || value.Length <= 4) throw new ProviderException("VALIDATION_FAILED", "Symbol must be a USDT linear perpetual symbol."); return value; }
    private static (bool Success, string Code, string Message, T? Data) Failure<T>(string code, string message) where T : class => (false, code, message, null);
}

