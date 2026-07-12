namespace TradeRelay.Desktop.Services;

internal sealed class ApplicationDataPaths
{
    public ApplicationDataPaths()
    {
        Root = OperatingSystem.IsMacOS()
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
}
