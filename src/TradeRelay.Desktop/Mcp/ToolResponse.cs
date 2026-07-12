using TradeRelay.Core.Models;

namespace TradeRelay.Desktop.Mcp;

internal static class ToolResponse
{
    public static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    public static ToolResult<T> Success<T>(T data, string message, TradingEnvironment environment, TimeProvider timeProvider)
    {
        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        return new ToolResult<T>(true, "OK", message, data, Guid.NewGuid().ToString("N"), environment, timestamp);
    }

    public static ToolResult<T> Failure<T>(string code, string message, TradingEnvironment environment, TimeProvider timeProvider)
    {
        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        return new ToolResult<T>(false, code, message, default, Guid.NewGuid().ToString("N"), environment, timestamp);
    }

    public static ToolResult<T> Result<T>(bool success, string code, string message, T data, TradingEnvironment environment, TimeProvider timeProvider)
    {
        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        return new ToolResult<T>(success, code, message, data, Guid.NewGuid().ToString("N"), environment, timestamp);
    }

    public static ToolResult<T> Correlated<T>(bool success, string code, string message, T? data, string correlationId, TradingEnvironment environment, TimeProvider timeProvider) =>
        new(success, code, message, data, correlationId, environment, timeProvider.GetUtcNow());

    public static async Task<ToolResult<T>> RunAsync<T>(Func<CancellationToken, Task<T>> action, string message, TradingEnvironment environment, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        try { return Success(await action(cancellationToken).ConfigureAwait(false), message, environment, timeProvider); }
        catch (ProviderException exception) { return Failure<T>(exception.Code, exception.Message, environment, timeProvider); }
        catch (OperationCanceledException) { throw; }
        catch { return Failure<T>("INTERNAL_ERROR", "The operation failed. Review TradeRelay logs using the correlation ID.", environment, timeProvider); }
    }
}
