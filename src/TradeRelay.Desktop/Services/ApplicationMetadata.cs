using System.Reflection;

namespace TradeRelay.Desktop.Services;

internal sealed class ApplicationMetadata
{
    public const string ProductName = "TradeRelay";
    public const string LicenseName = "MIT";
    public const string RepositoryUrl = "https://github.com/rambod/TradeRelay";
    public const string SupportedPlatforms = "macOS 14+, Windows 11 24H2+, Ubuntu 24.04 (X11/XWayland)";

    public ApplicationMetadata()
        : this(typeof(ApplicationMetadata).Assembly)
    {
    }

    internal ApplicationMetadata(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        Version = informationalVersion.Split('+', 2)[0];
    }

    public string Version { get; }
    public string Product => ProductName;
    public string License => LicenseName;
    public string Repository => RepositoryUrl;
    public string Platforms => SupportedPlatforms;
}
