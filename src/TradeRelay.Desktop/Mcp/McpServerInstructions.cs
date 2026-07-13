namespace TradeRelay.Desktop.Mcp;

internal static class McpServerInstructions
{
    public const string Value =
        "TradeRelay is a local operator-controlled trading bridge. Begin with get_system_status and make the exchange and environment explicit. Read tools require traderelay.read; planning tools require traderelay.plan; write tools require traderelay.trade. Before any Demo or Live execution, call prepare_order, " +
        "present the full order and risk summary to the user, and obtain " +
        "approval through TradeRelay when required. Never request withdrawals, transfers, deposits, or credential data. Treat order " +
        "acknowledgements as provisional until TradeRelay reports a reconciled status. Respect rate limits. " +
        "Prefer read tools before write tools. Never infer missing symbol, side, quantity, stop loss, or environment. " +
        "Never enable trading on the user's behalf. Never call cancel-all unless the user explicitly asks. Live cancel-all and close-position actions require a separate short-lived desktop confirmation; poll only the read-only confirmation tool and never retry a destructive call before approval. Never repeatedly retry failed or ambiguous order execution. " +
        "Never claim an order is filled unless TradeRelay reports Filled. Never expose internal authentication data. " +
        "Use get_system_status before write operations, get_instrument_info before precision-sensitive quantities, " +
        "and validate_order or prepare_order before execution.";
}
