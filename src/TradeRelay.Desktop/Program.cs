using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradeRelay.Desktop.ViewModels;
using TradeRelay.Desktop.Views;
using TradeRelay.Desktop.Services;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using IHost host = CreateHost(args);
        host.Start();

        try
        {
            BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static IHost CreateHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSingleton(new AppSettings());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ApplicationMetadata>();
        builder.Services.AddSingleton<LocalMcpTokenService>();
        builder.Services.AddSingleton<LocalMcpServerHost>();
        builder.Services.AddHostedService(services =>
            services.GetRequiredService<LocalMcpServerHost>());
        builder.Services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        builder.Services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient(services =>
            new MainWindow(services.GetRequiredService<MainWindowViewModel>()));

        return builder.Build();
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => services.GetRequiredService<App>())
            .UsePlatformDetect()
            .LogToTrace();
}
