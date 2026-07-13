using System.Net;
using System.Net.Sockets;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;
using TradeRelay.Desktop.ViewModels;
using Xunit;

namespace TradeRelay.Tests;

public sealed class ProductionSettingsTests
{
    [Fact]
    public async Task Settings_ValidateTrackDirtyStateAndUpdateStoppedEndpoint()
    {
        await using TestServerContext context = TestServerContext.Create(port: GetAvailablePort());
        var shell = new RecordingShellService();
        var diagnostics = new RecordingDiagnosticsExporter();
        using var viewModel = Create(context, shell, diagnostics);

        Assert.False(viewModel.IsDirty);
        viewModel.McpPort = "80";
        Assert.True(viewModel.HasValidationError);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        int port = GetAvailablePort();
        viewModel.McpPort = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        viewModel.StartMcpAutomatically = true;
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(port, context.Settings.Server.Port);
        Assert.True(context.Settings.Server.StartAutomatically);
        Assert.Equal(port, context.Host.Snapshot.Port);
        Assert.False(viewModel.IsDirty);
        Assert.Contains(port.ToString(System.Globalization.CultureInfo.InvariantCulture), await File.ReadAllTextAsync(context.Paths.SettingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunningServerLocksPortAndAutomaticStartupNeverEnablesTrading()
    {
        int port = GetAvailablePort();
        await using TestServerContext context = TestServerContext.Create(port: port);
        context.Settings.Server.StartAutomatically = true;
        using var viewModel = Create(context, new RecordingShellService(), new RecordingDiagnosticsExporter());

        await context.Host.StartAsync(default);

        Assert.Equal(McpServerState.Running, context.Host.Snapshot.State);
        Assert.False(viewModel.IsPortEditingEnabled);
        Assert.False(context.TradingControl.Snapshot.Enabled);
        Assert.False(context.Host.TryUpdateConfiguredPort(GetAvailablePort()));
        viewModel.StartMcpAutomatically = false;
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.False(context.Settings.Server.StartAutomatically);
    }

    [Fact]
    public async Task AutomaticStartupFaultLeavesDesktopAvailableAndTradingDisabled()
    {
        using var conflict = new TcpListener(IPAddress.Loopback, 0);
        conflict.Start();
        int port = ((IPEndPoint)conflict.LocalEndpoint).Port;
        await using TestServerContext context = TestServerContext.Create(port: port);
        context.Settings.Server.StartAutomatically = true;

        await context.Host.StartAsync(default);

        Assert.Equal(McpServerState.Faulted, context.Host.Snapshot.State);
        Assert.Contains(port.ToString(System.Globalization.CultureInfo.InvariantCulture), context.Host.Snapshot.LastError, StringComparison.Ordinal);
        Assert.False(context.TradingControl.Snapshot.Enabled);
    }

    [Fact]
    public async Task SettingsCommandsUseAbstractionsRotateTokenAndExportDiagnostics()
    {
        await using TestServerContext context = TestServerContext.Create(port: GetAvailablePort());
        var shell = new RecordingShellService();
        var diagnostics = new RecordingDiagnosticsExporter();
        using var viewModel = Create(context, shell, diagnostics);
        string previous = context.TokenService.CurrentToken;

        viewModel.OpenApplicationDataFolderCommand.Execute(null);
        viewModel.OpenLogFolderCommand.Execute(null);
        await viewModel.RotateTokenCommand.ExecuteAsync(null);
        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Contains(context.Paths.Root, shell.Paths);
        Assert.Contains(context.Paths.LogsDirectory, shell.Paths);
        Assert.NotEqual(previous, context.TokenService.CurrentToken);
        Assert.False(context.TokenService.IsValid(previous));
        Assert.Equal(1, diagnostics.Calls);
    }

    private static SettingsViewModel Create(TestServerContext context, IDesktopShellService shell, IDiagnosticsExporter diagnostics) => new(
        context.Settings,
        context.SettingsStore,
        context.Host,
        context.TokenService,
        context.Paths,
        context.Metadata,
        shell,
        diagnostics,
        new ImmediateUiDispatcher());

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class RecordingShellService : IDesktopShellService
    {
        public List<string> Paths { get; } = [];
        public bool TryOpenFolder(string path, out string? error)
        {
            Paths.Add(path);
            error = null;
            return true;
        }
    }

    private sealed class RecordingDiagnosticsExporter : IDiagnosticsExporter
    {
        public int Calls { get; private set; }
        public Task<DiagnosticsExportResult> ExportAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new DiagnosticsExportResult(true, "OK", "Exported.", "/tmp/diagnostics.json"));
        }
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
