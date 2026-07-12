using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.Services;

internal sealed class TradingControlService : IDisposable
{
    private readonly ExchangeConnectionManager _connectionManager;
    private readonly RiskEngine _riskEngine;
    private readonly AppSettings _settings;
    private readonly TimeProvider _timeProvider;
    private TradingSessionSnapshot _snapshot;

    public TradingControlService(ExchangeConnectionManager connectionManager, RiskEngine riskEngine, AppSettings settings, TimeProvider timeProvider)
    {
        _connectionManager = connectionManager; _riskEngine = riskEngine; _settings = settings; _timeProvider = timeProvider;
        _snapshot = Disabled("Demo trading requires explicit session enablement.");
        _connectionManager.StateChanged += OnConnectionChanged;
    }

    public event EventHandler<TradingSessionSnapshot>? StateChanged;
    public TradingSessionSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task<TradingGateResult> EnableAsync(bool serverRunning, bool acknowledged, CancellationToken cancellationToken)
    {
        if (!acknowledged) return Denied("VALIDATION_FAILED", "Acknowledge that Demo trading sends real API write requests to the Bybit Demo account.");
        if (!serverRunning) return Denied("TRADING_DISABLED", "Start the local MCP server before enabling Demo trading.");
        ProviderConnectionSnapshot connection = _connectionManager.Snapshot;
        if (connection.Environment != TradingEnvironment.Demo) return Denied("LIVE_NOT_ENABLED", "Milestone 5 permits Demo trading only.");
        ApiCredentialInfo? credential = connection.CredentialInfo;
        if (!connection.CredentialLoaded || credential is null) return Denied("CREDENTIALS_MISSING", "Connect valid Bybit Demo credentials first.");
        if (credential.IsReadOnly || !credential.HasTradingPermission) return Denied("READ_ONLY", "A read/write API key with contract-trading permission is required.");
        if (credential.HasWithdrawalPermission) return Denied("UNSAFE_API_PERMISSION", "Withdrawal-enabled credentials cannot enable trading.");
        RiskSettingsValidationResult risk = _riskEngine.ValidateSettings(_settings.Risk);
        if (!risk.Valid) return Denied("VALIDATION_FAILED", "Risk settings must be valid before enabling Demo trading.");
        try { await (_connectionManager.Account ?? throw new ProviderException("CREDENTIALS_MISSING", "Connect Demo credentials first.")).GetAccountSummaryAsync(cancellationToken).ConfigureAwait(false); }
        catch (ProviderException exception) { return Denied(exception.Code, exception.Message); }
        Set(new(true, true, "Demo Trading Enabled", null, _timeProvider.GetUtcNow()));
        return new(true, "OK", "Demo trading is enabled for this application session.");
    }

    public void Disable(string reason = "Demo trading was disabled.") => Set(Disabled(reason));
    public void Dispose() => _connectionManager.StateChanged -= OnConnectionChanged;
    private void OnConnectionChanged(object? sender, ProviderConnectionSnapshot snapshot) { if (!snapshot.CredentialLoaded || snapshot.Environment != TradingEnvironment.Demo) Disable("Credential or environment state changed; Demo trading was disabled."); }
    private TradingGateResult Denied(string code, string message) { Set(new(false, false, "Demo Trading Disabled", message, _timeProvider.GetUtcNow())); return new(false, code, message); }
    private TradingSessionSnapshot Disabled(string reason) => new(false, false, "Demo Trading Disabled", reason, _timeProvider.GetUtcNow());
    private void Set(TradingSessionSnapshot snapshot) { Volatile.Write(ref _snapshot, snapshot); StateChanged?.Invoke(this, snapshot); }
}

