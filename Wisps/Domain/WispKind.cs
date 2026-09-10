using System.Numerics;

namespace Wisps.Domain;

internal enum WispKind
{
    Wild,
    Vivid,
    Primal,
    Sacred,
    Unknown,
    Fuel,
    Light,
}

internal readonly record struct WispKindInfo(
    WispKind Kind,
    float Weight,
    bool Routable,
    float MarkScale,
    string NameKey,
    string NameFallback,
    Vector4 DefaultColor);

internal static class WispKinds
{
    internal const int Count = 7;

    internal static readonly WispKindInfo[] All =
    [
        new(WispKind.Wild, 1f, true, 1f, "kind.wild", "Wild", new(0.72f, 0.42f, 1.00f, 0.95f)),
        new(WispKind.Vivid, 2f, true, 1f, "kind.vivid", "Vivid", new(1.00f, 0.86f, 0.25f, 0.95f)),
        new(WispKind.Primal, 3f, true, 1f, "kind.primal", "Primal", new(0.28f, 0.80f, 1.00f, 0.95f)),
        new(WispKind.Sacred, 4f, true, 1f, "kind.sacred", "Sacred", new(1.00f, 0.55f, 0.18f, 0.95f)),
        new(WispKind.Unknown, 1f, true, 1f, "kind.unknown", "Other", new(0.75f, 0.78f, 0.82f, 0.95f)),
        new(WispKind.Fuel, 0f, false, 1.8f, "kind.fuel", "Fuel", new(1.00f, 0.30f, 0.42f, 0.95f)),
        new(WispKind.Light, 0f, false, 1.4f, "kind.light", "Light", new(0.55f, 1.00f, 0.60f, 0.95f)),
    ];

    // Топливо и свет — не добыча: маршрут строится только по ярусам.
    internal static readonly KindMask Routable = BuildRoutableMask();

    internal const float LargeModelFactor = 1.6f;

    internal static WispKindInfo Of(WispKind kind) => All[(int)kind];

    internal static float TierWeight(WispKind kind) => All[(int)kind].Weight;

    internal static bool IsRoutable(WispKind kind) => All[(int)kind].Routable;

    internal static float SizeFactor(bool isLarge) => isLarge ? LargeModelFactor : 1f;

    private static KindMask BuildRoutableMask()
    {
        var bits = 0;
        foreach (var info in All)
        {
            if (info.Routable) bits |= 1 << (int)info.Kind;
        }

        return new KindMask(bits);
    }
}

internal readonly record struct KindMask(int Bits)
{
    internal bool Has(WispKind kind) => (this.Bits & (1 << (int)kind)) != 0;

    internal KindMask And(KindMask other) => new(this.Bits & other.Bits);
}
