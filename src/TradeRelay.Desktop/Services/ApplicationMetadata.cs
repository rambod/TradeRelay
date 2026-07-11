using System.Reflection;

namespace TradeRelay.Desktop.Services;

internal sealed class ApplicationMetadata
{
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
}
