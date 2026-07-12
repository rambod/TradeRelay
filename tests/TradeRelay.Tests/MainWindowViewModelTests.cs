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
}
