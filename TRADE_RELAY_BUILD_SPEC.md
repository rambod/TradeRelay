# TradeRelay

A secure, local, open-source MCP desktop application that connects AI coding agents such as Codex and Claude Code to Bybit market data and controlled trading tools.

This document is the implementation brief for Codex. Read the entire document before writing code.

---

## 1. Working Rules for Codex

Build this project incrementally and keep it simple.

1. Do not invent additional services, microservices, databases, message brokers, or abstraction layers unless this document explicitly requires them.
2. Keep the solution compiling after every meaningful step.
3. Add tests for security gates, trading gates, and risk calculations before enabling live execution.
4. Use decimal values for prices, quantities, balances, percentages, and monetary calculations. Do not use `float` or `double` for trading values.
5. Do not log API keys, API secrets, bearer tokens, authorization headers, or raw credential objects.
6. Do not expose Bybit credentials through MCP tools, UI diagnostics, exceptions, crash logs, or configuration export.
7. Do not add withdrawal, deposit, transfer, P2P, earn, copy-trading, or asset-management tools.
8. Do not implement autonomous strategy loops. TradeRelay is a controlled bridge, not a trading bot.
9. Start with Bybit, but do not name core interfaces after Bybit.
10. Prefer readable code over clever code.
11. Keep classes focused, but do not split every small operation into a separate project or service.
12. Before adding a package, confirm it is actively maintained and necessary.
13. Use cancellation tokens for network operations and server shutdown.
14. Treat every exchange order response as provisional until order state is reconciled.
15. Never let an MCP client bypass UI mode, risk limits, or approval requirements.

---

## 2. Project Goal

TradeRelay runs only on the user's computer.

The user opens the desktop application, enters or loads their Bybit API credentials, chooses an environment, tests the connection, starts the local MCP server, and then connects Codex or Claude Code to it.

The application exposes:

- Read-only market data tools
- Read-only account tools
- Risk and position-size calculation tools
- Controlled order preparation tools
- Controlled order execution tools
- Order cancellation and position protection tools
- Local server status and diagnostics

The application must remain useful without live trading enabled.

---

## 3. Product Principles

### 3.1 Local first

The MCP server must run locally and bind only to:

```text
127.0.0.1
```

Do not bind to:

```text
0.0.0.0
```

Do not make remote hosting part of version 1.

### 3.2 Safe by default

Every application launch starts in:

```text
Read-only mode
```

Live trading must never remain enabled after restarting the application.

### 3.3 Human controlled

The AI can inspect data and prepare an order.

For live trading, execution requires explicit approval in the TradeRelay UI unless the user deliberately changes the safety setting. The default must remain manual approval.

### 3.4 Small scope

Do not build another exchange dashboard.

The UI is a control panel, not a charting terminal.

### 3.5 Provider ready, not provider overengineered

The first provider is Bybit.

Core trading interfaces must be exchange-neutral so future providers can be added without rewriting MCP tools. Do not create a plugin marketplace or dynamic assembly loading system in version 1.

---

## 4. Version 1 Scope

### Included

- Cross-platform desktop application
- Start and stop local MCP server
- Bybit API key and secret entry
- Session-only credentials
- Optional remembered credentials
- Bybit connection test
- Read-only, Demo, and Live environments
- Local bearer-token protection for the MCP endpoint
- Market data tools
- Account and position tools
- Risk validation
- Order preparation
- Manual approval queue
- Controlled order execution
- Cancel order
- Cancel all orders
- Close position with reduce-only behavior
- Set or update stop loss and take profit
- Local audit log
- Codex configuration instructions
- Claude Code configuration instructions
- Unit tests
- Optional integration tests against Bybit Demo

### Trading scope

For version 1:

- Support Bybit Unified Trading Account
- Support USDT perpetual contracts for account trading
- Support public market data for USDT perpetual contracts
- Spot market data may be included if it comes naturally from the selected library
- Do not implement spot order execution in version 1
- Do not implement inverse contracts
- Do not implement options
- Do not implement batch orders

### Excluded

- Automated strategies
- Scheduled trading
- Background cloud service
- Remote multi-user server
- Withdrawal
- Deposit
- Internal transfer
- Subaccount management
- Copy trading
- Earn products
- P2P
- Fiat functions
- Portfolio rebalancing
- Tax reporting
- Machine learning
- News sentiment
- Backtesting
- Charts
- SQLite or another database
- User accounts
- OAuth server
- Mobile application
- Browser application

---

## 5. Technology Decisions

Use:

```text
C#
.NET 10
Avalonia UI
ASP.NET Core
Official ModelContextProtocol C# SDK
ModelContextProtocol.AspNetCore
Bybit.Net by JKorf
Microsoft.Extensions.Hosting
Microsoft.Extensions.Logging
Microsoft.Extensions.Configuration
System.Text.Json
xUnit
```

Do not use Electron.

Do not use a web UI hosted inside the desktop app.

Do not use MediatR, MassTransit, Entity Framework, AutoMapper, or a repository pattern in version 1.

Use Avalonia MVVM. `CommunityToolkit.Mvvm` is acceptable if it reduces boilerplate.

---

## 6. Solution Structure

Keep the solution small:

```text
TradeRelay.sln

src/
  TradeRelay.Desktop/
  TradeRelay.Core/
  TradeRelay.Providers.Bybit/

tests/
  TradeRelay.Tests/
```

### 6.1 TradeRelay.Desktop

Responsibilities:

- Avalonia UI
- Application startup and shutdown
- Dependency injection
- Local settings loading and saving
- Credential storage selection
- MCP server hosting
- Bearer-token middleware
- MCP tool registration
- Approval queue UI
- Log display
- Client configuration helpers

The MCP host stays inside the desktop application. Do not create a separate executable for the HTTP server in version 1.

### 6.2 TradeRelay.Core

Responsibilities:

- Exchange-neutral models
- Provider interfaces
- Trading mode state
- Risk settings
- Risk validation
- Order preparation
- Approval state
- Tool result models
- Audit event models
- Common validation

This project must not reference Avalonia, ASP.NET Core, or Bybit.Net.

### 6.3 TradeRelay.Providers.Bybit

Responsibilities:

- Bybit.Net integration
- REST client
- WebSocket client
- Environment mapping
- Model mapping
- Order placement
- Order cancellation
- Position and order reconciliation
- API-key information lookup
- Instrument metadata lookup
- Bybit error normalization

Only this project may reference Bybit.Net.

### 6.4 TradeRelay.Tests

Responsibilities:

- Unit tests for risk validation
- Unit tests for mode gates
- Unit tests for approval gates
- Unit tests for decimal normalization
- Unit tests for secret redaction
- Unit tests for order-plan immutability
- Optional integration tests against Bybit Demo

---

## 7. Application Data

Store application data in the normal per-user application-data directory.

Suggested paths:

```text
Windows:
%LOCALAPPDATA%/TradeRelay/

macOS:
~/Library/Application Support/TradeRelay/

Linux:
~/.config/TradeRelay/
```

Files:

```text
settings.json
logs/traderelay-YYYY-MM-DD.log
audit/audit-YYYY-MM-DD.jsonl
```

Do not create a database in version 1.

### 7.1 settings.json

This file contains only non-secret settings.

Example:

```json
{
  "server": {
    "port": 5050,
    "startAutomatically": false
  },
  "bybit": {
    "environment": "Demo",
    "rememberCredentials": false
  },
  "risk": {
    "allowedSymbols": [
      "BTCUSDT",
      "ETHUSDT",
      "XRPUSDT"
    ],
    "maxRiskPerTradePercent": 0.25,
    "maxOrderNotionalUsd": 500,
    "maxOpenPositions": 2,
    "maxLeverage": 3,
    "requireStopLoss": true,
    "requireManualApprovalForDemo": false,
    "requireManualApprovalForLive": true,
    "preparedOrderExpirySeconds": 120
  }
}
```

Never add these properties to `settings.json`:

```text
apiKey
apiSecret
privateKey
bearerToken
authorization
password
```

### 7.2 Repository ignore rules

The repository must include:

```gitignore
**/bin/
**/obj/
.vs/
.idea/
.vscode/
*.user
*.suo
.env
.env.*
appsettings.Local.json
*.secrets.json
credentials.*
logs/
audit/
artifacts/
TestResults/
```

Application data should not be stored inside the repository directory.

---

## 8. Credential Handling

The user enters credentials inside the UI.

The UI must provide:

```text
API Key
API Secret
Remember credentials on this device
Test Connection
Save
Delete
```

The API secret field must be masked.

After saving, the UI must never display the complete secret again.

### 8.1 Storage modes

Provide two modes:

#### Session only

- Available on every platform
- Default for the first release
- Credentials stay in memory
- Credentials are removed when the app closes
- Credentials must not be written to logs, settings, audit files, or crash reports

#### Remember on this device

The app handles secure storage automatically. The user must not be instructed to manually create keychain entries.

Implement behind:

```csharp
public interface ICredentialStore
{
    Task SaveAsync(
        string id,
        ExchangeCredentials credentials,
        CancellationToken cancellationToken);

    Task<ExchangeCredentials?> LoadAsync(
        string id,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string id,
        CancellationToken cancellationToken);

    bool CanPersist { get; }
}
```

Implement:

```text
SessionCredentialStore
WindowsProtectedCredentialStore
MacOsKeychainCredentialStore
LinuxSecretServiceCredentialStore
```

Requirements:

- Windows may use DPAPI with current-user scope
- macOS may use the system Keychain through a small native wrapper or the `security` command
- Linux may use Secret Service through a maintained library or `secret-tool`
- If protected persistent storage is unavailable, fall back to session-only mode
- Do not silently save credentials as plaintext
- Do not add cloud vaults
- Do not add a master-password system in version 1

### 8.2 Credential lifetime

- Retrieve credentials only when constructing or refreshing the exchange client
- Avoid keeping extra copies
- Never serialize credential models
- Clear references during disconnect and shutdown where practical
- Do not include credential objects in exception messages
- Never return credentials from an MCP tool

### 8.3 Bybit API-key validation

After a successful connection, query the Bybit API-key information endpoint.

Display:

- Read-only or read/write
- Trading permissions
- Wallet permissions
- Withdrawal permission
- IP binding status
- Expiration or remaining valid days when available
- Master or subaccount status
- Environment

Reject the key if withdrawal permission is present.

Show a warning if:

- The key is a live key
- The key is read/write
- The key has no IP binding
- The key belongs to the master account
- The key expires soon
- The key has broader permissions than TradeRelay needs

Do not block read-only keys. They are valid for read-only mode.

---

## 9. Local MCP Authentication

The MCP server uses Streamable HTTP.

Default endpoint:

```text
http://127.0.0.1:5050/mcp
```

Requirements:

1. Bind only to `127.0.0.1`.
2. Reject requests whose remote address is not loopback.
3. Require an `Authorization: Bearer <token>` header.
4. Generate a cryptographically random local MCP token.
5. Store the token using the same protected credential mechanism used by the app.
6. If persistent protected storage is unavailable, generate a session token and show it in the UI.
7. Never place the token in repository files.
8. Never log the token.
9. Provide a button to rotate the token.
10. Rotating the token immediately invalidates the previous token.

Generate at least 32 random bytes:

```csharp
RandomNumberGenerator.GetBytes(32)
```

Encode with Base64Url or hexadecimal.

### 9.1 Server states

Use:

```csharp
public enum McpServerState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}
```

The UI must show:

- Current state
- URL
- Port
- Authentication enabled
- Connected MCP sessions if available
- Last error
- Start button
- Stop button
- Rotate token button
- Copy configuration button

### 9.2 Stop behavior

Stopping the MCP server:

- Stops new MCP requests
- Rejects new trade preparations
- Cancels in-progress non-execution requests when safe
- Waits briefly for a currently submitted order to reach a known state
- Persists the audit event
- Disconnects WebSockets
- Disposes exchange clients
- Stops Kestrel

Stopping the server must not:

- Close positions automatically
- Cancel open orders automatically
- Remove exchange-side stop losses
- Remove take profits

The UI must clearly state this.

---

## 10. Trading Modes

Use:

```csharp
public enum TradingEnvironment
{
    Demo,
    Live
}

public enum TradingAccessMode
{
    ReadOnly,
    TradingDisabled,
    TradingEnabled
}
```

Suggested behavior:

### ReadOnly

- Read tools enabled
- Calculation tools enabled
- Order validation enabled
- Order preparation may be allowed as a simulation
- Exchange write operations blocked

### Demo

- Uses Bybit Demo credentials and endpoints
- Trading may be enabled by the user
- Manual approval is configurable
- Default manual approval may be disabled for faster testing
- The UI must label all data and actions as Demo

### Live

- Uses Bybit live credentials and endpoints
- Starts with trading disabled every application launch
- Requires explicit UI action to enable trading
- Manual approval enabled by default
- The UI must display a persistent red Live indicator
- Live mode must never be hidden behind a generic environment label

### 10.1 Live enable flow

When the user enables live trading:

1. Confirm the MCP server is running.
2. Confirm credentials are valid.
3. Confirm the API key has no withdrawal permission.
4. Confirm risk settings are valid.
5. Confirm account data is reachable.
6. Show a confirmation dialog summarizing:
   - Environment
   - Maximum risk per trade
   - Maximum order notional
   - Maximum open positions
   - Maximum leverage
   - Manual approval state
7. Enable trading only for the current application session.

When the app restarts, live trading returns to disabled.

---

## 11. Core Interfaces

Keep interfaces small.

### 11.1 Market data

```csharp
public interface IMarketDataProvider
{
    Task<TickerSnapshot> GetTickerAsync(
        string symbol,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol,
        CandleInterval interval,
        int limit,
        CancellationToken cancellationToken);

    Task<InstrumentInfo> GetInstrumentInfoAsync(
        string symbol,
        CancellationToken cancellationToken);

    Task<OrderBookSnapshot> GetOrderBookAsync(
        string symbol,
        int depth,
        CancellationToken cancellationToken);
}
```

### 11.2 Account data

```csharp
public interface ITradingAccountProvider
{
    Task<AccountSummary> GetAccountSummaryAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(
        string? symbol,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(
        string? symbol,
        CancellationToken cancellationToken);

    Task<ApiCredentialInfo> GetCredentialInfoAsync(
        CancellationToken cancellationToken);
}
```

### 11.3 Trading

```csharp
public interface IExchangeTradingProvider
{
    Task<OrderSubmissionResult> PlaceOrderAsync(
        ValidatedOrder order,
        CancellationToken cancellationToken);

    Task<OrderSnapshot?> GetOrderAsync(
        string symbol,
        string exchangeOrderId,
        string? clientOrderId,
        CancellationToken cancellationToken);

    Task<OperationResult> CancelOrderAsync(
        string symbol,
        string exchangeOrderId,
        CancellationToken cancellationToken);

    Task<OperationResult> CancelAllOrdersAsync(
        string? symbol,
        CancellationToken cancellationToken);

    Task<OrderSubmissionResult> ClosePositionAsync(
        ClosePositionRequest request,
        CancellationToken cancellationToken);

    Task<OperationResult> SetTradingStopAsync(
        TradingStopRequest request,
        CancellationToken cancellationToken);
}
```

### 11.4 WebSocket state

```csharp
public interface IExchangeStream
{
    bool IsConnected { get; }

    event EventHandler<OrderUpdate>? OrderUpdated;
    event EventHandler<ExecutionUpdate>? ExecutionUpdated;
    event EventHandler<PositionUpdate>? PositionUpdated;

    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}
```

Do not expose Bybit.Net types outside `TradeRelay.Providers.Bybit`.

---

## 12. Core Models

Use records where they improve immutability.

Example:

```csharp
public sealed record PrepareOrderRequest(
    string Symbol,
    TradeSide Side,
    OrderType OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    decimal StopLoss,
    decimal? TakeProfit,
    decimal? RequestedLeverage,
    string? UserNote);

public sealed record ValidatedOrder(
    Guid PreparationId,
    string ClientOrderId,
    string Symbol,
    TradeSide Side,
    OrderType OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    decimal StopLoss,
    decimal? TakeProfit,
    decimal EstimatedEntryPrice,
    decimal EstimatedRiskUsd,
    decimal EstimatedRewardUsd,
    decimal RiskRewardRatio,
    decimal AccountRiskPercent,
    decimal EstimatedNotionalUsd,
    decimal Leverage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string ImmutableHash);

public sealed record OrderSubmissionResult(
    bool Accepted,
    bool Confirmed,
    string? ExchangeOrderId,
    string ClientOrderId,
    string Status,
    string Message,
    DateTimeOffset TimestampUtc);
```

### 12.1 Client order IDs

Every order must have a unique client order ID.

Suggested format:

```text
tr-<short-guid>
```

Keep within Bybit limits.

Use the client order ID for:

- Idempotency
- Duplicate prevention
- Audit correlation
- Reconciliation

---

## 13. Risk Engine

Risk rules are enforced inside TradeRelay.Core.

MCP tool descriptions are not security controls.

The provider adapter must never place an order that has not passed the risk engine.

### 13.1 Required settings

```csharp
public sealed class RiskSettings
{
    public IReadOnlySet<string> AllowedSymbols { get; init; }
    public decimal MaxRiskPerTradePercent { get; init; }
    public decimal MaxOrderNotionalUsd { get; init; }
    public int MaxOpenPositions { get; init; }
    public decimal MaxLeverage { get; init; }
    public bool RequireStopLoss { get; init; }
    public bool RequireManualApprovalForDemo { get; init; }
    public bool RequireManualApprovalForLive { get; init; }
    public int PreparedOrderExpirySeconds { get; init; }
}
```

### 13.2 Validation checks

Before preparing an order:

1. Symbol is normalized.
2. Symbol is allowed.
3. Side is valid.
4. Quantity is positive.
5. Order type is supported.
6. Limit order has a limit price.
7. Stop loss exists if required.
8. Stop loss is on the correct side of entry.
9. Take profit, when provided, is on the correct side of entry.
10. Quantity respects Bybit minimum quantity.
11. Quantity respects quantity step.
12. Price respects tick size.
13. Estimated notional does not exceed the configured maximum.
14. Requested leverage does not exceed the configured maximum.
15. Account risk percentage does not exceed the configured maximum.
16. Current open-position count does not exceed the configured maximum.
17. Trading environment and access mode permit the action.
18. Live trading has been explicitly enabled for the session.
19. Credentials permit trading.
20. No duplicate prepared order with the same client request identifier exists.

### 13.3 Risk calculation

For a linear contract, use a clear initial estimate:

```text
risk per unit = absolute(entry price - stop-loss price)

estimated risk = risk per unit * quantity

account risk percent = estimated risk / available account equity * 100
```

Document that fees, slippage, funding, liquidation behavior, gaps, and partial fills can make realized loss different from the estimate.

Do not present the estimate as a guaranteed maximum loss.

### 13.4 Instrument normalization

Before validation, fetch instrument metadata and normalize:

- Tick size
- Quantity step
- Minimum quantity
- Maximum quantity
- Minimum notional when applicable
- Maximum leverage

Never rely on hard-coded symbol precision.

### 13.5 Decimal rounding

Create explicit helpers:

```csharp
decimal RoundDownToStep(decimal value, decimal step);
decimal RoundToTick(decimal value, decimal tickSize);
```

Quantity should usually round down.

Price rounding depends on intent. Keep behavior explicit and tested.

---

## 14. Prepared Order and Approval Flow

Use a two-stage flow.

### Stage 1: Prepare

The MCP client calls:

```text
prepare_order
```

TradeRelay:

1. Validates input.
2. Fetches current instrument and account data.
3. Calculates risk.
4. Creates an immutable prepared-order object.
5. Assigns a preparation ID.
6. Assigns a client order ID.
7. Computes an immutable hash.
8. Sets an expiration time.
9. Adds it to an in-memory pending-order store.
10. Writes an audit event.
11. Returns the full plan.

### Stage 2: Approve and execute

For Live mode by default:

1. The UI shows the pending order.
2. The user reviews:
   - Symbol
   - Side
   - Order type
   - Quantity
   - Entry
   - Stop loss
   - Take profit
   - Estimated notional
   - Estimated risk
   - Account risk percentage
   - Leverage
   - Environment
3. The user clicks Approve or Reject.
4. Approval is bound to the preparation ID and immutable hash.
5. The MCP client calls `execute_prepared_order`, or the UI may provide a separate Execute button.
6. TradeRelay revalidates:
   - Order not expired
   - Hash unchanged
   - Approval present
   - Mode still permits trading
   - Live trading still enabled
   - Current open positions still within limits
   - Current price has not moved beyond configured tolerance when relevant
7. TradeRelay submits the order.
8. TradeRelay reconciles the order state.
9. TradeRelay removes the prepared order from the pending store.
10. TradeRelay writes an audit event.

An approved order must never be editable.

Any changed field requires a new prepared order and new approval.

### 14.1 Expiration

Default:

```text
120 seconds
```

Expired orders cannot execute.

### 14.2 Concurrency

Use a per-preparation lock or atomic state transition.

Valid states:

```csharp
public enum PreparedOrderState
{
    PendingApproval,
    Approved,
    Rejected,
    Executing,
    Executed,
    Failed,
    Expired
}
```

Only one transition from `Approved` to `Executing` may succeed.

This prevents duplicate execution when two MCP calls arrive together.

---

## 15. Order Submission and Reconciliation

Do not treat the first Bybit acknowledgement as final order state.

Required flow:

1. Generate or reuse the prepared client order ID.
2. Submit through Bybit.Net.
3. Record acknowledgement.
4. Wait for private order or execution WebSocket update.
5. If no update arrives within a short timeout, query order state through REST.
6. Return:
   - Accepted
   - Confirmed
   - Current status
   - Exchange order ID
   - Client order ID
   - Message
7. Audit every stage.

Possible statuses should be normalized into internal values such as:

```text
Pending
New
PartiallyFilled
Filled
Cancelled
Rejected
Unknown
```

If state remains unknown, return `Unknown`. Do not pretend success.

### 15.1 Duplicate prevention

Before submitting:

- Check that the prepared order has not already executed
- Check local state by client order ID
- When uncertainty exists after a timeout, query Bybit by exchange order ID or client order ID before retrying
- Never blindly retry order creation after an ambiguous network failure

### 15.2 Partial fills

Do not treat partial fill as full completion.

Return:

- Original quantity
- Filled quantity
- Remaining quantity
- Average fill price
- Current order status

### 15.3 WebSocket disconnects

When the private WebSocket disconnects:

- Mark stream health as degraded
- Do not automatically disable read tools
- Block new live execution if reliable reconciliation is unavailable
- Attempt reconnect using library-supported behavior
- Reconcile open orders and positions through REST after reconnect

---

## 16. MCP Server Instructions

Provide an MCP server-wide `instructions` value.

The first part must be self-contained and direct.

Suggested text:

```text
TradeRelay is a local trading bridge. Read tools are safe. Never execute a live trade without first calling prepare_order, presenting the full order and risk summary to the user, and obtaining approval through TradeRelay. Never request withdrawals, transfers, deposits, or credential data. Treat order acknowledgements as provisional until TradeRelay reports a reconciled status.
```

Continue with:

- Respect rate limits
- Prefer read tools before write tools
- Never infer missing symbol, side, quantity, stop loss, or environment
- Never call cancel-all unless the user explicitly asks
- Never repeatedly retry failed order execution
- Never claim an order is filled unless the tool reports `Filled`
- Never expose internal authentication data
- Use `get_system_status` before write operations
- Use `get_instrument_info` before suggesting precision-sensitive quantities
- Use `validate_order` or `prepare_order` before execution

---

## 17. MCP Tools

Organize tools into four classes:

```text
SystemTools
MarketTools
AccountTools
TradingTools
```

Do not create one class per tool.

All tools return structured objects, not long prose.

Use clear descriptions and annotate read-only versus destructive behavior using SDK-supported metadata where available.

### 17.1 System tools

#### `get_system_status`

Returns:

- App version
- Server state
- Server URL
- Environment
- Access mode
- Live trading enabled
- Manual approval required
- Bybit REST health
- Private WebSocket health
- Credential loaded
- Credential type summary
- Pending approval count
- Current UTC time

Do not return secrets.

#### `test_bybit_connection`

Returns:

- Success
- Environment
- Account mode
- Read-only status
- Trading permission status
- Withdrawal permission detected
- IP-bound status
- Expiration data
- Message

#### `get_risk_settings`

Returns effective risk settings.

#### `get_pending_approvals`

Returns pending prepared orders without any credentials or internal tokens.

### 17.2 Market tools

#### `get_ticker`

Input:

```text
symbol
```

Output:

- Last price
- Bid
- Ask
- High
- Low
- Volume
- Timestamp

#### `get_candles`

Input:

```text
symbol
interval
limit
```

Rules:

- Limit must have a safe upper bound
- Default limit should be small
- Return oldest-to-newest order consistently
- Include open time and close time

#### `get_instrument_info`

Output:

- Symbol
- Status
- Tick size
- Quantity step
- Minimum quantity
- Maximum quantity
- Minimum notional when available
- Maximum leverage
- Contract type

#### `get_order_book`

Input:

```text
symbol
depth
```

Use a conservative maximum depth in version 1.

### 17.3 Account tools

#### `get_account_summary`

Returns:

- Total equity
- Available balance
- Unrealized PnL
- Margin usage when available
- Environment
- Timestamp

#### `get_wallet_balances`

Returns normalized wallet balances.

#### `get_positions`

Optional symbol filter.

Returns:

- Symbol
- Side
- Size
- Entry price
- Mark price
- Leverage
- Unrealized PnL
- Liquidation price when available
- Stop loss when available
- Take profit when available
- Position mode

#### `get_open_orders`

Optional symbol filter.

Returns:

- Exchange order ID
- Client order ID
- Symbol
- Side
- Type
- Price
- Quantity
- Filled quantity
- Status
- Reduce-only
- Created time

### 17.4 Calculation and trading tools

#### `calculate_position_size`

Input:

```text
symbol
entryPrice
stopLoss
accountRiskPercent
```

Output:

- Raw quantity
- Normalized quantity
- Estimated notional
- Estimated risk
- Warnings

This tool never places an order.

#### `validate_order`

Input matches `PrepareOrderRequest`.

Output:

- Valid
- Errors
- Warnings
- Normalized values
- Estimated risk
- Estimated notional

This tool never stores or executes an order.

#### `prepare_order`

Creates an expiring immutable order plan.

It does not execute.

#### `get_prepared_order`

Input:

```text
preparationId
```

Returns current state and full order summary.

#### `execute_prepared_order`

Input:

```text
preparationId
```

Requirements:

- Write operation
- Requires trading enabled
- Requires current valid approval when configured
- Revalidates state
- Executes once only
- Reconciles before reporting final status

#### `cancel_order`

Input:

```text
symbol
exchangeOrderId
```

Requirements:

- Write operation
- Explicit order ID required
- No fuzzy matching

#### `cancel_all_orders`

Input:

```text
optional symbol
```

Requirements:

- Destructive operation
- Must require explicit user intent
- In Live mode, require UI confirmation
- Return count or provider result
- Never call as an automatic cleanup step

#### `close_position`

Input:

```text
symbol
optional quantity
```

Requirements:

- Use reduce-only behavior
- Never create a reverse position
- If quantity is omitted, close the full current position
- Validate actual current position first
- In Live mode, require UI confirmation by default

#### `set_trading_stop`

Input:

```text
symbol
stopLoss
optional takeProfit
```

Requirements:

- Validate against current position side
- Reject values on the wrong side
- Return normalized values and provider result

---

## 18. Tool Result Shape

Use a consistent envelope:

```csharp
public sealed record ToolResult<T>(
    bool Success,
    string Code,
    string Message,
    T? Data,
    string CorrelationId,
    TradingEnvironment Environment,
    DateTimeOffset TimestampUtc);
```

Example codes:

```text
OK
VALIDATION_FAILED
READ_ONLY
TRADING_DISABLED
LIVE_NOT_ENABLED
APPROVAL_REQUIRED
APPROVAL_REJECTED
ORDER_EXPIRED
DUPLICATE_REQUEST
RISK_LIMIT_EXCEEDED
CREDENTIALS_MISSING
CREDENTIALS_INVALID
UNSAFE_API_PERMISSION
PROVIDER_UNAVAILABLE
RATE_LIMITED
ORDER_STATE_UNKNOWN
INTERNAL_ERROR
```

Do not return raw stack traces through MCP.

---

## 19. Error Handling

Create a small internal exception set only where useful:

```text
ValidationException
RiskLimitException
TradingDisabledException
ApprovalRequiredException
CredentialException
ProviderException
```

At the MCP boundary:

- Convert known exceptions into stable tool errors
- Assign a correlation ID
- Log the correlation ID
- Return a safe message
- Never return stack traces
- Never return raw HTTP headers
- Never return request signatures
- Never return credentials

For unexpected errors:

```text
Code: INTERNAL_ERROR
Message: The operation failed. Review TradeRelay logs using the correlation ID.
```

---

## 20. Logging and Audit

Use normal application logs and a separate audit stream.

### 20.1 Application logs

Include:

- Startup
- Shutdown
- Server state
- Provider connection state
- WebSocket reconnect state
- Validation failures
- Normalized provider errors
- Correlation IDs

Do not include:

- API key
- API secret
- MCP bearer token
- Authorization header
- Bybit request signature
- Complete credential objects
- Raw request dumps from authenticated endpoints

### 20.2 Redaction

Create a redaction helper.

Redact values associated with keys containing:

```text
key
secret
token
authorization
signature
password
private
credential
```

Be careful not to destroy harmless fields such as `apiKeyExpiredAt`. Prefer explicit redaction for known sensitive values and headers.

Add tests proving secrets are not present in log output.

### 20.3 Audit log

Use append-only JSON Lines:

```text
audit/audit-YYYY-MM-DD.jsonl
```

Audit events include:

- Event ID
- Correlation ID
- Timestamp UTC
- Environment
- MCP client information when available
- Tool name
- Action
- Symbol
- Preparation ID
- Client order ID
- Exchange order ID
- Approval state
- Risk summary
- Provider result
- Final normalized status
- Error code

Do not include:

- API key
- API secret
- bearer token
- full authorization header
- raw signed request
- private key

Audit writes must not block the UI for long. A simple synchronized append is enough for version 1.

---

## 21. UI Requirements

Keep the UI clean and small.

Suggested navigation:

```text
Dashboard
Credentials
Risk
Audit
Settings
```

### 21.1 Dashboard

Show:

- TradeRelay status
- MCP server status
- Start MCP button
- Stop MCP button
- Endpoint URL
- Copy Codex config
- Copy Claude Code command
- Bybit environment
- Bybit connection status
- REST status
- WebSocket status
- Trading access mode
- Live trading enabled state
- Open positions count
- Open orders count
- Pending approvals count
- Last error

### 21.2 Credentials

Show:

- Environment selector
- API key
- API secret
- Remember checkbox
- Test Connection
- Save
- Delete
- Masked saved API-key preview
- Permission summary
- Warning messages

Do not expose the saved API secret.

### 21.3 Risk

Show editable settings:

- Allowed symbols
- Maximum risk per trade
- Maximum order notional
- Maximum open positions
- Maximum leverage
- Require stop loss
- Require manual approval for Demo
- Require manual approval for Live
- Prepared-order expiry

Validate before saving.

### 21.4 Pending approvals

Display as cards or a compact table.

Actions:

```text
Approve
Reject
View details
```

Approval details must show all immutable order fields.

### 21.5 Audit

Show recent events from the current session and recent JSONL entries.

Filters:

- Date
- Environment
- Tool
- Symbol
- Result

Do not build advanced reporting.

### 21.6 Settings

Include:

- MCP port
- Start server automatically
- Rotate MCP token
- Open app-data folder
- Open log folder
- Export non-secret diagnostics
- Application version

`Start server automatically` may start the MCP server, but must not enable live trading.

---

## 22. Bybit Adapter

Use `Bybit.Net`.

Create:

```text
BybitMarketDataProvider
BybitTradingAccountProvider
BybitTradingProvider
BybitExchangeStream
BybitEnvironmentFactory
BybitModelMapper
```

Do not expose Bybit.Net types outside the provider project.

### 22.1 Client creation

Create Bybit clients through dependency injection or a small factory.

Clients must be recreated when:

- Credentials change
- Environment changes
- User disconnects
- Credential storage is deleted

### 22.2 Environment mapping

Support:

```text
Demo
Live
```

Do not treat Demo and Testnet as the same environment.

Use the correct Bybit Demo endpoints.

Public market data in Demo may use mainnet public streams when required by Bybit.

### 22.3 REST and WebSocket

Use REST for:

- Initial account state
- Historical candles
- Instrument metadata
- Order placement in version 1
- Fallback reconciliation
- API-key information
- Open orders
- Position snapshots

Use private WebSocket for:

- Order updates
- Execution updates
- Position updates
- Reconciliation support

Do not use Bybit WebSocket trade order submission in version 1. REST submission is simpler.

### 22.4 Rate limits

- Respect library rate-limit handling
- Do not poll rapidly when a WebSocket stream can provide updates
- Use bounded retries only for safe read operations
- Do not blindly retry order placement
- Normalize rate-limit errors
- Surface retry-after or reset timing when available

---

## 23. Security Gates

Every write tool must pass through one centralized gate.

Example:

```csharp
public interface ITradingGate
{
    Task<TradingGateResult> CheckAsync(
        TradingAction action,
        CancellationToken cancellationToken);
}
```

Check:

1. Credentials loaded
2. Credentials valid
3. Withdrawal permission absent
4. Server running
5. Environment known
6. Trading access mode permits action
7. Live trading enabled when environment is Live
8. Manual approval satisfied when required
9. WebSocket or fallback reconciliation available
10. Risk settings loaded
11. No emergency disable state
12. Provider healthy enough for the action

Do not duplicate these checks differently inside each MCP tool.

### 23.1 Emergency disable

Provide a UI control:

```text
Disable New Trading Actions
```

This must immediately:

- Block prepare-to-execute transitions
- Block new write tool calls
- Leave read tools available
- Leave existing exchange orders and positions unchanged
- Write an audit event

Do not name this button `Close Everything`.

---

## 24. MCP Client Setup

### 24.1 Codex

The UI should generate a configuration snippet similar to:

```toml
[mcp_servers.traderelay]
url = "http://127.0.0.1:5050/mcp"
bearer_token_env_var = "TRADERELAY_MCP_TOKEN"
default_tools_approval_mode = "writes"
startup_timeout_sec = 10
tool_timeout_sec = 60
enabled = true
```

The user must set:

```text
TRADERELAY_MCP_TOKEN
```

The UI may offer:

- Copy token
- Copy config
- Open Codex config folder
- Show setup instructions

Do not automatically edit the user's Codex config in version 1.

### 24.2 Claude Code

The UI should generate a command similar to:

```bash
claude mcp add --transport http traderelay http://127.0.0.1:5050/mcp \
  --header "Authorization: Bearer YOUR_LOCAL_MCP_TOKEN"
```

Also generate a JSON example using an environment variable:

```json
{
  "mcpServers": {
    "traderelay": {
      "type": "http",
      "url": "http://127.0.0.1:5050/mcp",
      "headers": {
        "Authorization": "Bearer ${TRADERELAY_MCP_TOKEN}"
      }
    }
  }
}
```

Do not claim that Claude web custom connectors can access localhost. Version 1 targets Claude Code and local MCP-capable clients.

### 24.3 Client setup documentation

Create:

```text
docs/CODEX_SETUP.md
docs/CLAUDE_CODE_SETUP.md
```

Include:

- Start TradeRelay first
- Confirm server is running
- Copy endpoint
- Configure token
- Restart or reconnect MCP client
- Call `get_system_status`
- Troubleshooting for unauthorized and connection-refused errors

---

## 25. Shutdown and Lifecycle

On application shutdown:

1. Set emergency trading disable.
2. Reject new write operations.
3. Allow an active submitted order to complete reconciliation within a short timeout.
4. Expire all unexecuted prepared orders.
5. Write shutdown audit event.
6. Disconnect WebSockets.
7. Dispose REST clients.
8. Stop MCP host.
9. Release credential references.
10. Exit.

Do not close exchange positions or cancel exchange orders automatically.

---

## 26. Thread Safety

Use simple concurrency primitives.

Recommended:

- `SemaphoreSlim` for server start and stop
- `ConcurrentDictionary<Guid, PreparedOrder>` for pending orders
- Atomic state transition or per-order lock for execution
- A single synchronized audit writer
- Immutable prepared-order records
- Cancellation tokens for shutdown

Do not add distributed locks.

---

## 27. Testing Requirements

### 27.1 Unit tests

At minimum, test:

#### Risk

- Long stop loss below entry is valid
- Long stop loss above entry is invalid
- Short stop loss above entry is valid
- Short stop loss below entry is invalid
- Risk percentage calculation
- Maximum risk rejection
- Maximum notional rejection
- Maximum leverage rejection
- Disallowed symbol rejection
- Missing stop-loss rejection
- Quantity step normalization
- Tick-size normalization
- Maximum open-position rejection

#### Mode gates

- Read-only blocks execution
- Demo trading-disabled blocks execution
- Demo trading-enabled allows eligible execution
- Live trading disabled after startup
- Live trading requires explicit session enable
- Emergency disable blocks writes
- Read tools remain available when trading is disabled

#### Approval

- Live execution requires approval by default
- Rejected order cannot execute
- Expired order cannot execute
- Changed order hash cannot execute
- Duplicate execution is blocked
- Only one concurrent execution succeeds

#### Security

- Secret fields never serialize into settings
- Secret values are redacted from logs
- MCP token is never returned by tools
- Withdrawal permission causes credential rejection
- Non-loopback MCP request is rejected
- Missing bearer token is rejected
- Invalid bearer token is rejected

#### Provider mapping

- Bybit statuses map correctly
- Partial fills map correctly
- Provider errors map to stable internal codes

### 27.2 Integration tests

Integration tests must be opt-in.

Use environment variables such as:

```text
TRADERELAY_BYBIT_DEMO_API_KEY
TRADERELAY_BYBIT_DEMO_API_SECRET
```

Do not commit integration credentials.

Integration tests may cover:

- Connect to Demo
- Query API-key information
- Query account summary
- Query ticker
- Query instrument metadata
- Place a tiny Demo limit order away from market
- Confirm it appears
- Cancel it
- Confirm cancellation

Integration tests must never run against Live by default.

---

## 28. Diagnostics Export

Provide a non-secret diagnostics export.

Include:

- App version
- OS
- .NET version
- Environment name
- MCP state
- Server URL
- Bybit REST health
- WebSocket health
- Risk settings
- Recent normalized errors
- Package versions

Exclude:

- API key
- API secret
- MCP bearer token
- Authorization headers
- signatures
- raw authenticated request bodies

Before writing the file, run the redaction helper.

---

## 29. Open-Source Repository Files

Create:

```text
README.md
LICENSE
SECURITY.md
CONTRIBUTING.md
CODE_OF_CONDUCT.md
CHANGELOG.md
docs/CODEX_SETUP.md
docs/CLAUDE_CODE_SETUP.md
docs/SECURITY_MODEL.md
docs/DEVELOPMENT.md
```

Recommended license:

```text
MIT
```

### 29.1 README content

The README must include:

- What TradeRelay is
- What TradeRelay is not
- Supported clients
- Supported Bybit features
- Screenshot placeholder
- Installation
- First-run setup
- Credential safety
- Demo-first warning
- Live-trading warning
- MCP client setup
- Development instructions
- Contribution instructions
- License
- Disclaimer

### 29.2 Disclaimer

Include a clear disclaimer:

```text
TradeRelay is developer tooling, not financial advice. Trading involves risk. Users are responsible for API-key permissions, configuration, order review, and all resulting gains or losses. Use Demo mode first. Never use an API key with withdrawal permission.
```

Do not use the disclaimer as an excuse to remove technical safety controls.

### 29.3 SECURITY.md

Explain:

- How to report a vulnerability privately
- Never include real API credentials in reports
- Supported versions
- Credential storage model
- Localhost binding
- MCP authentication
- Secret redaction
- Live-trading safety model

---

## 30. Development Milestones

Build in this order.

### Milestone 1: Scaffold

- Create projects
- Add references
- Add Avalonia shell
- Add dependency injection
- Add settings model
- Add tests project
- Confirm build and tests run

Acceptance:

```text
dotnet build
dotnet test
```

Both succeed.

### Milestone 2: Desktop control panel and MCP host

- Dashboard
- Start and stop MCP server
- Loopback-only binding
- Bearer authentication
- `get_system_status`
- Token generation
- Server-state handling

Acceptance:

- Server starts and stops repeatedly
- Unauthorized requests fail
- Authorized MCP client sees `get_system_status`
- Non-loopback requests are rejected

### Milestone 3: Credentials and Bybit read-only connection

- Credential UI
- Session-only storage
- Protected persistent storage where available
- Bybit provider factory
- Test connection
- API-key information
- Market data tools
- Account tools
- WebSocket health

Acceptance:

- Demo credentials connect
- Read-only tools work
- Withdrawal-permission key is rejected
- No secrets appear in logs

### Milestone 4: Risk engine and preparation

- Risk settings UI
- Instrument normalization
- Position-size calculation
- Validate-order tool
- Prepare-order tool
- Pending-order store
- Expiration
- Approval UI
- Tests

Acceptance:

- Invalid orders cannot be prepared
- Risk limits are enforced
- Approval is immutable
- Expired orders cannot execute

### Milestone 5: Demo execution

- Demo trading enable
- Place order
- Reconcile order
- Cancel order
- Cancel all
- Close position
- Set trading stop
- Audit log
- Integration tests

Acceptance:

- A tiny Demo order can be prepared, approved, submitted, confirmed, and cancelled
- Duplicate execution is blocked
- Ambiguous submission does not trigger blind retry

### Milestone 6: Live safety

- Live environment
- Session-only live enable
- Persistent Live indicator
- Manual approval required by default
- Emergency disable
- Final security tests
- Documentation

Acceptance:

- Live execution is impossible immediately after app startup
- Live execution is impossible without explicit enable
- Live execution is impossible without approval by default
- Read tools continue working when trading is disabled

### Milestone 7: Open-source polish

- README
- SECURITY
- CONTRIBUTING
- Client setup docs
- Release packaging
- CI
- Version metadata
- Screenshots

---

## 31. Continuous Integration

Use GitHub Actions.

On pull requests and pushes:

```text
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

Do not run Bybit integration tests in normal CI.

Add optional manual workflow for Demo integration tests using repository secrets.

Never print secret environment variables.

---

## 32. Definition of Done for Version 1

Version 1 is complete only when:

- The desktop app runs on the primary supported operating systems
- MCP server starts and stops from the UI
- MCP binds only to loopback
- MCP requires bearer authentication
- Codex can connect
- Claude Code can connect
- Bybit Demo credentials can be entered through the UI
- Credentials are not committed or logged
- Read-only market tools work
- Account tools work
- Risk calculation works
- Prepared-order flow works
- Manual approval works
- Demo order execution works
- Order state is reconciled
- Duplicate execution is blocked
- Cancel order works
- Close position uses reduce-only behavior
- Stop loss and take profit update works
- Live mode starts disabled
- Live execution requires explicit session enable
- Live execution requires approval by default
- Withdrawal-permission keys are rejected
- Unit tests pass
- Documentation exists
- Security model is documented
- No unnecessary database or backend service was added

---

## 33. Initial Task for Codex

Start with Milestone 1 only.

Perform these steps:

1. Inspect the empty solution and repository.
2. Create the solution structure described in this document.
3. Add the minimum required NuGet packages.
4. Create a basic Avalonia desktop window.
5. Configure dependency injection and generic hosting.
6. Create placeholder core enums and settings models.
7. Create the tests project.
8. Add a root `.gitignore`.
9. Add a minimal `README.md` stating that the project is under active development and must not yet be used for live trading.
10. Run `dotnet build`.
11. Run `dotnet test`.
12. Fix all build and test failures.
13. Summarize exactly what was created.
14. Do not begin Bybit order execution until later milestones.

After Milestone 1 is working, continue one milestone at a time. Do not skip directly to live trading.

---

## 34. Official References

Use the current official documentation while implementing. Do not rely only on copied examples in this file.

- MCP C# SDK repository and documentation
  - https://github.com/modelcontextprotocol/csharp-sdk
  - https://csharp.sdk.modelcontextprotocol.io/

- MCP documentation
  - https://modelcontextprotocol.io/

- OpenAI Codex MCP documentation
  - https://developers.openai.com/codex/mcp

- Claude Code MCP documentation
  - https://code.claude.com/docs/en/mcp

- Bybit V5 API documentation
  - https://bybit-exchange.github.io/docs/v5/intro
  - https://bybit-exchange.github.io/docs/v5/guide
  - https://bybit-exchange.github.io/docs/v5/demo
  - https://bybit-exchange.github.io/docs/v5/user/apikey-info
  - https://bybit-exchange.github.io/docs/v5/websocket/trade/guideline
  - https://bybit-exchange.github.io/docs/v5/rate-limit

- Bybit.Net
  - https://github.com/JKorf/Bybit.Net

---

## 35. Final Constraint

Keep TradeRelay understandable by one developer.

A new contributor should be able to follow the flow:

```text
MCP tool
-> trading gate
-> validation and risk engine
-> approval
-> exchange provider
-> reconciliation
-> audit
```

If the architecture becomes harder to understand than that, simplify it before adding more code.
