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
using TradeRelay.Providers.Binance;
using TradeRelay.Providers.KuCoin;
using TradeRelay.Core.Risk;
using TradeRelay.Core.Models;

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
        builder.Services.AddSingleton<SensitiveDataRedactor>();
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
        builder.Services.AddSingleton<OAuthPairingService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<OAuthPairingService>());
        builder.Services.AddSingleton<IExchangeProviderFactory, BybitExchangeProviderFactory>();
        builder.Services.AddSingleton<BinanceExchangeProviderFactory>();
        builder.Services.AddSingleton<KuCoinExchangeProviderFactory>();
        builder.Services.AddSingleton<IExchangeProviderRegistry>(services => new ExchangeProviderRegistry(
        [
            services.GetRequiredService<IExchangeProviderFactory>(),
            services.GetRequiredService<BinanceExchangeProviderFactory>(),
            services.GetRequiredService<KuCoinExchangeProviderFactory>(),
        ]));
        builder.Services.AddSingleton<RiskEngine>();
        builder.Services.AddSingleton<PreparedOrderStore>();
        builder.Services.AddSingleton<LiveActionConfirmationStore>();
        builder.Services.AddSingleton<OrderPreparationService>();
        builder.Services.AddSingleton<SafeLogService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<SafeLogService>());
        builder.Services.AddSingleton<AuditLogService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<AuditLogService>());
        builder.Services.AddSingleton<TradingControlService>();
        builder.Services.AddSingleton<TradingGate>();
        builder.Services.AddSingleton<OrderExecutionService>();
        builder.Services.AddSingleton<LocalMcpTokenService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<LocalMcpTokenService>());
        builder.Services.AddSingleton<ExchangeConnectionManager>();
        builder.Services.AddSingleton<ExchangeSessionCoordinator>();
        builder.Services.AddSingleton<IExchangeSessionCoordinator>(services => services.GetRequiredService<ExchangeSessionCoordinator>());
        builder.Services.AddHostedService(services => services.GetRequiredService<ExchangeConnectionManager>());
        builder.Services.AddHostedService(services => services.GetRequiredService<ExchangeSessionCoordinator>());
        builder.Services.AddSingleton<TradingLifecycleService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<TradingLifecycleService>());
        builder.Services.AddSingleton<LocalMcpServerHost>();
        builder.Services.AddHostedService(services =>
            services.GetRequiredService<LocalMcpServerHost>());
        builder.Services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        builder.Services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        builder.Services.AddSingleton<IDesktopShellService, DesktopShellService>();
        builder.Services.AddSingleton<IDiagnosticsExporter, DiagnosticsExporter>();
        builder.Services.AddSingleton<IClientProcessRunner, ClientProcessRunner>();
        builder.Services.AddSingleton<IClientFileSystem, ClientFileSystem>();
        builder.Services.AddSingleton<AgentClientInstaller>();
        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<RiskViewModel>();
        builder.Services.AddSingleton<ApprovalsViewModel>();
        builder.Services.AddSingleton<ActivityViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton(services => new OperationsViewModel(
            services.GetRequiredService<IExchangeSessionCoordinator>(),
            services.GetRequiredService<AuditLogService>(),
            services.GetRequiredService<IUiDispatcher>(),
            services.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<AgentClientsViewModel>();
        builder.Services.AddSingleton<ProviderConnectionsViewModel>();
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
