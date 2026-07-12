using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradeRelay.Desktop.ViewModels;
using TradeRelay.Desktop.Views;
using TradeRelay.Desktop.Services;
using TradeRelay.Core.Settings;
using TradeRelay.Core.Providers;
using TradeRelay.Core.Security;
using TradeRelay.Desktop.Security;
using TradeRelay.Providers.Bybit;
using TradeRelay.Core.Risk;

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

        builder.Services.AddSingleton<ApplicationDataPaths>();
        builder.Services.AddSingleton<ApplicationSettingsStore>();
        builder.Services.AddSingleton(services => services.GetRequiredService<ApplicationSettingsStore>().Load());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ApplicationMetadata>();
        builder.Services.AddSingleton<SessionCredentialStore>();
        builder.Services.AddSingleton<MacOsKeychainSecretStore>();
#pragma warning disable CA1416
        builder.Services.AddSingleton(services => new WindowsProtectedSecretStore(services.GetRequiredService<ApplicationDataPaths>().ProtectedDataDirectory));
#pragma warning restore CA1416
        builder.Services.AddSingleton<LinuxSecretServiceSecretStore>();
        builder.Services.AddSingleton<IProtectedSecretStore>(services =>
            OperatingSystem.IsMacOS() ? services.GetRequiredService<MacOsKeychainSecretStore>() :
            OperatingSystem.IsWindows() ? services.GetRequiredService<WindowsProtectedSecretStore>() :
            services.GetRequiredService<LinuxSecretServiceSecretStore>());
        builder.Services.AddSingleton<ICredentialStore>(services =>
            OperatingSystem.IsMacOS() ? new MacOsKeychainCredentialStore(services.GetRequiredService<MacOsKeychainSecretStore>()) :
            OperatingSystem.IsWindows() ? new WindowsProtectedCredentialStore(services.GetRequiredService<WindowsProtectedSecretStore>()) :
            new LinuxSecretServiceCredentialStore(services.GetRequiredService<LinuxSecretServiceSecretStore>()));
        builder.Services.AddSingleton<CredentialStoreCoordinator>();
        builder.Services.AddSingleton<IExchangeProviderFactory, BybitExchangeProviderFactory>();
        builder.Services.AddSingleton<RiskEngine>();
        builder.Services.AddSingleton<PreparedOrderStore>();
        builder.Services.AddSingleton<OrderPreparationService>();
        builder.Services.AddSingleton<LocalMcpTokenService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<LocalMcpTokenService>());
        builder.Services.AddSingleton<ExchangeConnectionManager>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ExchangeConnectionManager>());
        builder.Services.AddSingleton<LocalMcpServerHost>();
        builder.Services.AddHostedService(services =>
            services.GetRequiredService<LocalMcpServerHost>());
        builder.Services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        builder.Services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<RiskViewModel>();
        builder.Services.AddSingleton<ApprovalsViewModel>();
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
