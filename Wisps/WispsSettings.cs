using System.Numerics;
using GameHelper.Plugin;
using Wisps.Domain;

namespace Wisps;

public enum PluginLanguage
{
    Host,
    English,
    Russian,
}

public enum HudCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public sealed class WispKindOption
{
    public bool Show = true;
    public Vector4 Color;
}

public sealed class WispsSettings : IPSettings
{
    public const float MinMarkerSize = 2f;
    public const float MaxMarkerSize = 10f;

    public PluginLanguage Language = PluginLanguage.Host;

    public WispKindOption[] Kinds = CreateKinds();
    public float MarkerSize = 4f;

    public bool ShowRoute = true;
    public bool ShowEvenRoute;
    public bool DeepAreaScan = true;

    public bool ShowHud = true;
    public HudCorner Hud = HudCorner.TopLeft;

    // Восстановление схемы при обновлении структуры конфига.
    public void Normalize()
    {
        if (Kinds.Length != WispKinds.Count)
        {
            var restored = CreateKinds();
            for (var i = 0; i < Kinds.Length && i < restored.Length; i++)
            {
                restored[i] = Kinds[i];
            }

            Kinds = restored;
        }

        for (var i = 0; i < Kinds.Length; i++)
        {
            var option = Kinds[i];
            if (option is null)
            {
                option = new WispKindOption();
                Kinds[i] = option;
            }

            if (option.Color == Vector4.Zero) option.Color = WispKinds.All[i].DefaultColor;
        }

        MarkerSize = Math.Clamp(MarkerSize, MinMarkerSize, MaxMarkerSize);
    }

    public void RestoreDefaultColors()
    {
        for (var i = 0; i < Kinds.Length; i++)
        {
            Kinds[i].Color = WispKinds.All[i].DefaultColor;
        }
    }

    internal KindMask VisibleKinds()
    {
        var bits = 0;
        for (var i = 0; i < Kinds.Length; i++)
        {
            if (Kinds[i].Show) bits |= 1 << i;
        }

        return new KindMask(bits);
    }

    private static WispKindOption[] CreateKinds()
    {
        var kinds = new WispKindOption[WispKinds.Count];
        for (var i = 0; i < kinds.Length; i++)
        {
            kinds[i] = new WispKindOption { Color = WispKinds.All[i].DefaultColor };
        }

        return kinds;
    }
}
