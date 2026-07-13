using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class AccountTools(IExchangeSessionCoordinator sessions, AppSettings settings, TimeProvider timeProvider)
{
    [McpServerTool(Name = "get_account_summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets a normalized account summary from the selected or explicit exchange.")]
    public Task<ToolResult<AccountSummary>> GetAccountSummaryAsync(string? exchange = null, CancellationToken cancellationToken = default) => Run(account => account.GetAccountSummaryAsync(cancellationToken), "Account summary retrieved.", exchange);

    [McpServerTool(Name = "get_wallet_balances", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets normalized non-zero balances from the selected or explicit exchange.")]
    public Task<ToolResult<IReadOnlyList<WalletBalance>>> GetWalletBalancesAsync(string? exchange = null, CancellationToken cancellationToken = default) => Run(account => account.GetBalancesAsync(cancellationToken), "Wallet balances retrieved.", exchange);

    [McpServerTool(Name = "get_positions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets open USDT perpetual positions, optionally filtered by symbol and exchange.")]
    public Task<ToolResult<IReadOnlyList<PositionSnapshot>>> GetPositionsAsync(string? symbol = null, string? exchange = null, CancellationToken cancellationToken = default) => Run(account => account.GetPositionsAsync(symbol, cancellationToken), "Open positions retrieved.", exchange);

    [McpServerTool(Name = "get_open_orders", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets open USDT perpetual orders, optionally filtered by symbol and exchange.")]
    public Task<ToolResult<IReadOnlyList<OrderSnapshot>>> GetOpenOrdersAsync(string? symbol = null, string? exchange = null, CancellationToken cancellationToken = default) => Run(account => account.GetOpenOrdersAsync(symbol, cancellationToken), "Open orders retrieved.", exchange);

    private Task<ToolResult<T>> Run<T>(Func<ITradingAccountProvider, Task<T>> action, string message, string? exchange)
    {
        if (!sessions.TryResolve(exchange, out ProviderSessionAccess? session, out string code, out string error) || session is null)
            return Task.FromResult(ToolResponse.Failure<T>(code, error, settings.Bybit.Environment, timeProvider));
        ITradingAccountProvider? account = session.Account;
        return account is null
            ? Task.FromResult(ToolResponse.Failure<T>("EXCHANGE_NOT_CONNECTED", $"Connect {session.Descriptor.DisplayName} in TradeRelay first.", session.Environment, timeProvider))
            : ToolResponse.RunAsync(_ => action(account), message, session.Environment, timeProvider, CancellationToken.None);
    }
}
