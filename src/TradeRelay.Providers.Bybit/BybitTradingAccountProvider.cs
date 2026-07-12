using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using TradeRelay.Core.Models;
using TradeRelay.Core.Providers;

namespace TradeRelay.Providers.Bybit;

internal sealed class BybitTradingAccountProvider(IBybitRestClient client, TradingEnvironment environment, TimeProvider timeProvider) : ITradingAccountProvider
{
    public async Task<AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken)
    {
        BybitBalance balance = await GetUnifiedBalanceAsync(cancellationToken).ConfigureAwait(false);
        decimal equity = balance.TotalEquity ?? 0m;
        decimal? margin = equity > 0m && balance.TotalInitialMargin is not null ? balance.TotalInitialMargin.Value / equity * 100m : null;
        return new AccountSummary(equity, balance.TotalAvailableBalance ?? 0m, balance.TotalPerpUnrealizedPnl ?? 0m, margin, environment, timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken)
    {
        BybitBalance balance = await GetUnifiedBalanceAsync(cancellationToken).ConfigureAwait(false);
        return balance.Assets.Select(asset => new WalletBalance(asset.Asset, asset.Equity ?? 0m, asset.WalletBalance ?? 0m, asset.Free ?? asset.AvailableToWithdraw ?? 0m, asset.UnrealizedPnl ?? 0m, asset.UsdValue)).ToArray();
    }

    public async Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(string? symbol, CancellationToken cancellationToken)
    {
        symbol = string.IsNullOrWhiteSpace(symbol) ? null : BybitValidation.NormalizeSymbol(symbol);
        var result = await client.V5Api.Trading.GetPositionsAsync(Category.Linear, symbol: symbol, settleAsset: "USDT", limit: 200, ct: cancellationToken).ConfigureAwait(false);
        return BybitResult.Require(result).List.Where(position => position.Quantity != 0m).Select(position => new PositionSnapshot(
            position.Symbol, position.Side == PositionSide.Sell ? TradeSide.Sell : TradeSide.Buy, position.Quantity,
            position.AveragePrice ?? 0m, position.MarkPrice ?? 0m, position.Leverage ?? 0m, position.UnrealizedPnl ?? 0m,
            position.LiquidationPrice, position.StopLoss, position.TakeProfit, position.PositionIdx.ToString())).ToArray();
    }

    public async Task<IReadOnlyList<OrderSnapshot>> GetOpenOrdersAsync(string? symbol, CancellationToken cancellationToken)
    {
        symbol = string.IsNullOrWhiteSpace(symbol) ? null : BybitValidation.NormalizeSymbol(symbol);
        var result = await client.V5Api.Trading.GetOrdersAsync(Category.Linear, symbol: symbol, settleAsset: "USDT", limit: 50, openOnly: 0, ct: cancellationToken).ConfigureAwait(false);
        return BybitResult.Require(result).List.Select(order => new OrderSnapshot(
            order.OrderId, order.ClientOrderId, order.Symbol, order.Side == OrderSide.Sell ? TradeSide.Sell : TradeSide.Buy,
            order.OrderType.ToString(), order.Price, order.Quantity, order.QuantityFilled ?? 0m, order.Status.ToString(), order.ReduceOnly ?? false,
            new DateTimeOffset(DateTime.SpecifyKind(order.CreateTime, DateTimeKind.Utc)))).ToArray();
    }

    public async Task<ApiCredentialInfo> GetCredentialInfoAsync(CancellationToken cancellationToken)
    {
        var result = await client.V5Api.Account.GetApiKeyInfoAsync(cancellationToken).ConfigureAwait(false);
        BybitApiKeyInfo info = BybitResult.Require(result);
        bool trading = info.Permissions.ContractTrade.Length > 0;
        bool wallet = info.Permissions.Wallet.Length > 0;
        bool withdrawal = info.Permissions.Wallet.Any(permission => permission.Equals("Withdraw", StringComparison.OrdinalIgnoreCase));
        var warnings = new List<string>();
        if (environment == TradingEnvironment.Live) warnings.Add("This is a Live environment API key.");
        if (!info.Readonly) warnings.Add("The API key has read/write access.");
        if (info.Ips.Length == 0) warnings.Add("The API key is not IP-bound.");
        if (info.IsMaster) warnings.Add("The API key belongs to a master account.");
        if (info.DeadlineDays is > 0 and <= 7) warnings.Add("The API key expires within seven days.");
        if (info.Permissions.Spot.Length > 0 || info.Permissions.Options.Length > 0 || info.Permissions.Derivatives.Length > 0 || info.Permissions.Earn.Length > 0) warnings.Add("The API key has permissions beyond TradeRelay's USDT perpetual scope.");
        if (withdrawal) warnings.Add("Withdrawal permission is unsafe and is rejected by TradeRelay.");
        DateTimeOffset? expires = info.ExpireTime == default ? null : new DateTimeOffset(DateTime.SpecifyKind(info.ExpireTime, DateTimeKind.Utc));
        return new ApiCredentialInfo(info.Readonly, trading, wallet, withdrawal, info.Ips.Length > 0, info.DeadlineDays > 0 ? info.DeadlineDays : null, expires, info.IsMaster, environment, warnings);
    }

    private async Task<BybitBalance> GetUnifiedBalanceAsync(CancellationToken cancellationToken)
    {
        var result = await client.V5Api.Account.GetBalancesAsync(AccountType.Unified, ct: cancellationToken).ConfigureAwait(false);
        return BybitResult.Require(result).List.SingleOrDefault() ?? throw new ProviderException("PROVIDER_UNAVAILABLE", "Bybit returned no unified account balance.");
    }
}
