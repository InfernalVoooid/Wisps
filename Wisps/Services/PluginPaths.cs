using System.Reflection;

namespace Wisps.Services;

// Резолв абсолютного пути к сборке, если хост передал относительный DllDirectory.
internal static class PluginPaths
{
    internal static string Root(string dllDirectoryFromHost)
    {
        if (Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) is { Length: > 0 } directory)
        {
            return directory;
        }

        return string.IsNullOrWhiteSpace(dllDirectoryFromHost)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(dllDirectoryFromHost);
    }

    internal static string ConfigFile(string root) => Path.Join(root, "config", "settings.txt");

    internal static string LocalizationDirectory(string root) => Path.Join(root, "Localization");
}
