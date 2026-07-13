namespace TradeRelay.Desktop.Services;

internal sealed class ApplicationDataPaths
{
    public ApplicationDataPaths()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("TRADERELAY_APP_DATA");
        Root = !string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.GetFullPath(overrideRoot)
            : OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "TradeRelay")
            : OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradeRelay")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "TradeRelay");
    }

    internal ApplicationDataPaths(string root) => Root = root;

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string ProtectedDataDirectory => Path.Combine(Root, "protected");
    public string AuditDirectory => Path.Combine(Root, "audit");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string DiagnosticsDirectory => Path.Combine(Root, "diagnostics");
}
