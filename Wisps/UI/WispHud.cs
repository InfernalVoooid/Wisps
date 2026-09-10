using System.Globalization;
using System.Numerics;
using GameHelper.Utils;
using ImGuiNET;
using Wisps.Domain;
using Wisps.Services;

namespace Wisps.UI;

internal static class WispHud
{
    private const float MarginPx = 18f;
    private const float PaddingPx = 8f;
    private const float ColumnGapPx = 14f;
    private const float DotRadiusPx = 4f;

    private static readonly Vector4 Background = new(0f, 0f, 0f, 0.55f);
    private static readonly Vector4 TitleColor = new(1f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 TextColor = new(0.92f, 0.94f, 0.98f, 0.95f);
    private static readonly Vector4 MutedColor = new(0.66f, 0.70f, 0.78f, 0.90f);
    private static readonly Vector4 EvenColor = new(0.32f, 1f, 0.86f, 0.95f);

    private static readonly int[] Rows = new int[WispKinds.Count];
    private static readonly string[] Names = new string[WispKinds.Count];
    private static readonly string[] Counts = new string[WispKinds.Count];
    private static readonly string[] Harvested = new string[WispKinds.Count];

    private static readonly int[] LastCounts = new int[WispKinds.Count];
    private static readonly int[] LastHarvested = new int[WispKinds.Count];

    private static string title = string.Empty;
    private static string routeLine = string.Empty;
    private static string evenLine = string.Empty;
    private static int rowCount;

    private static float nameColumn;
    private static float countColumn;
    private static float harvestColumn;
    private static float panelWidth;
    private static float lastFontSize;
    private static int lastRoute = -1;
    private static int lastEven = -1;
    private static int lastRevision = -1;
    private static int lastMask = -1;

    internal static void Draw(WispsSettings settings, WispField wisps, WispRoute haul, WispRoute even, TextCatalog text)
    {
        if (!settings.ShowHud) return;

        Rebuild(settings, wisps, haul, even, text);
        if (rowCount == 0) return;

        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var lines = rowCount + 1 + (routeLine.Length > 0 ? 1 : 0) + (evenLine.Length > 0 ? 1 : 0);
        var size = new Vector2(panelWidth, lineHeight * lines);
        var origin = Anchor(settings.Hud, size);

        var draw = ImGui.GetBackgroundDrawList();
        draw.AddRectFilled(origin - new Vector2(PaddingPx), origin + size + new Vector2(PaddingPx), ImGuiHelper.Color(Background), 5f);
        draw.AddText(origin, ImGuiHelper.Color(TitleColor), title);

        var countX = nameColumn + ColumnGapPx;
        var harvestX = countX + countColumn + ColumnGapPx;

        for (var i = 0; i < rowCount; i++)
        {
            var kind = Rows[i];
            var y = lineHeight * (i + 1);
            var color = ImGuiHelper.Color(settings.Kinds[kind].Color);

            draw.AddCircleFilled(origin + new Vector2(DotRadiusPx, y + (lineHeight * 0.35f)), DotRadiusPx, color);
            draw.AddText(origin + new Vector2(DotRadiusPx * 3f, y), ImGuiHelper.Color(TextColor), Names[kind]);
            draw.AddText(origin + new Vector2(countX, y), ImGuiHelper.Color(TextColor), Counts[kind]);
            draw.AddText(origin + new Vector2(harvestX, y), ImGuiHelper.Color(MutedColor), Harvested[kind]);
        }

        var footer = rowCount + 1;
        if (routeLine.Length > 0)
        {
            draw.AddText(origin + new Vector2(0f, lineHeight * footer++), ImGuiHelper.Color(MutedColor), routeLine);
        }

        if (evenLine.Length > 0)
        {
            draw.AddText(origin + new Vector2(0f, lineHeight * footer), ImGuiHelper.Color(EvenColor), evenLine);
        }
    }

    private static void Rebuild(WispsSettings settings, WispField wisps, WispRoute haul, WispRoute even, TextCatalog text)
    {
        var mask = settings.VisibleKinds().Bits;
        var routeCount = settings.ShowRoute && haul.HasRoute ? haul.Count : -1;
        var evenCount = settings.ShowEvenRoute && even.HasRoute ? even.Count : -1;
        var stale = lastRevision != text.Revision ||
                    lastMask != mask ||
                    lastRoute != routeCount ||
                    lastEven != evenCount;

        for (var i = 0; i < WispKinds.Count && !stale; i++)
        {
            stale = LastCounts[i] != wisps.CountOf((WispKind)i) || LastHarvested[i] != wisps.CollectedOf((WispKind)i);
        }

        if (!stale && MathF.Abs(lastFontSize - ImGui.GetFontSize()) < 0.01f) return;

        lastFontSize = ImGui.GetFontSize();
        lastRevision = text.Revision;
        lastMask = mask;
        lastRoute = routeCount;
        lastEven = evenCount;

        title = text.T("hud.title", "Wisps");
        rowCount = 0;

        for (var i = 0; i < WispKinds.Count; i++)
        {
            var info = WispKinds.All[i];
            var count = wisps.CountOf(info.Kind);
            var collected = wisps.CollectedOf(info.Kind);
            LastCounts[i] = count;
            LastHarvested[i] = collected;

            if (!settings.Kinds[i].Show || (count == 0 && collected == 0)) continue;

            Names[i] = text.T(info.NameKey, info.NameFallback);
            Counts[i] = count.ToString(CultureInfo.InvariantCulture);
            Harvested[i] = text.F("hud.collected", "+{0}", collected);
            Rows[rowCount++] = i;
        }

        routeLine = routeCount < 0 ? string.Empty : text.F("hud.route", "Route: {0}", routeCount);
        evenLine = evenCount < 0 ? string.Empty : text.F("hud.route_even", "Even: {0}", evenCount);

        Measure();
    }

    private static void Measure()
    {
        nameColumn = ImGui.CalcTextSize(title).X;
        countColumn = 0f;
        harvestColumn = 0f;

        for (var i = 0; i < rowCount; i++)
        {
            var kind = Rows[i];
            nameColumn = MathF.Max(nameColumn, (DotRadiusPx * 3f) + ImGui.CalcTextSize(Names[kind]).X);
            countColumn = MathF.Max(countColumn, ImGui.CalcTextSize(Counts[kind]).X);
            harvestColumn = MathF.Max(harvestColumn, ImGui.CalcTextSize(Harvested[kind]).X);
        }

        panelWidth = nameColumn + ColumnGapPx + countColumn + ColumnGapPx + harvestColumn;
        if (routeLine.Length > 0) panelWidth = MathF.Max(panelWidth, ImGui.CalcTextSize(routeLine).X);
        if (evenLine.Length > 0) panelWidth = MathF.Max(panelWidth, ImGui.CalcTextSize(evenLine).X);
    }

    private static Vector2 Anchor(HudCorner corner, Vector2 size)
    {
        var area = GameMemory.WindowArea;
        var right = area.Width - size.X - MarginPx;
        var bottom = area.Height - size.Y - MarginPx;

        return corner switch
        {
            HudCorner.TopRight => new Vector2(right, MarginPx),
            HudCorner.BottomLeft => new Vector2(MarginPx, bottom),
            HudCorner.BottomRight => new Vector2(right, bottom),
            _ => new Vector2(MarginPx, MarginPx),
        };
    }
}
