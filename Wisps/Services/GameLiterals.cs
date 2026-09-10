using Wisps.Domain;

namespace Wisps.Services;

internal static class GameLiterals
{
    // Ярус виспа определяется только токеном модели в Animated.ModelPath, но топливо и свет
    // носят общую модель GuidingLight — их различает исключительно путь сущности.
    internal const string ResourceBasePath = "Metadata/MiscellaneousObjects/Azmeri/AzmeriResourceBase";
    internal const string FuelResupplyPath = "Metadata/MiscellaneousObjects/Azmeri/AzmeriFuelResupply";
    internal const string LightBombPath = "Metadata/MiscellaneousObjects/Azmeri/AzmeriLightBomb";

    private const string WildModel = "wisp_vodoo";
    private const string VividModel = "wisp_warden";
    private const string PrimalModel = "wisp_primal";
    private const string SacredModel = "wisp_sacred";
    private const string LargeModel = "_big";

    internal static bool IsWispPath(string path) =>
        path.StartsWith(ResourceBasePath, StringComparison.Ordinal) ||
        path.StartsWith(FuelResupplyPath, StringComparison.Ordinal) ||
        path.StartsWith(LightBombPath, StringComparison.Ordinal);

    internal static WispKind ResolveKind(string entityPath, string modelPath)
    {
        if (entityPath.StartsWith(FuelResupplyPath, StringComparison.Ordinal)) return WispKind.Fuel;
        if (entityPath.StartsWith(LightBombPath, StringComparison.Ordinal)) return WispKind.Light;

        if (modelPath.Contains(PrimalModel, StringComparison.Ordinal)) return WispKind.Primal;
        if (modelPath.Contains(SacredModel, StringComparison.Ordinal)) return WispKind.Sacred;
        if (modelPath.Contains(VividModel, StringComparison.Ordinal)) return WispKind.Vivid;
        if (modelPath.Contains(WildModel, StringComparison.Ordinal)) return WispKind.Wild;

        return WispKind.Unknown;
    }

    internal static bool IsLargeModel(string modelPath) => modelPath.Contains(LargeModel, StringComparison.Ordinal);
}
