using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TradeRelay.App.Views;

namespace TradeRelay.App;

/// <summary>
/// Initializes the Avalonia application and resolves its main window from dependency injection.
/// </summary>
public sealed partial class App(IServiceProvider services) : Application
{
    private readonly IServiceProvider _services =
        services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
