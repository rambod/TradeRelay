using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class AccountTools(ExchangeConnectionManager manager, AppSettings settings, TimeProvider timeProvider)
{
    [McpServerTool(Name = "get_account_summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets a normalized Bybit Unified Trading Account summary.")]
    public Task<ToolResult<AccountSummary>> GetAccountSummaryAsync(CancellationToken cancellationToken) => Run(account => account.GetAccountSummaryAsync(cancellationToken), "Account summary retrieved.");

    [McpServerTool(Name = "get_wallet_balances", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets normalized non-zero Bybit Unified Trading Account balances.")]
    public Task<ToolResult<IReadOnlyList<WalletBalance>>> GetWalletBalancesAsync(CancellationToken cancellationToken) => Run(account => account.GetBalancesAsync(cancellationToken), "Wallet balances retrieved.");

    [McpServerTool(Name = "get_positions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets open Bybit USDT perpetual positions, optionally filtered by symbol.")]
    public Task<ToolResult<IReadOnlyList<PositionSnapshot>>> GetPositionsAsync(string? symbol = null, CancellationToken cancellationToken = default) => Run(account => account.GetPositionsAsync(symbol, cancellationToken), "Open positions retrieved.");

    [McpServerTool(Name = "get_open_orders", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets open Bybit USDT perpetual orders, optionally filtered by symbol.")]
    public Task<ToolResult<IReadOnlyList<OrderSnapshot>>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default) => Run(account => account.GetOpenOrdersAsync(symbol, cancellationToken), "Open orders retrieved.");

    private Task<ToolResult<T>> Run<T>(Func<ITradingAccountProvider, Task<T>> action, string message)
    {
        ITradingAccountProvider? account = manager.Account;
        return account is null
            ? Task.FromResult(ToolResponse.Failure<T>("CREDENTIALS_MISSING", "Load and validate Bybit credentials in TradeRelay first.", settings.Bybit.Environment, timeProvider))
            : ToolResponse.RunAsync(_ => action(account), message, settings.Bybit.Environment, timeProvider, CancellationToken.None);
    }
}
