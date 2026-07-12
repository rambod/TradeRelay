namespace TradeRelay.Desktop.Mcp;

internal static class McpServerInstructions
{
    public const string Value =
        "TradeRelay is a local trading bridge. Read tools are safe. Live exchange writes are unavailable. Before any " +
        "Bybit Demo execution, call prepare_order, present the full order and risk summary to the user, and obtain " +
        "approval through TradeRelay when required. Never request withdrawals, transfers, deposits, or credential data. Treat order " +
        "acknowledgements as provisional until TradeRelay reports a reconciled status. Respect rate limits. " +
        "Prefer read tools before write tools. Never infer missing symbol, side, quantity, stop loss, or environment. " +
        "Never call cancel-all unless the user explicitly asks. Never repeatedly retry failed order execution. " +
        "Never claim an order is filled unless TradeRelay reports Filled. Never expose internal authentication data. " +
        "Use get_system_status before write operations, get_instrument_info before precision-sensitive quantities, " +
        "and validate_order or prepare_order before execution.";
}
