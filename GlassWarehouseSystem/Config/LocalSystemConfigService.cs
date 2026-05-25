using System;
using System.IO;
using Newtonsoft.Json;

namespace GlassWarehouseSystem.LocalConfig;

public static class LocalSystemConfigService
{
    private static readonly string ConfigDirectory = AppContext.BaseDirectory;

    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "system-settings.json");

    public static LocalSystemSettings Load()
    {
        var defaults = new LocalSystemSettings();

        try
        {
            if (!File.Exists(ConfigPath))
            {
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(ConfigPath);
            if (string.IsNullOrWhiteSpace(json))
                return defaults;

            var settings = JsonConvert.DeserializeObject<LocalSystemSettings>(json);
            if (settings == null)
                return defaults;

            return settings;
        }
        catch
        {
            return defaults;
        }
    }

    public static void Save(LocalSystemSettings settings)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(ConfigPath, json);
    }

    public static string GetConfigPath() => ConfigPath;
}
