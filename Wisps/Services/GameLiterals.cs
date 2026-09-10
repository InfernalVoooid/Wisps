using Wisps.Domain;

namespace Wisps.Services;

/// <summary>
/// Литералы, продиктованные игрой: путь сущности виспа и токены его модели.
/// </summary>
/// <remarks>
/// Патч ломает их вместе и правятся они только здесь. Наши пороги, доли и тайминги сюда не
/// попадают: они не зависят от версии игры.
/// </remarks>
internal static class GameLiterals
{
    // Все четыре яруса — одна сущность; различает их только загруженная модель:
    // Metadata/Effects/.../wisp_doodads/wisp_<ярус>_<размер>.ao
    internal const string WispEntityPath = "Metadata/MiscellaneousObjects/Azmeri/AzmeriResourceBase";

    private const string WildModel = "wisp_vodoo";
    private const string VividModel = "wisp_warden";
    private const string PrimalModel = "wisp_primal";
    private const string SacredModel = "wisp_sacred";
    private const string LargeModel = "_big";

    internal static WispKind ResolveKind(string modelPath)
    {
        if (modelPath.Contains(PrimalModel, StringComparison.Ordinal)) return WispKind.Primal;
        if (modelPath.Contains(SacredModel, StringComparison.Ordinal)) return WispKind.Sacred;
        if (modelPath.Contains(VividModel, StringComparison.Ordinal)) return WispKind.Vivid;
        if (modelPath.Contains(WildModel, StringComparison.Ordinal)) return WispKind.Wild;

        return WispKind.Unknown;
    }

    internal static bool IsLargeModel(string modelPath) => modelPath.Contains(LargeModel, StringComparison.Ordinal);
}
