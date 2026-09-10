using System.Numerics;

namespace Wisps.Domain;

internal enum WispKind
{
    Wild,
    Vivid,
    Primal,
    Sacred,
    Unknown,
}

internal readonly record struct WispKindInfo(
    WispKind Kind,
    float Weight,
    string NameKey,
    string NameFallback,
    Vector4 DefaultColor);

internal static class WispKinds
{
    internal const int Count = 5;

    internal static readonly WispKindInfo[] All =
    [
        new(WispKind.Wild, 1f, "kind.wild", "Wild", new(0.72f, 0.42f, 1.00f, 0.95f)),
        new(WispKind.Vivid, 2f, "kind.vivid", "Vivid", new(1.00f, 0.86f, 0.25f, 0.95f)),
        new(WispKind.Primal, 3f, "kind.primal", "Primal", new(0.28f, 0.80f, 1.00f, 0.95f)),
        new(WispKind.Sacred, 4f, "kind.sacred", "Sacred", new(1.00f, 0.55f, 0.18f, 0.95f)),
        new(WispKind.Unknown, 1f, "kind.unknown", "Other", new(0.75f, 0.78f, 0.82f, 0.95f)),
    ];

    internal const float LargeModelFactor = 1.6f;

    internal static WispKindInfo Of(WispKind kind) => All[(int)kind];

    internal static float TierWeight(WispKind kind) => All[(int)kind].Weight;

    internal static float SizeFactor(bool isLarge) => isLarge ? LargeModelFactor : 1f;
}

internal readonly record struct KindMask(int Bits)
{
    internal bool Has(WispKind kind) => (this.Bits & (1 << (int)kind)) != 0;
}
