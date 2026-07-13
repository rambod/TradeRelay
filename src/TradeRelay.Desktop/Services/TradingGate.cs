using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.Services;

internal sealed class TradingGate(
    TradingControlService control,
    ExchangeConnectionManager connections,
    AuditLogService audit,
    AppSettings settings,
    RiskEngine riskEngine)
{
    public TradingGateResult Check(TradingAction action, PreparedOrder? order = null)
    {
        TradingEnvironment environment = settings.Bybit.Environment;
        ProviderConnectionSnapshot connection = connections.Snapshot;
        TradingSessionSnapshot session = control.Snapshot;

        if (!session.Enabled || session.State != TradingSessionState.Enabled)
            return Deny("TRADING_DISABLED", $"Enable {environment} trading from the desktop before using write tools.");
        if (session.Environment != environment || connection.Environment != environment)
            return Deny("ENVIRONMENT_MISMATCH", "The selected, connected, and enabled trading environments must match.");
        if (!audit.Health.Healthy)
            return Deny("AUDIT_UNAVAILABLE", "Activity auditing is unavailable; new write actions are blocked.");
        if (!riskEngine.ValidateSettings(settings.Risk).Valid)
            return Deny("VALIDATION_FAILED", "Risk settings are invalid; new write actions are blocked.");

        ApiCredentialInfo? credential = connection.CredentialInfo;
        if (credential is null || !connection.CredentialLoaded)
            return Deny("CREDENTIALS_MISSING", $"Validated {environment} credentials are required.");
        if (credential.Environment != environment)
            return Deny("ENVIRONMENT_MISMATCH", "The credential environment does not match the selected environment.");
        if (credential.IsReadOnly || !credential.HasTradingPermission)
            return Deny("READ_ONLY", "The active API key does not permit contract trading.");
        if (credential.HasWithdrawalPermission)
            return Deny("UNSAFE_API_PERMISSION", "Withdrawal-enabled credentials are blocked.");
        if (connections.Trading is null || connections.Account is null)
            return Deny("PROVIDER_UNAVAILABLE", $"The authenticated {environment} provider is unavailable.");

        if (order is not null)
        {
            if (order.Environment != environment)
                return Deny("ENVIRONMENT_MISMATCH", "The prepared plan belongs to a different environment.");
            if (order.ConnectionGenerationId != connection.ConnectionGenerationId)
                return Deny("CONNECTION_CHANGED", "The authenticated connection changed after this plan was prepared.");
        }

        return new(true, "OK", $"{action} is permitted in the current {environment} session.");
    }

    public (TradingGateResult Result, TradingWriteLease? Lease) TryEnter(TradingAction action, PreparedOrder? order = null)
    {
        TradingGateResult result = Check(action, order);
        if (!result.Allowed) return (result, null);
        TradingWriteLease? lease = control.TryBeginWrite();
        return lease is null
            ? (Deny("TRADING_DISABLED", "Trading was disabled before the write action could begin."), null)
            : (result, lease);
    }

    private static TradingGateResult Deny(string code, string message) => new(false, code, message);
}
