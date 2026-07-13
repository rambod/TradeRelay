using System.Diagnostics;

namespace TradeRelay.Desktop.Services;

internal interface IDesktopShellService
{
    bool TryOpenFolder(string path, out string? error);
}

internal sealed class DesktopShellService : IDesktopShellService
{
    public bool TryOpenFolder(string path, out string? error)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            error = null;
            return true;
        }
        catch
        {
            error = "The folder could not be opened.";
            return false;
        }
    }
}
