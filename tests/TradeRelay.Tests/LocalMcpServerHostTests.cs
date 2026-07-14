using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TradeRelay.Core.Models;
using TradeRelay.Desktop.Services;
using Xunit;

namespace TradeRelay.Tests;

public sealed class LocalMcpServerHostTests
{
    [Fact]
    public async Task StartAndStop_CanRepeatAndRemainLoopbackOnly()
    {
        await using TestServerContext context = TestServerContext.Create();

        await context.Host.StartServerAsync();
        McpServerSnapshot firstRun = context.Host.Snapshot;

        Assert.Equal(McpServerState.Running, firstRun.State);
        Assert.True(firstRun.Port > 0);
        Assert.Equal(IPAddress.Loopback.ToString(), new Uri(firstRun.Url).Host);

        await context.Host.StartServerAsync();
        Assert.Equal(firstRun, context.Host.Snapshot);

        await context.Host.StopServerAsync();
        Assert.Equal(McpServerState.Stopped, context.Host.Snapshot.State);

        await context.Host.StopServerAsync();
        Assert.Equal(McpServerState.Stopped, context.Host.Snapshot.State);

        await context.Host.StartServerAsync();
        Assert.Equal(McpServerState.Running, context.Host.Snapshot.State);

        await context.Host.StopServerAsync();
        Assert.Equal(McpServerState.Stopped, context.Host.Snapshot.State);
    }

    [Fact]
    public async Task McpEndpoint_RejectsUnauthorizedRequests()
    {
        await using TestServerContext context = TestServerContext.Create();
        await context.Host.StartServerAsync();
        using var client = new HttpClient();
        using var payload = new StringContent("{}", Encoding.UTF8, "application/json");

        using HttpResponseMessage missing = await client.PostAsync(context.Host.Snapshot.Url, payload);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("Bearer", missing.Headers.WwwAuthenticate.Single().Scheme);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, context.Host.Snapshot.Url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        invalidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        using HttpResponseMessage invalid = await client.SendAsync(invalidRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_RejectsUntrustedHostHeaders()
    {
        await using TestServerContext context = TestServerContext.Create();
        await context.Host.StartServerAsync();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, context.Host.Snapshot.Url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Host = "attacker.example";
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            context.TokenService.CurrentToken);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedMcpClient_CanInvokeSystemStatusWithoutReceivingToken()
    {
        await using TestServerContext context = TestServerContext.Create();
        await context.Host.StartServerAsync();
        await using McpClient client = await CreateClientAsync(
            context.Host.Snapshot.Url,
            context.TokenService.CurrentToken);

        Assert.Equal("TradeRelay", client.ServerInfo.Name);
        Assert.Equal(context.Metadata.Version, client.ServerInfo.Version);
        Assert.Contains("local operator-controlled trading bridge", client.ServerInstructions, StringComparison.OrdinalIgnoreCase);

        IList<McpClientTool> tools = await client.ListToolsAsync();
        McpClientTool statusTool = Assert.Single(tools, tool => tool.Name == "get_system_status");
        CallToolResult result = await statusTool.CallAsync();
        string structuredContent = result.StructuredContent?.GetRawText() ?? string.Empty;

        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Contains(context.Metadata.Version, structuredContent, StringComparison.Ordinal);
        Assert.Contains("Running", structuredContent, StringComparison.Ordinal);
        Assert.Contains("TradingDisabled", structuredContent, StringComparison.Ordinal);
        Assert.DoesNotContain(context.TokenService.CurrentToken, structuredContent, StringComparison.Ordinal);
        string[] expected = ["test_bybit_connection", "get_ticker", "get_candles", "get_instrument_info", "get_order_book", "get_account_summary", "get_wallet_balances", "get_positions", "get_open_orders", "get_risk_settings", "calculate_position_size", "validate_order", "prepare_order", "get_prepared_order", "get_pending_approvals", "execute_prepared_order", "cancel_order", "cancel_all_orders", "close_position", "set_trading_stop", "get_live_action_confirmation", "get_pending_live_confirmations", "get_exchanges", "get_order_history", "get_execution_history", "get_position_history", "get_runtime_errors"];
        foreach (string name in expected) Assert.Contains(tools, tool => tool.Name == name);
    }

    [Fact]
    public async Task AuthorizedMcpClient_CanInvokeEveryReadOnlyMilestoneThreeTool()
    {
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory());
        ExchangeConnectionResult saved = await context.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "test-key", "test-secret", false, default);
        Assert.True(saved.Success);
        await context.Host.StartServerAsync();
        await using McpClient client = await CreateClientAsync(context.Host.Snapshot.Url, context.TokenService.CurrentToken);
        IList<McpClientTool> tools = await client.ListToolsAsync();

        var calls = new (string Name, IReadOnlyDictionary<string, object?>? Arguments)[]
        {
            ("test_bybit_connection", null),
            ("get_ticker", new Dictionary<string, object?> { ["symbol"] = "BTCUSDT" }),
            ("get_candles", new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["interval"] = "15m", ["limit"] = 10 }),
            ("get_instrument_info", new Dictionary<string, object?> { ["symbol"] = "BTCUSDT" }),
            ("get_order_book", new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["depth"] = 25 }),
            ("get_account_summary", null),
            ("get_wallet_balances", null),
            ("get_positions", null),
            ("get_open_orders", null),
            ("get_risk_settings", null),
            ("calculate_position_size", new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["entryPrice"] = 100m, ["stopLoss"] = 90m, ["accountRiskPercent"] = .1m }),
            ("validate_order", new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["side"] = "Buy", ["orderType"] = "Limit", ["quantity"] = .1m, ["limitPrice"] = 100m, ["stopLoss"] = 90m, ["takeProfit"] = 120m }),
            ("prepare_order", new Dictionary<string, object?> { ["clientRequestId"] = "mcp-integration-1", ["symbol"] = "BTCUSDT", ["side"] = "Buy", ["orderType"] = "Limit", ["quantity"] = .1m, ["limitPrice"] = 100m, ["stopLoss"] = 90m, ["takeProfit"] = 120m }),
            ("get_pending_approvals", null)
        };

        foreach ((string name, IReadOnlyDictionary<string, object?>? arguments) in calls)
        {
            McpClientTool tool = Assert.Single(tools, item => item.Name == name);
            CallToolResult result = await tool.CallAsync(arguments);
            Assert.NotEqual(true, result.IsError);
            string json = result.StructuredContent?.GetRawText() ?? string.Empty;
            Assert.Contains("\"success\":true", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("test-secret", json, StringComparison.Ordinal);
        }

        PreparedOrder prepared = Assert.Single(context.PreparedOrderStore.GetAll());
        McpClientTool getPrepared = Assert.Single(tools, item => item.Name == "get_prepared_order");
        CallToolResult preparedResult = await getPrepared.CallAsync(new Dictionary<string, object?> { ["preparationId"] = prepared.PreparationId.ToString() });
        Assert.NotEqual(true, preparedResult.IsError);
        Assert.Contains(prepared.ClientOrderId, preparedResult.StructuredContent?.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TradingTools_RequireDesktopEnableAndExecuteDemoPlanOnce()
    {
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Demo, "write-key", "write-secret", false, default)).Success);
        await context.Host.StartServerAsync();
        await using McpClient client = await CreateClientAsync(context.Host.Snapshot.Url, context.TokenService.CurrentToken);
        IList<McpClientTool> tools = await client.ListToolsAsync();

        McpClientTool prepare = Assert.Single(tools, item => item.Name == "prepare_order");
        await prepare.CallAsync(new Dictionary<string, object?> { ["clientRequestId"] = "m5-execution", ["symbol"] = "BTCUSDT", ["side"] = "Buy", ["orderType"] = "Limit", ["quantity"] = .1m, ["limitPrice"] = 100m, ["stopLoss"] = 90m, ["takeProfit"] = 120m });
        PreparedOrder plan = Assert.Single(context.PreparedOrderStore.GetAll());
        McpClientTool execute = Assert.Single(tools, item => item.Name == "execute_prepared_order");

        CallToolResult disabled = await execute.CallAsync(new Dictionary<string, object?> { ["preparationId"] = plan.PreparationId.ToString() });
        Assert.Contains("TRADING_DISABLED", disabled.StructuredContent?.GetRawText(), StringComparison.Ordinal);
        Assert.True((await context.TradingControl.EnableAsync(true, true, default)).Allowed);

        CallToolResult executed = await execute.CallAsync(new Dictionary<string, object?> { ["preparationId"] = plan.PreparationId.ToString() });
        Assert.Contains("\"success\":true", executed.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PreparedOrderState.Executed, context.PreparedOrderStore.Get(plan.PreparationId)?.State);
        CallToolResult duplicate = await execute.CallAsync(new Dictionary<string, object?> { ["preparationId"] = plan.PreparationId.ToString() });
        Assert.Contains("DUPLICATE_REQUEST", duplicate.StructuredContent?.GetRawText(), StringComparison.Ordinal);

        CallToolResult unsupported = await Assert.Single(tools, item => item.Name == "cancel_all_orders").CallAsync(new Dictionary<string, object?> { ["confirm"] = true, ["exchange"] = "binance" });
        Assert.Contains("CAPABILITY_NOT_SUPPORTED", unsupported.StructuredContent?.GetRawText(), StringComparison.Ordinal);
        CallToolResult cancelled = await Assert.Single(tools, item => item.Name == "cancel_order").CallAsync(new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["exchangeOrderId"] = "exchange-1" });
        Assert.Contains("\"success\":true", cancelled.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        CallToolResult cancelAll = await Assert.Single(tools, item => item.Name == "cancel_all_orders").CallAsync(new Dictionary<string, object?> { ["confirm"] = true });
        Assert.Contains("\"success\":true", cancelAll.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        CallToolResult close = await Assert.Single(tools, item => item.Name == "close_position").CallAsync(new Dictionary<string, object?> { ["symbol"] = "BTCUSDT" });
        Assert.Contains("VALIDATION_FAILED", close.StructuredContent?.GetRawText(), StringComparison.Ordinal);
        CallToolResult stop = await Assert.Single(tools, item => item.Name == "set_trading_stop").CallAsync(new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["stopLoss"] = 90m });
        Assert.Contains("VALIDATION_FAILED", stop.StructuredContent?.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("write-secret", string.Join(string.Empty, (await context.AuditLog.LoadRecentAsync(default)).Events.Select(item => item.ProviderResult)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveTools_RequireSessionPlanAndDestructiveActionApprovalThroughOfficialClient()
    {
        var trading = new StubTradingProvider();
        OrderSnapshot order = new("order-1", "client-1", "BTCUSDT", TradeSide.Buy, "Limit", 100m, 1m, 0m, "New", false, DateTimeOffset.UtcNow);
        await using TestServerContext context = TestServerContext.Create(providerFactory: new SuccessfulTestProviderFactory(readOnly: false, trading: trading, orders: [order]));
        Assert.True((await context.ConnectionManager.SaveAsync(TradingEnvironment.Live, "live-key", "live-secret", false, default)).Success);
        await context.Host.StartServerAsync();
        await using McpClient client = await CreateClientAsync(context.Host.Snapshot.Url, context.TokenService.CurrentToken);
        IList<McpClientTool> tools = await client.ListToolsAsync();

        McpClientTool prepare = Assert.Single(tools, item => item.Name == "prepare_order");
        await prepare.CallAsync(new Dictionary<string, object?> { ["clientRequestId"] = "m6-live-plan", ["symbol"] = "BTCUSDT", ["side"] = "Buy", ["orderType"] = "Limit", ["quantity"] = .1m, ["limitPrice"] = 100m, ["stopLoss"] = 90m, ["takeProfit"] = 120m });
        PreparedOrder plan = Assert.Single(context.PreparedOrderStore.GetAll());
        McpClientTool execute = Assert.Single(tools, item => item.Name == "execute_prepared_order");
        Assert.Contains("TRADING_DISABLED", (await execute.CallAsync(new Dictionary<string, object?> { ["preparationId"] = plan.PreparationId.ToString() })).StructuredContent?.GetRawText(), StringComparison.Ordinal);
        Assert.True(context.PreparedOrderStore.Approve(plan.PreparationId, plan.ImmutableHash).Success);
        Assert.True((await context.TradingControl.EnableAsync(true, false, "ENABLE LIVE TRADING", default)).Allowed);
        Assert.Contains("\"success\":true", (await execute.CallAsync(new Dictionary<string, object?> { ["preparationId"] = plan.PreparationId.ToString() })).StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);

        McpClientTool cancelAll = Assert.Single(tools, item => item.Name == "cancel_all_orders");
        CallToolResult requested = await cancelAll.CallAsync(new Dictionary<string, object?> { ["confirm"] = true, ["clientRequestId"] = "m6-live-cancel-all" });
        string requestedJson = requested.StructuredContent?.GetRawText() ?? string.Empty;
        Assert.Contains("LIVE_CONFIRMATION_REQUIRED", requestedJson, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(requestedJson);
        Guid confirmationId = document.RootElement.GetProperty("data").GetProperty("confirmation").GetProperty("confirmationId").GetGuid();
        LiveActionConfirmation confirmation = Assert.IsType<LiveActionConfirmation>(context.LiveConfirmations.Get(confirmationId));
        Assert.True(context.LiveConfirmations.Approve(confirmationId, confirmation.ImmutableHash).Success);
        CallToolResult confirmed = await cancelAll.CallAsync(new Dictionary<string, object?> { ["confirm"] = true, ["clientRequestId"] = "m6-live-cancel-all", ["liveConfirmationId"] = confirmationId.ToString() });
        Assert.Contains("\"success\":true", confirmed.StructuredContent?.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, trading.CancelAllCount);

        context.TradingControl.Disable("Emergency test.", emergency: true);
        CallToolResult blocked = await Assert.Single(tools, item => item.Name == "cancel_order").CallAsync(new Dictionary<string, object?> { ["symbol"] = "BTCUSDT", ["exchangeOrderId"] = "order-1" });
        Assert.Contains("TRADING_DISABLED", blocked.StructuredContent?.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotateToken_RejectsOldTokenAndAcceptsNewToken()
    {
        await using TestServerContext context = TestServerContext.Create();
        await context.Host.StartServerAsync();
        string oldToken = context.TokenService.CurrentToken;
        string newToken = context.TokenService.Rotate();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using McpClient _ = await CreateClientAsync(context.Host.Snapshot.Url, oldToken);
        });

        await using McpClient client = await CreateClientAsync(context.Host.Snapshot.Url, newToken);
        IList<McpClientTool> tools = await client.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name == "get_system_status");
    }

    [Fact]
    public async Task StartServerAsync_WhenPortIsOccupied_FaultsSafelyAndCanRecover()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using TestServerContext context = TestServerContext.Create(port);

        await context.Host.StartServerAsync();

        Assert.Equal(McpServerState.Faulted, context.Host.Snapshot.State);
        Assert.Equal(
            $"The local MCP server could not start on port {port}. The port may already be in use.",
            context.Host.Snapshot.LastError);
        Assert.DoesNotContain(context.TokenService.CurrentToken, context.Host.Snapshot.LastError, StringComparison.Ordinal);

        listener.Stop();
        await context.Host.StartServerAsync();

        Assert.Equal(McpServerState.Running, context.Host.Snapshot.State);
    }

    [Fact]
    public async Task StopServerAsync_MakesEndpointUnavailable()
    {
        await using TestServerContext context = TestServerContext.Create();
        await context.Host.StartServerAsync();
        string endpoint = context.Host.Snapshot.Url;
        await context.Host.StopServerAsync();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        Exception? error = await Record.ExceptionAsync(() => client.GetAsync(endpoint));
        Assert.True(
            error is HttpRequestException or TaskCanceledException,
            $"Expected a connection failure after stopping MCP, but received {error?.GetType().Name ?? "no exception"}.");
    }

    [Fact]
    public async Task LifecycleLogs_DoNotContainAuthenticationData()
    {
        var logger = new RecordingLogger<LocalMcpServerHost>();
        await using TestServerContext context = TestServerContext.Create(logger: logger);

        await context.Host.StartServerAsync();
        await context.Host.StopServerAsync();
        string logs = string.Join(Environment.NewLine, logger.Messages);

        Assert.DoesNotContain(context.TokenService.CurrentToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", logs, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<McpClient> CreateClientAsync(string endpoint, string token)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}"
            }
        });

        return await McpClient.CreateAsync(transport);
    }
}
