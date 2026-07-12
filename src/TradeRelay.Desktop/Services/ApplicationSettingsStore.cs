using System.Text.Json;
using System.Text.Json.Serialization;
using TradeRelay.Core.Settings;

namespace TradeRelay.Desktop.Services;

internal sealed class ApplicationSettingsStore(ApplicationDataPaths paths)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(paths.SettingsFile)) return new AppSettings();
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsFile), Options);
            return settings ?? new AppSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Root);
        string temporary = paths.SettingsFile + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, paths.SettingsFile, true);
    }
}
