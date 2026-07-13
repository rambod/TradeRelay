using System.Text.Json;
using System.Text.Json.Serialization;
using TradeRelay.Core.Settings;
using TradeRelay.Core.Models;

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
            string json = File.ReadAllText(paths.SettingsFile);
            using JsonDocument document = JsonDocument.Parse(json);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            if (document.RootElement.TryGetProperty("bybit", out JsonElement legacy) || document.RootElement.TryGetProperty("Bybit", out legacy))
            {
                bool hasEnvironment = legacy.TryGetProperty("environment", out JsonElement environmentElement) || legacy.TryGetProperty("Environment", out environmentElement);
                TradingEnvironment environment = hasEnvironment
                    && Enum.TryParse(environmentElement.GetString(), true, out TradingEnvironment parsed)
                    ? parsed
                    : TradingEnvironment.Demo;
                bool hasRemember = legacy.TryGetProperty("rememberCredentials", out JsonElement rememberElement) || legacy.TryGetProperty("RememberCredentials", out rememberElement);
                bool remember = hasRemember && rememberElement.GetBoolean();
                ExchangeProviderSettings bybit = settings.GetExchange(new ExchangeId("bybit"));
                bybit.Environment = environment;
                bybit.SetRemember(environment, remember);
                settings.SelectedExchange = "bybit";
                settings.SchemaVersion = 2;
                BackupLegacySettingsOnce();
                SaveAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
            }

            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    private void BackupLegacySettingsOnce()
    {
        string backup = paths.SettingsFile + ".v1.backup";
        if (!File.Exists(backup)) File.Copy(paths.SettingsFile, backup);
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
