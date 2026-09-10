using GameHelper.Localization;
using Newtonsoft.Json;

namespace Wisps.Services;

/// <summary>
/// Тексты плагина с собственным выбором языка.
/// </summary>
/// <remarks>
/// Ядро отдаёт плагинам только свой общий язык (<see cref="PluginLocalization"/>), а оператору
/// нужен английский хост с русским плагином — поэтому каталог читает те же файлы
/// <c>Localization/*.json</c>, но язык выбирает сам.
/// </remarks>
internal sealed class TextCatalog(string localizationDirectory)
{
    private const string FallbackCode = "en-US";
    private static readonly string[] Codes = [string.Empty, "en-US", "ru-RU"];

    private readonly Dictionary<string, Dictionary<string, string>> loaded = [];

    private Dictionary<string, string> current = [];
    private string currentCode = string.Empty;

    // Растёт при смене языка: по нему кэши строк оверлея понимают, что их пора пересобрать.
    internal int Revision { get; private set; }

    internal void Use(PluginLanguage language)
    {
        var code = language == PluginLanguage.Host
            ? OverlayLocalization.LanguageCode(OverlayLocalization.CurrentLanguage)
            : Codes[(int)language];

        if (string.Equals(code, currentCode, StringComparison.Ordinal)) return;

        currentCode = code;
        current = Resolve(code);
        Revision++;
    }

    internal string T(string key, string fallback) =>
        current.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    internal string F(string key, string fallback, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, T(key, fallback), args);

    // Видимый текст плюс стабильный скрытый идентификатор ImGui: смена языка не должна сбрасывать
    // состояние элемента.
    internal string Label(string key, string fallback, string id) => $"{T(key, fallback)}##{id}";

    private Dictionary<string, string> Resolve(string code)
    {
        if (TryLoad(code, out var resources)) return resources;

        return TryLoad(FallbackCode, out resources) ? resources : [];
    }

    private bool TryLoad(string code, out Dictionary<string, string> resources)
    {
        resources = [];
        if (code.Length == 0) return false;

        if (loaded.TryGetValue(code, out var cached))
        {
            resources = cached;
            return true;
        }

        var path = Path.Combine(localizationDirectory, $"{code}.json");
        if (!File.Exists(path)) return false;

        try
        {
            resources = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wisps] перевод {code} не прочитан: {ex.Message}");
            resources = [];
        }

        loaded[code] = resources;
        return true;
    }
}
