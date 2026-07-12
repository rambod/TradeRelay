using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Desktop.ViewModels;

internal sealed partial class RiskViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly RiskEngine _riskEngine;
    private bool _loading;

    [ObservableProperty] private string _allowedSymbolsText = string.Empty;
    [ObservableProperty] private string _maxRiskPercent = string.Empty;
    [ObservableProperty] private string _maxNotionalUsd = string.Empty;
    [ObservableProperty] private string _maxOpenPositions = string.Empty;
    [ObservableProperty] private string _maxLeverage = string.Empty;
    [ObservableProperty] private string _expirySeconds = string.Empty;
    [ObservableProperty] private bool _requireStopLoss;
    [ObservableProperty] private bool _requireManualApprovalForDemo;
    [ObservableProperty] private bool _requireManualApprovalForLive;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _validationMessage = "Settings are using the conservative defaults.";
    [ObservableProperty] private string _warningMessage = string.Empty;

    public RiskViewModel(AppSettings settings, ApplicationSettingsStore settingsStore, RiskEngine riskEngine)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _riskEngine = riskEngine;
        Load();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildSettings(out RiskSettings? candidate, out string parseError))
        {
            ValidationMessage = parseError;
            return;
        }
        RiskSettingsValidationResult validation = _riskEngine.ValidateSettings(candidate);
        if (!validation.Valid)
        {
            ValidationMessage = string.Join(Environment.NewLine, validation.Errors.Select(error => $"• {error}"));
            WarningMessage = string.Join(Environment.NewLine, validation.Warnings.Select(warning => $"• {warning}"));
            return;
        }
        _settings.Risk = candidate;
        await _settingsStore.SaveAsync(_settings, cancellationToken);
        _loading = true;
        IsDirty = false;
        _loading = false;
        ValidationMessage = "Risk settings saved. Existing simulations keep their original settings snapshot.";
        WarningMessage = string.Join(Environment.NewLine, validation.Warnings.Select(warning => $"• {warning}"));
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Discard()
    {
        Load();
        ValidationMessage = "Unsaved changes discarded.";
    }

    partial void OnAllowedSymbolsTextChanged(string value) => MarkDirty();
    partial void OnMaxRiskPercentChanged(string value) => MarkDirty();
    partial void OnMaxNotionalUsdChanged(string value) => MarkDirty();
    partial void OnMaxOpenPositionsChanged(string value) => MarkDirty();
    partial void OnMaxLeverageChanged(string value) => MarkDirty();
    partial void OnExpirySecondsChanged(string value) => MarkDirty();
    partial void OnRequireStopLossChanged(bool value) => MarkDirty();
    partial void OnRequireManualApprovalForDemoChanged(bool value) => MarkDirty();
    partial void OnRequireManualApprovalForLiveChanged(bool value) => MarkDirty();

    private bool CanSave() => IsDirty;

    private void Load()
    {
        _loading = true;
        RiskSettings risk = _settings.Risk;
        AllowedSymbolsText = string.Join(Environment.NewLine, risk.AllowedSymbols.Order(StringComparer.Ordinal));
        MaxRiskPercent = Format(risk.MaxRiskPerTradePercent);
        MaxNotionalUsd = Format(risk.MaxOrderNotionalUsd);
        MaxOpenPositions = risk.MaxOpenPositions.ToString(CultureInfo.InvariantCulture);
        MaxLeverage = Format(risk.MaxLeverage);
        ExpirySeconds = risk.PreparedOrderExpirySeconds.ToString(CultureInfo.InvariantCulture);
        RequireStopLoss = risk.RequireStopLoss;
        RequireManualApprovalForDemo = risk.RequireManualApprovalForDemo;
        RequireManualApprovalForLive = risk.RequireManualApprovalForLive;
        IsDirty = false;
        WarningMessage = risk.RequireStopLoss ? string.Empty : "• Stop loss is optional. Plans without one have unknown estimated risk.";
        _loading = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        ValidationMessage = "Unsaved changes";
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool TryBuildSettings(out RiskSettings settings, out string error)
    {
        settings = null!;
        string[] symbols = AllowedSymbolsText.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(RiskEngine.NormalizeSymbol).Distinct(StringComparer.Ordinal).ToArray();
        if (!TryDecimal(MaxRiskPercent, out decimal risk) || !TryDecimal(MaxNotionalUsd, out decimal notional) || !int.TryParse(MaxOpenPositions, NumberStyles.Integer, CultureInfo.InvariantCulture, out int positions) || !TryDecimal(MaxLeverage, out decimal leverage) || !int.TryParse(ExpirySeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expiry))
        {
            error = "Enter valid numbers using a period as the decimal separator.";
            return false;
        }
        settings = new RiskSettings
        {
            AllowedSymbols = new HashSet<string>(symbols, StringComparer.Ordinal),
            MaxRiskPerTradePercent = risk,
            MaxOrderNotionalUsd = notional,
            MaxOpenPositions = positions,
            MaxLeverage = leverage,
            RequireStopLoss = RequireStopLoss,
            RequireManualApprovalForDemo = RequireManualApprovalForDemo,
            RequireManualApprovalForLive = RequireManualApprovalForLive,
            PreparedOrderExpirySeconds = expiry
        };
        error = string.Empty;
        return true;
    }

    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private static string Format(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
