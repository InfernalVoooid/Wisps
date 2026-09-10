using Wisps.Domain;

namespace Wisps.Services;

internal static class GameLiterals
{
    // Тир виспа определяется только токеном модели в Animated.ModelPath.
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
