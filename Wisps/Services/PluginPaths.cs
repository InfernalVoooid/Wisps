using System.Reflection;

namespace Wisps.Services;

/// <summary>
/// Пути к ресурсам плагина от физического расположения сборки.
/// </summary>
/// <remarks>
/// Хост отдаёт <c>DllDirectory</c> относительным (<c>Plugins/Wisps</c>): запуск с другим рабочим
/// каталогом иначе тихо уводит и настройки, и переводы в сторону.
/// </remarks>
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
