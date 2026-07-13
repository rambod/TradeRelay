using System.Globalization;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Risk;

namespace TradeRelay.Desktop.Services;

internal sealed class OrderExecutionService(
    ExchangeConnectionManager connections,
    TradingControlService control,
    TradingGate gate,
    OrderPreparationService preparation,
    PreparedOrderStore store,
    LiveActionConfirmationStore liveConfirmations,
    AuditLogService audit,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(5);

    public async Task<(bool Success, string Code, string Message, OrderSubmissionResult? Data)> ExecuteAsync(Guid preparationId, string correlationId, CancellationToken cancellationToken)
    {
        PreparedOrder? current = store.Get(preparationId);
        if (current is null) return Failure<OrderSubmissionResult>("VALIDATION_FAILED", "Prepared order not found.");
        (TradingGateResult allowed, TradingWriteLease? lease) = gate.TryEnter(TradingAction.ExecutePreparedOrder, current);
        if (!allowed.Allowed || lease is null)
        {
            await AuditDeniedAsync("execute_prepared_order", current.Order.Symbol, allowed, correlationId, preparationId, cancellationToken).ConfigureAwait(false);
            return Failure<OrderSubmissionResult>(allowed.Code, allowed.Message);
        }

        using (lease)
        {
            await audit.WriteRequiredAsync(audit.Create("execute_prepared_order", "execution_requested", "STARTED", current.Environment, correlationId, current.Order.Symbol, current.PreparationId, current.ClientOrderId, approvalState: current.State.ToString(), riskSummary: Risk(current)), cancellationToken).ConfigureAwait(false);
            PreparedOrderResult claim = store.BeginExecution(preparationId, current.ImmutableHash);
            if (!claim.Success || claim.Order is null) return Failure<OrderSubmissionResult>(claim.Code, claim.Message);
            current = claim.Order;

            try
            {
                OrderValidationResult validation = await preparation.RevalidateAsync(current, cancellationToken).ConfigureAwait(false);
                if (!validation.Valid || validation.Order is null)
                    return await FailPreparedAsync(current, correlationId, "RISK_LIMIT_EXCEEDED", "Current account or market state no longer passes the prepared risk plan.", cancellationToken).ConfigureAwait(false);

                NormalizedOrder order = validation.Order;
                if (current.Environment == TradingEnvironment.Live && order.OrderType == OrderType.Market && PriceDeviationPercent(current.Order.EstimatedEntryPrice, order.EstimatedEntryPrice) > current.RiskSettings.MaxMarketPriceDeviationPercent)
                    return await FailPreparedAsync(current, correlationId, "PRICE_DEVIATION_EXCEEDED", $"The current executable price moved more than the approved {current.RiskSettings.MaxMarketPriceDeviationPercent.ToString("G29", CultureInfo.InvariantCulture)} percent tolerance.", cancellationToken).ConfigureAwait(false);

                var request = new ExchangeOrderRequest(current.ClientOrderId, order.Symbol, order.Side, order.OrderType, order.Quantity, order.LimitPrice, order.StopLoss, order.TakeProfit, false);
                IExchangeTradingProvider provider = connections.Trading!;
                OrderSubmissionResult acknowledgement;
                try
                {
                    acknowledgement = await provider.PlaceOrderAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (ProviderException)
                {
                    OrderSubmissionResult? ambiguous = await provider.GetOrderAsync(order.Symbol, null, current.ClientOrderId, cancellationToken).ConfigureAwait(false);
                    if (ambiguous is null)
                    {
                        control.Disable("Order submission state became unknown; new trading actions were disabled.", emergency: true);
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
                return (true, "OK", $"{current.Environment} order submitted and reconciled.", reconciled);
            }
            catch (OperationCanceledException) { throw; }
            catch (ProviderException exception)
            {
                HandleProviderFailure(exception);
                store.FailExecution(preparationId, null, exception.Message);
                await AuditFinalAsync("execute_prepared_order", "FAILED", current, correlationId, null, exception.Code, cancellationToken).ConfigureAwait(false);
                return Failure<OrderSubmissionResult>(exception.Code, exception.Message);
            }
        }
    }

    public async Task<(bool Success, string Code, string Message, OperationResult? Data)> CancelAsync(string symbol, string exchangeOrderId, string correlationId, CancellationToken cancellationToken)
    {
        string normalized = Normalize(symbol);
        if (string.IsNullOrWhiteSpace(exchangeOrderId)) return Failure<OperationResult>("VALIDATION_FAILED", "exchangeOrderId is required.");
        (TradingGateResult allowed, TradingWriteLease? lease) = gate.TryEnter(TradingAction.CancelOrder);
        if (!allowed.Allowed || lease is null) return await DeniedOperationAsync("cancel_order", normalized, allowed, correlationId, cancellationToken).ConfigureAwait(false);
        using (lease)
        {
            TradingEnvironment environment = connections.Snapshot.Environment;
            await audit.WriteRequiredAsync(audit.Create("cancel_order", "cancel_requested", "STARTED", environment, correlationId, normalized, exchangeOrderId: exchangeOrderId), cancellationToken).ConfigureAwait(false);
            try
            {
                OperationResult result = await connections.Trading!.CancelOrderAsync(normalized, exchangeOrderId, cancellationToken).ConfigureAwait(false);
                OrderSubmissionResult? order = await connections.Trading.GetOrderAsync(normalized, exchangeOrderId, null, cancellationToken).ConfigureAwait(false);
                OperationResult reconciled = result with { Status = order?.Status ?? result.Status, Message = order is null ? result.Message : $"Cancellation reconciled as {order.Status}." };
                await audit.TryWriteAsync(audit.Create("cancel_order", "cancel_reconciled", "OK", environment, correlationId, normalized, exchangeOrderId: exchangeOrderId, providerResult: reconciled.Message, finalStatus: reconciled.Status?.ToString()), cancellationToken).ConfigureAwait(false);
                return (true, "OK", reconciled.Message, reconciled);
            }
            catch (ProviderException exception) { HandleProviderFailure(exception); return Failure<OperationResult>(exception.Code, exception.Message); }
        }
    }

    public async Task<(bool Success, string Code, string Message, LiveActionOutcome<OperationResult>? Data)> CancelAllAsync(
        bool confirm,
        string? symbol,
        string? clientRequestId,
        string? liveConfirmationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!confirm) return Failure<LiveActionOutcome<OperationResult>>("VALIDATION_FAILED", "Set confirm=true only after the user explicitly requests cancel-all.");
        string? normalized = string.IsNullOrWhiteSpace(symbol) ? null : Normalize(symbol);
        TradingGateResult preflight = gate.Check(TradingAction.CancelAllOrders);
        if (!preflight.Allowed) return await DeniedLiveOutcomeAsync<OperationResult>("cancel_all_orders", normalized, preflight, correlationId, cancellationToken).ConfigureAwait(false);

        LiveActionConfirmation? confirmation = null;
        if (connections.Snapshot.Environment == TradingEnvironment.Live)
        {
            LiveActionConfirmationResult confirmationResult = await ConfirmLiveCancelAllAsync(normalized, clientRequestId, liveConfirmationId, correlationId, cancellationToken).ConfigureAwait(false);
            confirmation = confirmationResult.Confirmation;
            if (!confirmationResult.Success || confirmation?.State != LiveActionConfirmationState.Executing)
                return (false, confirmationResult.Code, confirmationResult.Message, new(confirmation, null));
        }

        (TradingGateResult allowed, TradingWriteLease? lease) = gate.TryEnter(TradingAction.CancelAllOrders);
        if (!allowed.Allowed || lease is null)
        {
            if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, allowed.Code, allowed.Message);
            return await DeniedLiveOutcomeAsync<OperationResult>("cancel_all_orders", normalized, allowed, correlationId, cancellationToken).ConfigureAwait(false);
        }

        using (lease)
        {
            TradingEnvironment environment = connections.Snapshot.Environment;
            try
            {
                await audit.WriteRequiredAsync(audit.Create("cancel_all_orders", "cancel_all_requested", "STARTED", environment, correlationId, normalized), cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderException exception)
            {
                if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, exception.Code, exception.Message);
                throw;
            }
            try
            {
                OperationResult result = await connections.Trading!.CancelAllOrdersAsync(normalized, cancellationToken).ConfigureAwait(false);
                int remaining = (await connections.Account!.GetOpenOrdersAsync(normalized, cancellationToken).ConfigureAwait(false)).Count;
                OperationResult reconciled = result with { Message = $"Cancel-all acknowledged; {remaining} matching active orders remain." };
                if (confirmation is not null) liveConfirmations.Complete(confirmation.ConfirmationId, "OK", reconciled.Message);
                await audit.TryWriteAsync(audit.Create("cancel_all_orders", "cancel_all_reconciled", "OK", environment, correlationId, normalized, providerResult: reconciled.Message, finalStatus: remaining == 0 ? "Cancelled" : "Pending"), cancellationToken).ConfigureAwait(false);
                return (true, "OK", reconciled.Message, new(liveConfirmations.Get(confirmation?.ConfirmationId ?? Guid.Empty), reconciled));
            }
            catch (ProviderException exception)
            {
                HandleProviderFailure(exception);
                if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, exception.Code, exception.Message);
                return (false, exception.Code, exception.Message, new(liveConfirmations.Get(confirmation?.ConfirmationId ?? Guid.Empty), null));
            }
        }
    }

    public async Task<(bool Success, string Code, string Message, LiveActionOutcome<OrderSubmissionResult>? Data)> CloseAsync(
        string symbol,
        decimal? quantity,
        string? clientRequestId,
        string? liveConfirmationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        string normalized = Normalize(symbol);
        if (quantity is <= 0m) return Failure<LiveActionOutcome<OrderSubmissionResult>>("VALIDATION_FAILED", "Close quantity must be positive when provided.");
        TradingGateResult preflight = gate.Check(TradingAction.ClosePosition);
        if (!preflight.Allowed) return await DeniedLiveOutcomeAsync<OrderSubmissionResult>("close_position", normalized, preflight, correlationId, cancellationToken).ConfigureAwait(false);

        LiveActionConfirmation? confirmation = null;
        if (connections.Snapshot.Environment == TradingEnvironment.Live)
        {
            LiveActionConfirmationResult confirmationResult = await ConfirmLiveCloseAsync(normalized, quantity, clientRequestId, liveConfirmationId, correlationId, cancellationToken).ConfigureAwait(false);
            confirmation = confirmationResult.Confirmation;
            if (!confirmationResult.Success || confirmation?.State != LiveActionConfirmationState.Executing)
                return (false, confirmationResult.Code, confirmationResult.Message, new(confirmation, null));
        }

        (TradingGateResult allowed, TradingWriteLease? lease) = gate.TryEnter(TradingAction.ClosePosition);
        if (!allowed.Allowed || lease is null)
        {
            if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, allowed.Code, allowed.Message);
            return await DeniedLiveOutcomeAsync<OrderSubmissionResult>("close_position", normalized, allowed, correlationId, cancellationToken).ConfigureAwait(false);
        }

        using (lease)
        {
            IReadOnlyList<PositionSnapshot> positions = await connections.Account!.GetPositionsAsync(normalized, cancellationToken).ConfigureAwait(false);
            if (positions.Count != 1)
            {
                string message = positions.Count == 0 ? "No open position exists for this symbol." : "The position is ambiguous and cannot be closed safely.";
                if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, "VALIDATION_FAILED", message);
                return (false, "VALIDATION_FAILED", message, new(liveConfirmations.Get(confirmation?.ConfirmationId ?? Guid.Empty), null));
            }

            PositionSnapshot position = positions[0];
            InstrumentInfo instrument = await connections.MarketData.GetInstrumentInfoAsync(normalized, cancellationToken).ConfigureAwait(false);
            decimal closeQuantity = DecimalNormalizer.RoundDownToStep(Math.Min(quantity ?? position.Size, position.Size), instrument.QuantityStep);
            if (closeQuantity <= 0m)
            {
                const string message = "Close quantity must be positive after instrument normalization.";
                if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, "VALIDATION_FAILED", message);
                return (false, "VALIDATION_FAILED", message, new(liveConfirmations.Get(confirmation?.ConfirmationId ?? Guid.Empty), null));
            }
            TradingEnvironment environment = connections.Snapshot.Environment;
            try
            {
                await audit.WriteRequiredAsync(audit.Create("close_position", "close_requested", "STARTED", environment, correlationId, normalized), cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderException exception)
            {
                if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, exception.Code, exception.Message);
                throw;
            }
            try
            {
                OrderSubmissionResult result = await connections.Trading!.ClosePositionAsync(new(normalized, position.Side, closeQuantity, position.PositionMode), cancellationToken).ConfigureAwait(false);
                OrderSubmissionResult reconciled = result.Confirmed ? result : await ReconcileAsync(connections.Trading, connections.Stream, normalized, result, cancellationToken).ConfigureAwait(false);
                string code = reconciled.Status == ExchangeOrderStatus.Unknown ? "ORDER_STATE_UNKNOWN" : "OK";
                if (confirmation is not null)
                {
                    if (code == "OK") liveConfirmations.Complete(confirmation.ConfirmationId, code, reconciled.Message);
                    else liveConfirmations.Fail(confirmation.ConfirmationId, code, reconciled.Message);
                }
                await audit.TryWriteAsync(audit.Create("close_position", "close_reconciled", code == "OK" ? "OK" : "UNKNOWN", environment, correlationId, normalized, clientOrderId: reconciled.ClientOrderId, exchangeOrderId: reconciled.ExchangeOrderId, providerResult: reconciled.Message, finalStatus: reconciled.Status.ToString()), cancellationToken).ConfigureAwait(false);
                return (code == "OK", code, reconciled.Message, new(liveConfirmations.Get(confirmation?.ConfirmationId ?? Guid.Empty), reconciled));
            }
            catch (ProviderException exception)
            {
                HandleProviderFailure(exception);
                if (confirmation is not null) liveConfirmations.Fail(confirmation.ConfirmationId, exception.Code, exception.Message);
                return (false, exception.Code, exception.Message, new(liveConfirmations.Get(confirmation?.ConfirmationId ?? Guid.Empty), null));
            }
        }
    }

    public async Task<(bool Success, string Code, string Message, OperationResult? Data)> SetTradingStopAsync(string symbol, decimal stopLoss, decimal? takeProfit, string correlationId, CancellationToken cancellationToken)
    {
        string normalized = Normalize(symbol);
        (TradingGateResult allowed, TradingWriteLease? lease) = gate.TryEnter(TradingAction.SetTradingStop);
        if (!allowed.Allowed || lease is null) return await DeniedOperationAsync("set_trading_stop", normalized, allowed, correlationId, cancellationToken).ConfigureAwait(false);
        using (lease)
        {
            IReadOnlyList<PositionSnapshot> positions = await connections.Account!.GetPositionsAsync(normalized, cancellationToken).ConfigureAwait(false);
            if (positions.Count != 1) return Failure<OperationResult>("VALIDATION_FAILED", positions.Count == 0 ? "No open position exists for this symbol." : "The position is ambiguous.");
            PositionSnapshot position = positions[0];
            InstrumentInfo instrument = await connections.MarketData.GetInstrumentInfoAsync(normalized, cancellationToken).ConfigureAwait(false);
            decimal stop = position.Side == TradeSide.Buy ? DecimalNormalizer.RoundDownToStep(stopLoss, instrument.TickSize) : DecimalNormalizer.RoundUpToStep(stopLoss, instrument.TickSize);
            decimal? take = takeProfit is null ? null : DecimalNormalizer.RoundToTick(takeProfit.Value, instrument.TickSize);
            if (stop <= 0m || (position.Side == TradeSide.Buy ? stop >= position.MarkPrice : stop <= position.MarkPrice)) return Failure<OperationResult>("VALIDATION_FAILED", position.Side == TradeSide.Buy ? "A long-position stop must be below mark price." : "A short-position stop must be above mark price.");
            if (take is not null && (position.Side == TradeSide.Buy ? take <= position.MarkPrice : take >= position.MarkPrice)) return Failure<OperationResult>("VALIDATION_FAILED", position.Side == TradeSide.Buy ? "A long-position take profit must be above mark price." : "A short-position take profit must be below mark price.");
            TradingEnvironment environment = connections.Snapshot.Environment;
            await audit.WriteRequiredAsync(audit.Create("set_trading_stop", "stop_update_requested", "STARTED", environment, correlationId, normalized), cancellationToken).ConfigureAwait(false);
            try
            {
                OperationResult result = await connections.Trading!.SetTradingStopAsync(new(normalized, position.Side, stop, take, position.PositionMode), cancellationToken).ConfigureAwait(false);
                await audit.TryWriteAsync(audit.Create("set_trading_stop", "stop_update_acknowledged", "OK", environment, correlationId, normalized, providerResult: result.Message), cancellationToken).ConfigureAwait(false);
                return (true, "OK", result.Message, result);
            }
            catch (ProviderException exception) { HandleProviderFailure(exception); return Failure<OperationResult>(exception.Code, exception.Message); }
        }
    }

    private async Task<LiveActionConfirmationResult> ConfirmLiveCancelAllAsync(string? symbol, string? clientRequestId, string? confirmationId, string correlationId, CancellationToken cancellationToken)
    {
        Guid sessionId = control.Snapshot.SessionId ?? Guid.Empty;
        if (string.IsNullOrWhiteSpace(confirmationId))
        {
            int count = (await connections.Account!.GetOpenOrdersAsync(symbol, cancellationToken).ConfigureAwait(false)).Count;
            var request = new LiveActionRequest(LiveActionType.CancelAllOrders, symbol, null, symbol is null ? "All active USDT-linear orders" : $"All active {symbol} orders", count);
            LiveActionConfirmationResult created = liveConfirmations.Add(clientRequestId, request, connections.Snapshot.ConnectionGenerationId, sessionId);
            await AuditConfirmationAsync("cancel_all_orders", "live_confirmation_requested", created, correlationId, symbol, cancellationToken).ConfigureAwait(false);
            return created;
        }

        if (!Guid.TryParse(confirmationId, out Guid id)) return new(false, "VALIDATION_FAILED", "liveConfirmationId is invalid.", null);
        LiveActionConfirmation? existing = liveConfirmations.Get(id);
        if (existing is null) return new(false, "VALIDATION_FAILED", "Live action confirmation not found.", null);
        var expected = new LiveActionRequest(LiveActionType.CancelAllOrders, symbol, null, symbol is null ? "All active USDT-linear orders" : $"All active {symbol} orders", existing.Request.CurrentMatchingCount);
        return liveConfirmations.Begin(id, clientRequestId ?? string.Empty, expected, connections.Snapshot.ConnectionGenerationId, sessionId);
    }

    private async Task<LiveActionConfirmationResult> ConfirmLiveCloseAsync(string symbol, decimal? quantity, string? clientRequestId, string? confirmationId, string correlationId, CancellationToken cancellationToken)
    {
        Guid sessionId = control.Snapshot.SessionId ?? Guid.Empty;
        if (string.IsNullOrWhiteSpace(confirmationId))
        {
            IReadOnlyList<PositionSnapshot> positions = await connections.Account!.GetPositionsAsync(symbol, cancellationToken).ConfigureAwait(false);
            if (positions.Count != 1) return new(false, "VALIDATION_FAILED", positions.Count == 0 ? "No open position exists for this symbol." : "The position is ambiguous and cannot be confirmed safely.", null);
            PositionSnapshot position = positions[0];
            string scope = $"{position.Side} position · current size {position.Size.ToString("G29", CultureInfo.InvariantCulture)} · requested {(quantity?.ToString("G29", CultureInfo.InvariantCulture) ?? "full")}";
            var request = new LiveActionRequest(LiveActionType.ClosePosition, symbol, quantity, scope, 1);
            LiveActionConfirmationResult created = liveConfirmations.Add(clientRequestId, request, connections.Snapshot.ConnectionGenerationId, sessionId);
            await AuditConfirmationAsync("close_position", "live_confirmation_requested", created, correlationId, symbol, cancellationToken).ConfigureAwait(false);
            return created;
        }

        if (!Guid.TryParse(confirmationId, out Guid id)) return new(false, "VALIDATION_FAILED", "liveConfirmationId is invalid.", null);
        LiveActionConfirmation? existing = liveConfirmations.Get(id);
        if (existing is null) return new(false, "VALIDATION_FAILED", "Live action confirmation not found.", null);
        var expected = new LiveActionRequest(LiveActionType.ClosePosition, symbol, quantity, existing.Request.Scope, existing.Request.CurrentMatchingCount);
        return liveConfirmations.Begin(id, clientRequestId ?? string.Empty, expected, connections.Snapshot.ConnectionGenerationId, sessionId);
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

    private async Task<(bool Success, string Code, string Message, OrderSubmissionResult? Data)> FailPreparedAsync(PreparedOrder order, string correlationId, string code, string message, CancellationToken cancellationToken)
    {
        store.FailExecution(order.PreparationId, null, message);
        await AuditFinalAsync("execute_prepared_order", "FAILED", order, correlationId, null, code, cancellationToken).ConfigureAwait(false);
        return Failure<OrderSubmissionResult>(code, message);
    }

    private async Task AuditConfirmationAsync(string tool, string action, LiveActionConfirmationResult result, string correlationId, string? symbol, CancellationToken cancellationToken) =>
        await audit.TryWriteAsync(audit.Create(tool, action, result.Success ? "PENDING" : "FAILED", TradingEnvironment.Live, correlationId, symbol, approvalState: result.Confirmation?.State.ToString(), errorCode: result.Success ? null : result.Code, providerResult: result.Message), cancellationToken).ConfigureAwait(false);

    private async Task AuditDeniedAsync(string tool, string? symbol, TradingGateResult result, string correlationId, Guid? preparationId, CancellationToken cancellationToken) =>
        await audit.TryWriteAsync(audit.Create(tool, "gate_denied", "DENIED", connections.Snapshot.Environment, correlationId, symbol, preparationId, errorCode: result.Code, providerResult: result.Message), cancellationToken).ConfigureAwait(false);

    private async Task<(bool Success, string Code, string Message, OperationResult? Data)> DeniedOperationAsync(string tool, string? symbol, TradingGateResult result, string correlationId, CancellationToken cancellationToken)
    {
        await AuditDeniedAsync(tool, symbol, result, correlationId, null, cancellationToken).ConfigureAwait(false);
        return Failure<OperationResult>(result.Code, result.Message);
    }

    private async Task<(bool Success, string Code, string Message, LiveActionOutcome<T>? Data)> DeniedLiveOutcomeAsync<T>(string tool, string? symbol, TradingGateResult result, string correlationId, CancellationToken cancellationToken) where T : class
    {
        await AuditDeniedAsync(tool, symbol, result, correlationId, null, cancellationToken).ConfigureAwait(false);
        return Failure<LiveActionOutcome<T>>(result.Code, result.Message);
    }

    private async Task AuditFinalAsync(string tool, string result, PreparedOrder order, string correlationId, OrderSubmissionResult? submission, string? errorCode, CancellationToken cancellationToken) =>
        await audit.TryWriteAsync(audit.Create(tool, "execution_reconciled", result, order.Environment, correlationId, order.Order.Symbol, order.PreparationId, order.ClientOrderId, submission?.ExchangeOrderId, order.State.ToString(), Risk(order), submission?.Message, submission?.Status.ToString(), errorCode), cancellationToken).ConfigureAwait(false);

    private static decimal PriceDeviationPercent(decimal preparedPrice, decimal currentPrice) => preparedPrice <= 0m ? decimal.MaxValue : decimal.Abs(currentPrice - preparedPrice) / preparedPrice * 100m;
    private void HandleProviderFailure(ProviderException exception)
    {
        if (string.Equals(exception.Code, "PROVIDER_UNAVAILABLE", StringComparison.Ordinal))
            control.Disable("The exchange connection became unavailable; new trading actions were disabled.", emergency: true);
    }
    private static string Risk(PreparedOrder order) => $"notional={order.Order.Risk.EstimatedNotionalUsd};risk={order.Order.Risk.EstimatedRiskUsd?.ToString() ?? "unknown"};accountRisk={order.Order.Risk.AccountRiskPercent?.ToString() ?? "unknown"}";
    private static string Normalize(string symbol) { string value = RiskEngine.NormalizeSymbol(symbol); if (!value.EndsWith("USDT", StringComparison.Ordinal) || value.Length <= 4) throw new ProviderException("VALIDATION_FAILED", "Symbol must be a USDT linear perpetual symbol."); return value; }
    private static (bool Success, string Code, string Message, T? Data) Failure<T>(string code, string message) where T : class => (false, code, message, null);
}
