using System.ComponentModel;
using ModelContextProtocol.Server;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Core.Providers;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.Mcp;

[McpServerToolType]
internal sealed class OperationsTools(
    IExchangeProviderRegistry registry,
    ExchangeConnectionManager connections,
    AuditLogService audit,
    SafeLogService safeLog,
    AppSettings settings,
    TimeProvider timeProvider)
{
    [McpServerTool(Name = "get_exchanges", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists registered exchanges, environments, credential field labels, and normalized capabilities. No secrets are returned.")]
    public ToolResult<IReadOnlyList<ExchangeProviderDescriptor>> GetExchanges() =>
        ToolResponse.Success(registry.Descriptors, "Registered exchanges retrieved.", settings.Bybit.Environment, timeProvider);

    [McpServerTool(Name = "get_order_history", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets exchange-returned recent order history. This is distinct from TradeRelay-observed lifecycle activity.")]
    public Task<ToolResult<IReadOnlyList<HistoricalOrder>>> GetOrderHistoryAsync(string? symbol = null, string? exchange = null, int limit = 100, CancellationToken cancellationToken = default) =>
        RunHistoryAsync(provider => provider.GetOrderHistoryAsync(new ExchangeHistoryQuery(symbol, Limit: limit), cancellationToken), "Order history retrieved.", exchange);

    [McpServerTool(Name = "get_execution_history", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets exchange-returned recent fills. TradeRelay does not fabricate executions from audit events.")]
    public Task<ToolResult<IReadOnlyList<HistoricalExecution>>> GetExecutionHistoryAsync(string? symbol = null, string? exchange = null, int limit = 100, CancellationToken cancellationToken = default) =>
        RunHistoryAsync(provider => provider.GetExecutionHistoryAsync(new ExchangeHistoryQuery(symbol, Limit: limit), cancellationToken), "Execution history retrieved.", exchange);

    [McpServerTool(Name = "get_position_history", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gets position lifecycle changes actually observed by TradeRelay. It never claims to reconstruct activity from before observation began.")]
    public async Task<ToolResult<IReadOnlyList<AuditEvent>>> GetPositionHistoryAsync(string? symbol = null, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        AuditHistoryPage history = await audit.QueryAsync(null, null, page, pageSize, cancellationToken).ConfigureAwait(false);
        AuditEvent[] positions = history.Events.Where(item => item.Tool.Equals(nameof(TradingLifecycleKind.Position), StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(symbol) || string.Equals(item.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
        return ToolResponse.Success<IReadOnlyList<AuditEvent>>(positions, "TradeRelay-observed position history retrieved.", settings.Bybit.Environment, timeProvider);
    }

    [McpServerTool(Name = "get_runtime_errors", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Gets grouped safe runtime errors without raw exception messages, stacks, authenticated payloads, or secrets.")]
    public ToolResult<IReadOnlyList<RuntimeErrorSummary>> GetRuntimeErrors()
    {
        RuntimeErrorSummary[] groups = safeLog.GetRecentErrors(100)
            .GroupBy(item => $"{item.Code}|{item.Category}|{item.ExceptionType}", StringComparer.Ordinal)
            .Select(group =>
            {
                SafeDiagnosticError[] entries = group.OrderBy(item => item.TimestampUtc).ToArray();
                SafeDiagnosticError latest = entries[^1];
                return new RuntimeErrorSummary(latest.Code, latest.Category, latest.Properties.GetValueOrDefault("provider") ?? "TradeRelay", latest.Properties.GetValueOrDefault("action") ?? latest.Category, latest.ExceptionType, entries.Length, entries[0].TimestampUtc, latest.TimestampUtc, entries.Select(item => item.Properties.GetValueOrDefault("correlationId")).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().Distinct(StringComparer.Ordinal).Take(10).ToArray(), [], "Verify current provider and exchange state before retrying the affected action.");
            }).ToArray();
        return ToolResponse.Success<IReadOnlyList<RuntimeErrorSummary>>(groups, "Safe runtime errors retrieved.", settings.Bybit.Environment, timeProvider);
    }

    private Task<ToolResult<T>> RunHistoryAsync<T>(Func<IExchangeHistoryProvider, Task<T>> action, string message, string? exchange)
    {
        if (!string.IsNullOrWhiteSpace(exchange) && !exchange.Trim().Equals("bybit", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ToolResponse.Failure<T>("EXCHANGE_NOT_FOUND", "The requested exchange is not registered.", settings.Bybit.Environment, timeProvider));
        return connections.History is null
            ? Task.FromResult(ToolResponse.Failure<T>("EXCHANGE_NOT_CONNECTED", "Connect the selected exchange before requesting authenticated history.", settings.Bybit.Environment, timeProvider))
            : ToolResponse.RunAsync(_ => action(connections.History), message, settings.Bybit.Environment, timeProvider, CancellationToken.None);
    }
}
