using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.Services;

internal sealed class TradingControlService : IDisposable
{
    public const string LiveConfirmationPhrase = "ENABLE LIVE TRADING";

    private readonly ExchangeConnectionManager _connectionManager;
    private readonly RiskEngine _riskEngine;
    private readonly AppSettings _settings;
    private readonly AuditLogService _audit;
    private readonly LiveActionConfirmationStore _liveConfirmations;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private TradingSessionSnapshot _snapshot;
    private TaskCompletionSource _idle = CompletedIdleSource();
    private int _activeWrites;

    public TradingControlService(
        ExchangeConnectionManager connectionManager,
        RiskEngine riskEngine,
        AppSettings settings,
        AuditLogService audit,
        LiveActionConfirmationStore liveConfirmations,
        TimeProvider timeProvider)
    {
        _connectionManager = connectionManager;
        _riskEngine = riskEngine;
        _settings = settings;
        _audit = audit;
        _liveConfirmations = liveConfirmations;
        _timeProvider = timeProvider;
        _snapshot = Disabled(settings.Bybit.Environment, "Trading requires explicit session enablement.", TradingSessionState.Disabled);
        _connectionManager.StateChanged += OnConnectionChanged;
        _audit.HealthChanged += OnAuditHealthChanged;
    }

    public event EventHandler<TradingSessionSnapshot>? StateChanged;
    public TradingSessionSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task<TradingGateResult> EnableAsync(
        bool serverRunning,
        bool demoAcknowledged,
        string? liveConfirmationPhrase,
        CancellationToken cancellationToken)
    {
        TradingEnvironment environment = _settings.Bybit.Environment;
        Set(new(false, false, $"{environment} Trading Enabling", null, _timeProvider.GetUtcNow(), environment, TradingSessionState.Enabling));

        if (environment == TradingEnvironment.Demo && !demoAcknowledged)
            return Denied(environment, "VALIDATION_FAILED", "Acknowledge that Demo trading sends real API write requests to the Bybit Demo account.");
        if (environment == TradingEnvironment.Live && !string.Equals(liveConfirmationPhrase, LiveConfirmationPhrase, StringComparison.Ordinal))
            return Denied(environment, "LIVE_CONFIRMATION_REQUIRED", $"Type {LiveConfirmationPhrase} exactly to enable Live trading.");
        if (!serverRunning) return Denied(environment, "TRADING_DISABLED", $"Start the local MCP server before enabling {environment} trading.");
        if (!_audit.Health.Healthy) return Denied(environment, "AUDIT_UNAVAILABLE", "Activity auditing must be healthy before trading can be enabled.");

        ProviderConnectionSnapshot connection = _connectionManager.Snapshot;
        if (connection.Environment != environment) return Denied(environment, "ENVIRONMENT_MISMATCH", "The authenticated provider environment does not match the selected environment.");
        ApiCredentialInfo? credential = connection.CredentialInfo;
        if (!connection.CredentialLoaded || credential is null) return Denied(environment, "CREDENTIALS_MISSING", $"Connect valid Bybit {environment} credentials first.");
        if (credential.Environment != environment) return Denied(environment, "ENVIRONMENT_MISMATCH", "The loaded credential metadata does not match the selected environment.");
        if (credential.IsReadOnly || !credential.HasTradingPermission) return Denied(environment, "READ_ONLY", "A read/write API key with contract-trading permission is required.");
        if (credential.HasWithdrawalPermission) return Denied(environment, "UNSAFE_API_PERMISSION", "Withdrawal-enabled credentials cannot enable trading.");

        RiskSettingsValidationResult risk = _riskEngine.ValidateSettings(_settings.Risk);
        if (!risk.Valid) return Denied(environment, "VALIDATION_FAILED", "Risk settings must be valid before trading can be enabled.");
        if (_connectionManager.Trading is null || _connectionManager.Account is null)
            return Denied(environment, "PROVIDER_UNAVAILABLE", "The authenticated trading provider is unavailable.");

        try
        {
            await _connectionManager.Account.GetAccountSummaryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderException exception)
        {
            return Denied(environment, exception.Code, exception.Message);
        }

        Guid sessionId = Guid.NewGuid();
        Set(new(true, true, $"{environment} Trading Enabled", null, _timeProvider.GetUtcNow(), environment, TradingSessionState.Enabled, sessionId));
        return new(true, "OK", $"{environment} trading is enabled for this application session.");
    }

    public Task<TradingGateResult> EnableAsync(bool serverRunning, bool demoAcknowledged, CancellationToken cancellationToken) =>
        EnableAsync(serverRunning, demoAcknowledged, null, cancellationToken);

    public void Disable(string reason = "Trading was disabled.", bool emergency = false)
    {
        TradingEnvironment environment = _settings.Bybit.Environment;
        _liveConfirmations.ExpireAllUnexecuted();
        Set(Disabled(environment, reason, emergency ? TradingSessionState.EmergencyDisabled : TradingSessionState.Disabled));
    }

    public TradingWriteLease? TryBeginWrite()
    {
        lock (_sync)
        {
            if (!_snapshot.Enabled || _snapshot.State != TradingSessionState.Enabled) return null;
            if (_activeWrites == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeWrites++;
            return new TradingWriteLease(EndWrite);
        }
    }

    public async Task<bool> WaitForActiveWritesAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task idle;
        lock (_sync) idle = _idle.Task;
        try
        {
            await idle.WaitAsync(timeout, _timeProvider, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _connectionManager.StateChanged -= OnConnectionChanged;
        _audit.HealthChanged -= OnAuditHealthChanged;
    }

    private void OnConnectionChanged(object? sender, ProviderConnectionSnapshot snapshot)
    {
        TradingSessionSnapshot trading = Snapshot;
        if (!trading.Enabled) return;
        if (!snapshot.CredentialLoaded || snapshot.Environment != trading.Environment)
            Disable("Credential or environment state changed; new trading actions were disabled.", emergency: true);
    }

    private void OnAuditHealthChanged(object? sender, AuditHealthSnapshot health)
    {
        if (!health.Healthy && Snapshot.Enabled)
            Disable("Activity auditing became unavailable; new trading actions were disabled.", emergency: true);
    }

    private TradingGateResult Denied(TradingEnvironment environment, string code, string message)
    {
        Set(Disabled(environment, message, TradingSessionState.Disabled));
        return new(false, code, message);
    }

    private TradingSessionSnapshot Disabled(TradingEnvironment environment, string reason, TradingSessionState state) =>
        new(false, false, $"{environment} Trading Disabled", reason, _timeProvider.GetUtcNow(), environment, state);

    private void Set(TradingSessionSnapshot snapshot)
    {
        lock (_sync) Volatile.Write(ref _snapshot, snapshot);
        try { StateChanged?.Invoke(this, snapshot); } catch { }
    }

    private void EndWrite()
    {
        TaskCompletionSource? completed = null;
        lock (_sync)
        {
            if (_activeWrites > 0) _activeWrites--;
            if (_activeWrites == 0) completed = _idle;
        }
        completed?.TrySetResult();
    }

    private static TaskCompletionSource CompletedIdleSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

internal sealed class TradingWriteLease(Action release) : IDisposable
{
    private Action? _release = release;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
