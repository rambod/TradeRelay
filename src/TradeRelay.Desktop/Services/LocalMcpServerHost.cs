using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using TradeRelay.Core.Models;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Mcp;
using TradeRelay.Desktop.Security;
using TradeRelay.Core.Risk;

namespace TradeRelay.Desktop.Services;

internal sealed class LocalMcpServerHost(
    AppSettings settings,
    LocalMcpTokenService tokenService,
    ApplicationMetadata metadata,
    TimeProvider timeProvider,
    ExchangeConnectionManager connectionManager,
    OrderPreparationService orderPreparationService,
    PreparedOrderStore preparedOrderStore,
    LiveActionConfirmationStore liveConfirmations,
    OrderExecutionService orderExecutionService,
    TradingControlService tradingControl,
    AuditLogService auditLog,
    SafeLogService safeLog,
    IExchangeProviderRegistry providerRegistry,
    OAuthPairingService oauthPairing,
    ILogger<LocalMcpServerHost> logger) : IHostedService
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private WebApplication? _application;
    private McpServerSnapshot _snapshot = CreateInitialSnapshot(settings.Server.Port);

    public event EventHandler<McpServerSnapshot>? StateChanged;

    public McpServerSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (settings.Server.StartAutomatically)
        {
            await StartServerAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public bool CanUpdateConfiguredPort => Snapshot.State is McpServerState.Stopped or McpServerState.Faulted;

    public bool TryUpdateConfiguredPort(int port)
    {
        if (port is < 1024 or > 65535 || !CanUpdateConfiguredPort) return false;
        settings.Server.Port = port;
        SetSnapshot(CreateInitialSnapshot(port));
        return true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        tradingControl.Disable("Application shutdown disabled all new trading actions.", emergency: true);
        preparedOrderStore.ExpireAllUnexecuted();
        liveConfirmations.ExpireAllUnexecuted();
        await tradingControl.WaitForActiveWritesAsync(ShutdownTimeout, cancellationToken).ConfigureAwait(false);
        await auditLog.TryWriteAsync(auditLog.Create("system", "application_shutdown", "OK", settings.Bybit.Environment, Guid.NewGuid().ToString("N")), cancellationToken).ConfigureAwait(false);
        await StopServerAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (Snapshot.State is McpServerState.Starting or McpServerState.Running)
            {
                return;
            }

            SetSnapshot(Snapshot with
            {
                State = McpServerState.Starting,
                LastError = null
            });

            await connectionManager.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            WebApplication application = BuildApplication();
            _application = application;

            try
            {
                await application.StartAsync(cancellationToken).ConfigureAwait(false);
                Uri endpoint = ResolveEndpoint(application);

                SetSnapshot(new McpServerSnapshot(
                    McpServerState.Running,
                    endpoint.AbsoluteUri,
                    endpoint.Port,
                    AuthenticationEnabled: true,
                    ConnectedSessionCount: null,
                    LastError: null));

                logger.LogInformation(
                    "Local MCP server started on loopback port {Port}",
                    endpoint.Port);
                await safeLog.TryWriteAsync(
                    SafeLogLevel.Information,
                    "MCP_STARTED",
                    "mcp.lifecycle",
                    "The local MCP server started on loopback.",
                    new Dictionary<string, string> { ["port"] = endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await auditLog.TryWriteAsync(auditLog.Create("system", "mcp_started", "OK", settings.Bybit.Environment, Guid.NewGuid().ToString("N")), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await DisposeApplicationAsync(application).ConfigureAwait(false);
                _application = null;
                SetSnapshot(Snapshot with { State = McpServerState.Stopped });
                throw;
            }
            catch (Exception exception)
            {
                await DisposeApplicationAsync(application).ConfigureAwait(false);
                _application = null;

                logger.LogError(
                    exception,
                    "Local MCP server failed to start on loopback port {Port}",
                    settings.Server.Port);

                await safeLog.TryWriteAsync(
                    SafeLogLevel.Error,
                    "MCP_START_FAILED",
                    "mcp.lifecycle",
                    "The local MCP server could not be started.",
                    new Dictionary<string, string> { ["port"] = settings.Server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    exception,
                    CancellationToken.None).ConfigureAwait(false);

                SetSnapshot(Snapshot with
                {
                    State = McpServerState.Faulted,
                    LastError = BuildSafeStartError(settings.Server.Port)
                });
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopServerAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (Snapshot.State is McpServerState.Stopped or McpServerState.Stopping)
            {
                return;
            }

            SetSnapshot(Snapshot with { State = McpServerState.Stopping });
            tradingControl.Disable("The local MCP server stopped; new trading actions were disabled.", emergency: true);
            await tradingControl.WaitForActiveWritesAsync(ShutdownTimeout, cancellationToken).ConfigureAwait(false);

            WebApplication? application = _application;
            _application = null;

            if (application is not null)
            {
                using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                shutdown.CancelAfter(ShutdownTimeout);

                try
                {
                    await application.StopAsync(shutdown.Token).ConfigureAwait(false);
                }
                finally
                {
                    await DisposeApplicationAsync(application).ConfigureAwait(false);
                }
            }

            await connectionManager.DisconnectAsync(cancellationToken).ConfigureAwait(false);

            SetSnapshot(Snapshot with
            {
                State = McpServerState.Stopped,
                LastError = null
            });

            logger.LogInformation("Local MCP server stopped");
            await safeLog.TryWriteAsync(SafeLogLevel.Information, "MCP_STOPPED", "mcp.lifecycle", "The local MCP server stopped.", cancellationToken: cancellationToken).ConfigureAwait(false);
            await auditLog.TryWriteAsync(auditLog.Create("system", "mcp_stopped", "OK", settings.Bybit.Environment, Guid.NewGuid().ToString("N")), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetSnapshot(Snapshot with
            {
                State = McpServerState.Faulted,
                LastError = "The local MCP server did not stop within the shutdown timeout."
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Local MCP server failed while stopping");
            await safeLog.TryWriteAsync(SafeLogLevel.Error, "MCP_STOP_FAILED", "mcp.lifecycle", "The local MCP server could not be stopped cleanly.", exception: exception, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            SetSnapshot(Snapshot with
            {
                State = McpServerState.Faulted,
                LastError = "The local MCP server could not be stopped cleanly."
            });
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private WebApplication BuildApplication()
    {
        var options = new WebApplicationOptions
        {
            ApplicationName = typeof(LocalMcpServerHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Production,
            Args = []
        };
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(options);

        builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost";
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, settings.Server.Port));

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(tokenService);
        builder.Services.AddSingleton(metadata);
        builder.Services.AddSingleton(timeProvider);
        builder.Services.AddSingleton(this);
        builder.Services.AddSingleton(connectionManager);
        builder.Services.AddSingleton(orderPreparationService);
        builder.Services.AddSingleton(preparedOrderStore);
        builder.Services.AddSingleton(liveConfirmations);
        builder.Services.AddSingleton(orderExecutionService);
        builder.Services.AddSingleton(tradingControl);
        builder.Services.AddSingleton(auditLog);
        builder.Services.AddSingleton(safeLog);
        builder.Services.AddSingleton<IExchangeProviderRegistry>(providerRegistry);
        builder.Services.AddSingleton(oauthPairing);

        builder.Services
            .AddMcpServer(mcp =>
            {
                mcp.ServerInfo = new Implementation
                {
                    Name = "TradeRelay",
                    Version = metadata.Version
                };
                mcp.ServerInstructions = McpServerInstructions.Value;
            })
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<SystemTools>()
            .WithTools<ConnectionTools>()
            .WithTools<MarketTools>()
            .WithTools<AccountTools>()
            .WithTools<RiskTools>()
            .WithTools<TradingTools>()
            .WithTools<OperationsTools>();

        WebApplication application = builder.Build();
        application.UseHostFiltering();
        application.UseMiddleware<McpSecurityMiddleware>();
        application.MapTradeRelayOAuth(oauthPairing);
        application.MapMcp("/mcp");

        return application;
    }

    private static Uri ResolveEndpoint(WebApplication application)
    {
        IServer server = application.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();
        string baseAddress = addresses?.Addresses.SingleOrDefault(address =>
            address.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The loopback server address was not available after startup.");

        return new Uri($"{baseAddress.TrimEnd('/')}/mcp", UriKind.Absolute);
    }

    private void SetSnapshot(McpServerSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);

        try
        {
            StateChanged?.Invoke(this, snapshot);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A local MCP server state listener failed");
        }
    }

    private static McpServerSnapshot CreateInitialSnapshot(int port) =>
        new(
            McpServerState.Stopped,
            BuildUrl(port),
            port,
            AuthenticationEnabled: true,
            ConnectedSessionCount: null,
            LastError: null);

    private static string BuildUrl(int port) => $"http://127.0.0.1:{port}/mcp";

    private static string BuildSafeStartError(int port) => port == 0
        ? "The local MCP server could not start on a temporary loopback port."
        : $"The local MCP server could not start on port {port}. The port may already be in use.";

    private static async ValueTask DisposeApplicationAsync(WebApplication application)
    {
        try
        {
            await application.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The original lifecycle error is more useful than a secondary disposal error.
        }
    }
}
