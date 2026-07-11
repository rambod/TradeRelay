using Avalonia.Threading;

namespace TradeRelay.Desktop.Services;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
