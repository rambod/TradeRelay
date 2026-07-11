namespace TradeRelay.Desktop.Services;

internal interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
