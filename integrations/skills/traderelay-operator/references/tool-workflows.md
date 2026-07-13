# Tool workflows

## Read sequence

1. `get_system_status`
2. `get_exchanges`
3. `get_account_summary`
4. `get_positions`
5. `get_open_orders`
6. `get_order_history` and `get_execution_history` when historical evidence matters
7. `get_runtime_errors` when state is degraded or a previous action failed

Pass `exchange` whenever more than one provider is connected. Omitting it is safe only when TradeRelay reports one unambiguous selected provider.

## Plan sequence

1. Read status, exchange capabilities, risk settings, account summary, positions, ticker, and instrument.
2. Calculate size if risk-based sizing is requested.
3. Validate the proposal.
4. Prepare once with a new `clientRequestId` containing 1–64 safe characters.
5. Present normalized plan fields, warnings, expiry, approval state, IDs, and hash preview.
6. Wait for desktop review when required.

## Write sequence

1. Re-read system status and the prepared order.
2. Confirm Bybit write capability, matching environment, enabled session, approval, expiry, connection generation, and hash.
3. Execute once.
4. Reconcile through prepared-order status, open orders, order history, execution history, and positions.
5. Report partial fills, remaining quantity, average price, normalized status, and uncertainty exactly.

## History sources

- `Exchange`: returned by the connected exchange history API.
- `TradeRelayObserved`: recorded from REST baselines, private streams, reconciliation, gates, and local control actions while TradeRelay was running.

Never infer missing exchange history from `TradeRelayObserved` events.

## Stable safety codes

- `TRADING_DISABLED`: desktop operator has not enabled writes for this session.
- `APPROVAL_REQUIRED`: prepared plan still needs desktop review.
- `LIVE_CONFIRMATION_REQUIRED`: destructive Live action ticket needs desktop approval.
- `CONNECTION_CHANGED`: credentials or authenticated session changed after preparation.
- `ORDER_STATE_UNKNOWN`: submission may have reached the exchange but reconciliation is inconclusive; never retry placement blindly.
- `AUDIT_UNAVAILABLE`: required pre-action audit cannot be guaranteed; writes are blocked.
- `CAPABILITY_NOT_SUPPORTED`: selected adapter does not implement the requested capability.
- `EXCHANGE_NOT_FOUND`: requested exchange is not registered.
- `EXCHANGE_NOT_CONNECTED`: authenticated provider session is not available.
- `AMBIGUOUS_EXCHANGE`: more than one provider could satisfy an omitted exchange selection.
