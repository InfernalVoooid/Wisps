using Newtonsoft.Json;

namespace Wisps.Services;

internal static class SettingsFile
{
    internal static WispsSettings Load(string path)
    {
        if (!File.Exists(path)) return new WispsSettings();

        WispsSettings settings;
        try
        {
            settings = JsonConvert.DeserializeObject<WispsSettings>(File.ReadAllText(path)) ?? new WispsSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wisps] настройки не прочитаны ({ex.Message}) — взяты значения по умолчанию");
            return new WispsSettings();
        }

        settings.Normalize();
        return settings;
    }

    internal static void Save(string path, WispsSettings settings)
    {
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wisps] настройки не сохранены: {ex.Message}");
        }
    }
}
