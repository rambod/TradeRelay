using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradeRelay.App.ViewModels;
using TradeRelay.App.Views;
using TradeRelay.Core.Settings;

namespace TradeRelay.App;

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
        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient<MainWindow>();

        return builder.Build();
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => services.GetRequiredService<App>())
            .UsePlatformDetect()
            .LogToTrace();
}
