using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.ViewModels;

/// <summary>
/// Supplies the static Milestone 1 status shown by the desktop shell.
/// </summary>
public sealed class MainWindowViewModel
{
    private readonly AppSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="settings">The application's non-secret settings.</param>
    public MainWindowViewModel(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Gets the application name.
    /// </summary>
    public string ApplicationName => "TradeRelay";

    /// <summary>
    /// Gets the current development milestone label.
    /// </summary>
    public string DevelopmentStatus => "Milestone 1 · Application scaffold";

    /// <summary>
    /// Gets the safety status displayed by the initial shell.
    /// </summary>
    public string SafetyStatus => "Read-only foundation. Live trading is not implemented.";

    /// <summary>
    /// Gets the default exchange environment label.
    /// </summary>
    public string EnvironmentStatus => $"Default environment: {_settings.Bybit.Environment}";

    /// <summary>
    /// Gets the startup trading access mode.
    /// </summary>
    public string AccessStatus => $"Trading access: {TradingAccessMode.ReadOnly}";
}
