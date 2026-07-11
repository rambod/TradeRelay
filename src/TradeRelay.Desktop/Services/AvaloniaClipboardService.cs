using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace TradeRelay.Desktop.Services;

internal sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            throw new InvalidOperationException("The desktop window is not available.");
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
        if (clipboard is null)
        {
            throw new InvalidOperationException("The system clipboard is not available.");
        }

        await clipboard.SetTextAsync(text).ConfigureAwait(false);
    }
}
