using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeRelay.Core.Settings;
using TradeRelay.Desktop.Services;

namespace TradeRelay.Tests;

internal sealed class TestServerContext : IAsyncDisposable
{
    public AppSettings Settings { get; }

    public LocalMcpTokenService TokenService { get; }

    public ApplicationMetadata Metadata { get; }

    public TimeProvider TimeProvider { get; }

    public LocalMcpServerHost Host { get; }

    public static TestServerContext Create(
        int port = 0,
        TimeProvider? timeProvider = null,
        ILogger<LocalMcpServerHost>? logger = null)
    {
        var settings = new AppSettings
        {
            Server = new ServerSettings
            {
                Port = port,
                StartAutomatically = false
            }
        };

        return new TestServerContext(
            settings,
            new LocalMcpTokenService(),
            new ApplicationMetadata(),
            timeProvider ?? TimeProvider.System,
            logger ?? NullLogger<LocalMcpServerHost>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopServerAsync();
    }

    private TestServerContext(
        AppSettings settings,
        LocalMcpTokenService tokenService,
        ApplicationMetadata metadata,
        TimeProvider timeProvider,
        ILogger<LocalMcpServerHost> logger)
    {
        Settings = settings;
        TokenService = tokenService;
        Metadata = metadata;
        TimeProvider = timeProvider;
        Host = new LocalMcpServerHost(
            settings,
            tokenService,
            metadata,
            timeProvider,
            logger);
    }
}
