using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.Services;

internal sealed class TradingGate(TradingControlService control, ExchangeConnectionManager connections, AuditLogService audit, AppSettings settings)
{
    public TradingGateResult Check(TradingAction action, PreparedOrder? order = null)
    {
        if (settings.Bybit.Environment != TradingEnvironment.Demo || connections.Snapshot.Environment != TradingEnvironment.Demo) return Deny("LIVE_NOT_ENABLED", "Live exchange writes are unavailable in Milestone 5.");
        if (!control.Snapshot.Enabled) return Deny("TRADING_DISABLED", "Enable Demo trading from the desktop before using write tools.");
        if (!audit.Health.Healthy) return Deny("AUDIT_UNAVAILABLE", "Activity auditing is unavailable; new write actions are blocked.");
        ApiCredentialInfo? credential = connections.Snapshot.CredentialInfo;
        if (credential is null || !connections.Snapshot.CredentialLoaded) return Deny("CREDENTIALS_MISSING", "Validated Demo credentials are required.");
        if (credential.IsReadOnly || !credential.HasTradingPermission) return Deny("READ_ONLY", "The active API key does not permit contract trading.");
        if (credential.HasWithdrawalPermission) return Deny("UNSAFE_API_PERMISSION", "Withdrawal-enabled credentials are blocked.");
        if (connections.Trading is null || connections.Account is null) return Deny("PROVIDER_UNAVAILABLE", "The authenticated Demo provider is unavailable.");
        if (order is not null && order.Environment != TradingEnvironment.Demo) return Deny("LIVE_NOT_ENABLED", "Only Demo prepared orders can execute.");
        return new(true, "OK", $"{action} is permitted in the current Demo session.");
    }

    private static TradingGateResult Deny(string code, string message) => new(false, code, message);
}

