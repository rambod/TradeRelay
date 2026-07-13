---
name: traderelay-operator
description: Safely inspect exchange accounts and operate TradeRelay 1.3+ MCP workflows. Use for provider/environment status, positions, orders, Long/Short exposure, runtime errors, risk sizing, immutable order preparation, desktop approval, explicitly enabled Bybit execution, cancellation, reduce-only closing, protection, and post-write reconciliation. Never use it to enable Live trading automatically or to retry an ambiguous submission.
---

# TradeRelay Operator

Use TradeRelay as an operator-controlled exchange console. Start with status, make the exchange and environment explicit, preserve desktop approval boundaries, and verify exchange state after every write.

## Non-negotiable safety rules

- Call `get_system_status` before any account, planning, or trading workflow.
- Call `get_exchanges` when provider choice or capability is not already explicit.
- State the selected exchange and environment in the response before proposing a write.
- Never enable Demo or Live trading. Only the desktop operator can enable the current session.
- Never treat Prepared, Approved, Acknowledged, or Executed as interchangeable.
- Never retry order placement after an ambiguous response. Inspect the prepared order, order history, open orders, executions, and system status.
- Never request a write for an exchange that does not advertise write capability. Bybit is the only write-capable adapter in the current release.
- Never claim TradeRelay-observed history covers activity from before TradeRelay observed the account.
- Do not ask users to paste API credentials, MCP tokens, authorization headers, or signatures into chat.

## Status-first workflow

1. Call `get_system_status`.
2. Call `get_exchanges` if more than one exchange is available or the user did not name one.
3. Resolve the exchange explicitly. If omission could select more than one connected provider, stop and ask for the exchange.
4. Confirm the environment. Treat `Live` as real funds and positions even while trading is disabled.
5. Inspect `get_runtime_errors` when health is degraded or a previous action failed.

Do not progress to a write when audit health, connection health, approvals, or reconciliation are unavailable.

## Inspect account and exposure

- Use `get_account_summary`, `get_wallet_balances`, `get_positions`, and `get_open_orders` for current state.
- Use `get_order_history` and `get_execution_history` for exchange-returned history.
- Use `get_position_history` only for changes TradeRelay actually observed.
- Always describe Buy-side positions as **Long** and Sell-side positions as **Short**. Include symbol, quantity, entry/mark price, leverage, unrealized PnL, liquidation information, and stop/take-profit coverage when available.
- Distinguish gross exposure from signed net exposure.

## Calculate and prepare

1. Get current risk settings with `get_risk_settings`.
2. Get fresh ticker and instrument information for the selected exchange.
3. Inspect account equity and current positions.
4. Use `calculate_position_size` when the user specifies account-risk sizing.
5. Call `validate_order` before preparation when assumptions need review.
6. Call `prepare_order` once with a unique caller-supplied `clientRequestId`.
7. Present requested versus normalized quantity/prices, notional, leverage, stop, take profit, estimated risk/reward, warnings, expiry, preparation ID, and immutable hash preview.

Preparation is a local immutable plan. It is not an exchange order.

## Respect desktop approval

- If the plan is pending, tell the user that approval or rejection happens in the TradeRelay desktop Approvals workspace.
- Poll only when the user asks to continue; use `get_prepared_order` or `get_pending_approvals`.
- Do not search for or invoke any hidden approval mechanism.
- Expired, rejected, failed, or hash-mismatched plans require a new preparation with a new `clientRequestId`.

## Execute only on explicit instruction

Before `execute_prepared_order`, confirm all of the following:

- Exchange is Bybit.
- Environment matches the prepared plan.
- Desktop session reports trading enabled.
- Plan is approved and unexpired.
- Connection generation and immutable hash remain valid.
- The user explicitly requested execution, not merely preparation or review.

Invoke `execute_prepared_order` once. Treat REST acknowledgement as provisional. Then call `get_prepared_order`, `get_open_orders`, `get_order_history`, `get_execution_history`, and `get_positions` as appropriate until the normalized reconciled outcome is clear.

If the result is `ORDER_STATE_UNKNOWN`, `CONNECTION_CHANGED`, or otherwise ambiguous, do not place again. Report the uncertainty and inspect state.

## Destructive actions

- Keep `confirm=true` mandatory for `cancel_all_orders`.
- Live cancel-all and close-position require a short-lived desktop confirmation ticket. The first exact request creates it; desktop approval is required; then repeat the exact action once with the approved confirmation ID.
- Never change ticket scope, symbol, quantity, exchange, environment, or caller request ID between approval and execution.
- `close_position` must remain reduce-only and may not reverse a position.
- After cancellation, close, or protection changes, verify open orders, positions, protection, and runtime errors.

## Failure handling

- For `CAPABILITY_NOT_SUPPORTED`, select a supported read workflow or explicitly explain that the adapter is read-only.
- For `EXCHANGE_NOT_FOUND`, `EXCHANGE_NOT_CONNECTED`, or `AMBIGUOUS_EXCHANGE`, resolve the provider before continuing.
- For `AUDIT_UNAVAILABLE`, do not attempt writes; the operator must restore audit health.
- For `RATE_LIMITED`, wait or reduce read frequency. Never convert a rate-limit failure into a blind write retry.
- For credential or permission failures, direct the operator to Connections. Never request secrets in chat.

Read [tool workflows](references/tool-workflows.md) when selecting tools, interpreting lifecycle sources, or handling stable codes.
