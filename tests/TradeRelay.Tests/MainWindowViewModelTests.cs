using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;
using TradeRelay.Desktop.ViewModels;
using Xunit;

namespace TradeRelay.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task TokenAndConfigurationCommands_AreSafeAndClipboardReady()
    {
        // Arrange
        await using TestServerContext context = TestServerContext.Create();
        var clipboard = new RecordingClipboardService();
        using var viewModel = new MainWindowViewModel(
            context.Settings,
            context.Host,
            context.TokenService,
            context.ConnectionManager,
            context.PreparedOrderStore,
            context.LiveConfirmations,
            new RiskViewModel(context.Settings, context.SettingsStore, context.RiskEngine),
            new ApprovalsViewModel(context.PreparedOrderStore, context.LiveConfirmations, new ImmediateUiDispatcher(), TimeProvider.System),
            new ActivityViewModel(context.AuditLog, new ImmediateUiDispatcher(), new RecordingShellService()),
            CreateSettings(context),
            context.TradingControl,
            context.AuditLog,
            context.Metadata,
            clipboard,
            new ImmediateUiDispatcher(),
            TimeProvider.System);
        string originalToken = context.TokenService.CurrentToken;

        // Act and assert: masked by default, reveal only on explicit action.
        Assert.Equal(LocalMcpTokenService.MaskedToken, viewModel.DisplayedToken);
        viewModel.ToggleTokenVisibilityCommand.Execute(null);
        Assert.Equal(originalToken, viewModel.DisplayedToken);

        viewModel.RotateTokenCommand.Execute(null);
        Assert.NotEqual(originalToken, viewModel.DisplayedToken);
        Assert.False(context.TokenService.IsValid(originalToken));

        await viewModel.CopyCodexConfigCommand.ExecuteAsync(null);
        Assert.Contains("TRADERELAY_MCP_TOKEN", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(context.TokenService.CurrentToken, clipboard.Text, StringComparison.Ordinal);

        await viewModel.CopyClaudeCodeCommand.ExecuteAsync(null);
        Assert.Contains("${TRADERELAY_MCP_TOKEN}", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(context.TokenService.CurrentToken, clipboard.Text, StringComparison.Ordinal);

        await viewModel.CopyTokenCommand.ExecuteAsync(null);
        Assert.Equal(context.TokenService.CurrentToken, clipboard.Text);
    }

    [Fact]
    public async Task ServerCommands_TrackLifecycleAndCanExecuteState()
    {
        // Arrange
        await using TestServerContext context = TestServerContext.Create();
        using var viewModel = new MainWindowViewModel(
            context.Settings,
            context.Host,
            context.TokenService,
            context.ConnectionManager,
            context.PreparedOrderStore,
            context.LiveConfirmations,
            new RiskViewModel(context.Settings, context.SettingsStore, context.RiskEngine),
            new ApprovalsViewModel(context.PreparedOrderStore, context.LiveConfirmations, new ImmediateUiDispatcher(), TimeProvider.System),
            new ActivityViewModel(context.AuditLog, new ImmediateUiDispatcher(), new RecordingShellService()),
            CreateSettings(context),
            context.TradingControl,
            context.AuditLog,
            context.Metadata,
            new RecordingClipboardService(),
            new ImmediateUiDispatcher(),
            TimeProvider.System);

        Assert.True(viewModel.StartServerCommand.CanExecute(null));
        Assert.False(viewModel.StopServerCommand.CanExecute(null));

        // Act
        await viewModel.StartServerCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(McpServerState.Running.ToString(), viewModel.ServerState);
        Assert.False(viewModel.StartServerCommand.CanExecute(null));
        Assert.True(viewModel.StopServerCommand.CanExecute(null));

        // Act
        await viewModel.StopServerCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(McpServerState.Stopped.ToString(), viewModel.ServerState);
        Assert.True(viewModel.StartServerCommand.CanExecute(null));
        Assert.False(viewModel.StopServerCommand.CanExecute(null));
    }

    [Fact]
    public async Task CredentialCommands_TestSaveMaskAndDeleteWithoutExposingSecret()
    {
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory());
        using var viewModel = new MainWindowViewModel(
            context.Settings,
            context.Host,
            context.TokenService,
            context.ConnectionManager,
            context.PreparedOrderStore,
            context.LiveConfirmations,
            new RiskViewModel(context.Settings, context.SettingsStore, context.RiskEngine),
            new ApprovalsViewModel(context.PreparedOrderStore, context.LiveConfirmations, new ImmediateUiDispatcher(), TimeProvider.System),
            new ActivityViewModel(context.AuditLog, new ImmediateUiDispatcher(), new RecordingShellService()),
            CreateSettings(context),
            context.TradingControl,
            context.AuditLog,
            context.Metadata,
            new RecordingClipboardService(),
            new ImmediateUiDispatcher(),
            TimeProvider.System);
        viewModel.ShowCredentialsCommand.Execute(null);
        viewModel.ApiKey = "test-api-key";
        viewModel.ApiSecret = "test-api-secret";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);
        Assert.Contains("Read-only", viewModel.PermissionSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("test-api-secret", viewModel.PermissionSummary, StringComparison.Ordinal);

        await viewModel.SaveCredentialsCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, viewModel.ApiKey);
        Assert.Equal(string.Empty, viewModel.ApiSecret);
        Assert.Equal("••••••-key", viewModel.SavedKeyPreview);
        Assert.Equal(ServiceHealthState.Healthy.ToString(), viewModel.RestHealth);

        await viewModel.DeleteCredentialsCommand.ExecuteAsync(null);
        Assert.Equal("None", viewModel.SavedKeyPreview);
        Assert.Contains("deleted", viewModel.CredentialActionStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoTradingCommands_RequireAcknowledgementAndServerStopDisablesSession()
    {
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "write-key", "write-secret", false, default)).Success);
        using MainWindowViewModel viewModel = Create(context);
        await viewModel.StartServerCommand.ExecuteAsync(null);

        await viewModel.EnableDemoTradingCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsDemoTradingEnabled);
        viewModel.TradingAcknowledged = true;
        await viewModel.EnableDemoTradingCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDemoTradingEnabled);
        Assert.Equal("TradingEnabled", viewModel.AccessStatus);

        await viewModel.StopServerCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsDemoTradingEnabled);
    }

    [Fact]
    public async Task LiveEnableDialog_RequiresExactPhraseAndShowsPersistentLiveState()
    {
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "live-key", "live-secret", false, default)).Success);
        using MainWindowViewModel viewModel = Create(context);
        await viewModel.StartServerCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLiveEnvironment);
        Assert.Contains("LIVE", viewModel.HeaderSafetyState, StringComparison.Ordinal);
        viewModel.OpenLiveEnableDialogCommand.Execute(null);
        Assert.True(viewModel.IsLiveEnableDialogOpen);
        viewModel.LiveConfirmationText = "enable live trading";
        Assert.False(viewModel.ConfirmLiveEnableCommand.CanExecute(null));
        viewModel.LiveConfirmationText = " ENABLE LIVE TRADING ";
        Assert.False(viewModel.ConfirmLiveEnableCommand.CanExecute(null));
        viewModel.LiveConfirmationText = "ENABLE LIVE TRADING";
        Assert.True(viewModel.ConfirmLiveEnableCommand.CanExecute(null));

        await viewModel.ConfirmLiveEnableCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsTradingEnabled);
        Assert.False(viewModel.IsLiveEnableDialogOpen);
        Assert.Contains("TRADING ENABLED", viewModel.HeaderSafetyState, StringComparison.Ordinal);

        await viewModel.DisableTradingCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsTradingEnabled);
        Assert.Equal(TradingSessionState.EmergencyDisabled, context.TradingControl.Snapshot.State);
    }

    [Fact]
    public async Task ActivityViewModel_FiltersLiveAuditEvents()
    {
        await using TestServerContext context = TestServerContext.Create();
        using var viewModel = new ActivityViewModel(context.AuditLog, new ImmediateUiDispatcher(), new RecordingShellService());
        await context.AuditLog.TryWriteAsync(context.AuditLog.Create("cancel_order", "cancel_reconciled", "OK", TradingEnvironment.Demo, "correlation", "BTCUSDT"), default);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsEmpty);
        viewModel.SymbolFilter = "ETH";
        Assert.True(viewModel.IsEmpty);
        viewModel.ClearFiltersCommand.Execute(null);
        Assert.False(viewModel.IsEmpty);
    }

    private static MainWindowViewModel Create(TestServerContext context) => new(
        context.Settings,
        context.Host,
        context.TokenService,
        context.ConnectionManager,
        context.PreparedOrderStore,
        context.LiveConfirmations,
        new RiskViewModel(context.Settings, context.SettingsStore, context.RiskEngine),
        new ApprovalsViewModel(context.PreparedOrderStore, context.LiveConfirmations, new ImmediateUiDispatcher(), TimeProvider.System),
        new ActivityViewModel(context.AuditLog, new ImmediateUiDispatcher(), new RecordingShellService()),
        CreateSettings(context),
        context.TradingControl,
        context.AuditLog,
        context.Metadata,
        new RecordingClipboardService(),
        new ImmediateUiDispatcher(),
        TimeProvider.System);

    private static SettingsViewModel CreateSettings(TestServerContext context) => new(
        context.Settings,
        context.SettingsStore,
        context.Host,
        context.TokenService,
        context.Paths,
        context.Metadata,
        new RecordingShellService(),
        new StubDiagnosticsExporter(),
        new ImmediateUiDispatcher());

    private sealed class RecordingClipboardService : IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class RecordingShellService : IDesktopShellService
    {
        public bool TryOpenFolder(string path, out string? error)
        {
            error = null;
            return true;
        }
    }

    private sealed class StubDiagnosticsExporter : IDiagnosticsExporter
    {
        public Task<DiagnosticsExportResult> ExportAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DiagnosticsExportResult(true, "OK", "Exported.", "/tmp/diagnostics.json"));
    }
}
