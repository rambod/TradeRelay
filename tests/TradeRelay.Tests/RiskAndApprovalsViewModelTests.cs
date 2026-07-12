using TradeRelay.Core.Models;
using TradeRelay.Core.Risk;
using TradeRelay.Desktop.ViewModels;
using Xunit;

namespace TradeRelay.Tests;

public sealed class RiskAndApprovalsViewModelTests
{
    [Fact]
    public async Task RiskViewModel_ValidatesSavesAndDiscardsSettings()
    {
        await using TestServerContext context = TestServerContext.Create();
        var viewModel = new RiskViewModel(context.Settings, context.SettingsStore, context.RiskEngine);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.MaxRiskPercent = "0";
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Contains("greater than 0", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.MaxRiskPercent = "0.5";
        viewModel.RequireStopLoss = false;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(.5m, context.Settings.Risk.MaxRiskPerTradePercent);
        Assert.Contains("unknown", viewModel.WarningMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsDirty);

        viewModel.MaxRiskPercent = "2";
        viewModel.DiscardCommand.Execute(null);
        Assert.Equal("0.5", viewModel.MaxRiskPercent);
    }

    [Fact]
    public void ApprovalsViewModel_TracksApprovesAndNeverDisplaysSecrets()
    {
        var store = new PreparedOrderStore(TimeProvider.System);
        var viewModel = new ApprovalsViewModel(store, new ImmediateDispatcher(), TimeProvider.System);
        try
        {
            PreparedOrderResult added = store.Add("ui-request", Validation(), TradingEnvironment.Live, new RiskSettingsSnapshot(["BTCUSDT"], 1m, 500m, 2, 3m, true, true, 120));
            Assert.True(added.Success);
            Assert.False(viewModel.IsEmpty);
            Assert.True(viewModel.ApproveCommand.CanExecute(null));
            Assert.DoesNotContain("secret", viewModel.SelectedWarnings, StringComparison.OrdinalIgnoreCase);

            viewModel.ApproveCommand.Execute(null);
            Assert.Equal(PreparedOrderState.Approved.ToString(), viewModel.SelectedState);
            Assert.False(viewModel.ApproveCommand.CanExecute(null));
            Assert.Contains("gated Demo execution", viewModel.ActionMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void ApprovalsViewModel_DisposeIsIdempotent()
    {
        var viewModel = new ApprovalsViewModel(
            new PreparedOrderStore(TimeProvider.System),
            new ImmediateDispatcher(),
            TimeProvider.System);

        viewModel.Dispose();
        viewModel.Dispose();
    }

    private static OrderValidationResult Validation()
    {
        var order = new NormalizedOrder("BTCUSDT", TradeSide.Buy, OrderType.Limit, 1m, 1m, 100m, 100m, 100m, 90m, 90m, 120m, 120m, 1m, new RiskEstimate(100m, 10m, 20m, 2m, 1m), null);
        return new(true, order, [], ["Simulation only"]);
    }

    private sealed class ImmediateDispatcher : TradeRelay.Desktop.Services.IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
