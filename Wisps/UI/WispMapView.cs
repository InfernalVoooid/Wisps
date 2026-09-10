using System.Globalization;
using System.Numerics;
using GameHelper.Utils;
using ImGuiNET;
using Wisps.Domain;

namespace Wisps.UI;

internal static class WispMapView
{
    private const int MaxDrawnMarks = 800;
    private const float MinDrawnRadius = 0.5f;
    private const float LargeMarkFactor = 1.35f;
    private const float MarkEdgeThickness = 1.2f;

    private const float PopInMs = 260f;
    private const float PopInOvershoot = 0.9f;

    private const float RouteThickness = 2.2f;
    private const float RouteCasingThickness = 5f;
    private const float DashOnPx = 9f;
    private const float DashGapPx = 7f;
    private const float DashSpeedPxPerSec = 30f;
    private const int MaxDashes = 160;
    private const float ArrowPx = 10f;
    private const float RingPulseHz = 0.55f;

    private static readonly Vector4 HaulColor = new(1f, 0.97f, 0.88f, 1f);
    private static readonly Vector4 EvenColor = new(0.32f, 1f, 0.86f, 1f);
    private static readonly Vector4 RouteCasingColor = new(0f, 0f, 0f, 0.75f);
    private static readonly Vector4 LabelBackground = new(0f, 0f, 0f, 0.55f);

    private static readonly Vector2[] Path = new Vector2[WispRoute.MaxApproachPoints + WispRoute.MaxStops];
    private static readonly uint[] Fill = new uint[WispKinds.Count];
    private static readonly uint[] Edge = new uint[WispKinds.Count];

    private static readonly int[] LabelCounts = [-1, -1];
    private static readonly string[] Labels = [string.Empty, string.Empty];

    internal static void Draw(
        WispsSettings settings,
        WispField wisps,
        WispRoute? haul,
        WispRoute? even,
        in MapProjector.MapView view,
        Vector2 playerGrid,
        float playerHeight,
        long nowMs)
    {
        var marks = wisps.Marks;
        if (marks.IsEmpty) return;

        RefreshPalette(settings);

        var draw = ImGui.GetBackgroundDrawList();
        draw.PushClipRect(view.Origin, view.Origin + view.Size, true);

        DrawMarks(draw, settings, marks, view, playerGrid, playerHeight, nowMs);

        if (even is { HasRoute: true }) DrawRoute(draw, even, view, playerGrid, playerHeight, EvenColor, 1);
        if (haul is { HasRoute: true }) DrawRoute(draw, haul, view, playerGrid, playerHeight, HaulColor, 0);

        draw.PopClipRect();
    }

    private static void RefreshPalette(WispsSettings settings)
    {
        for (var i = 0; i < WispKinds.Count; i++)
        {
            var color = settings.Kinds[i].Color;
            Fill[i] = ImGuiHelper.Color(color);
            Edge[i] = ImGuiHelper.Color(new Vector4(color.X * 0.2f, color.Y * 0.2f, color.Z * 0.2f, MathF.Min(1f, color.W + 0.25f)));
        }
    }

    private static void DrawMarks(
        ImDrawListPtr draw,
        WispsSettings settings,
        ReadOnlySpan<WispMark> marks,
        in MapProjector.MapView view,
        Vector2 playerGrid,
        float playerHeight,
        long nowMs)
    {
        var padding = settings.MarkerSize * 3f;
        var clipMin = view.Origin - new Vector2(padding);
        var clipMax = view.Origin + view.Size + new Vector2(padding);
        var drawn = 0;

        foreach (var mark in marks)
        {
            if (!settings.Kinds[(int)mark.Kind].Show) continue;

            var point = view.Project(mark.Grid - playerGrid, mark.Height - playerHeight);
            if (point.X < clipMin.X || point.X > clipMax.X || point.Y < clipMin.Y || point.Y > clipMax.Y) continue;

            var radius = settings.MarkerSize * (mark.IsLarge ? LargeMarkFactor : 1f) * PopIn(mark.FirstSeenMs, nowMs);
            if (radius < MinDrawnRadius) continue;

            var kind = (int)mark.Kind;
            draw.AddCircleFilled(point, radius, Fill[kind]);
            draw.AddCircle(point, radius, Edge[kind], 0, MarkEdgeThickness);

            if (++drawn == MaxDrawnMarks) return;
        }
    }

    private static void DrawRoute(
        ImDrawListPtr draw,
        WispRoute route,
        in MapProjector.MapView view,
        Vector2 playerGrid,
        float playerHeight,
        Vector4 routeColor,
        int labelSlot)
    {
        var count = BuildScreenPath(route, view, playerGrid, playerHeight);
        if (count < 2) return;

        var path = Path.AsSpan(0, count);
        var color = ImGuiHelper.Color(routeColor);
        var casing = ImGuiHelper.Color(RouteCasingColor);
        var time = ImGui.GetTime();

        DrawFlowPath(draw, path, color, casing, time);
        DrawArrow(draw, path, color, casing);

        var pulse = 0.5f + (0.5f * MathF.Sin((float)time * MathF.Tau * RingPulseHz));
        var startIndex = route.Approach.IsEmpty ? 0 : route.Approach.Length - 1;
        var radius = 7f + (pulse * 3f);
        draw.AddCircle(path[startIndex], radius, casing, 0, 3.4f + pulse);
        draw.AddCircle(path[startIndex], radius, color, 0, 1.6f + pulse);

        DrawCount(draw, path[^1], color, route.Count, labelSlot);
    }

    private static int BuildScreenPath(WispRoute route, in MapProjector.MapView view, Vector2 playerGrid, float playerHeight)
    {
        var approach = route.Approach;
        var stops = route.Stops;
        var firstHeight = stops.IsEmpty ? playerHeight : stops[0].Height;
        var count = 0;

        for (var i = 0; i < approach.Length; i++)
        {
            var progress = approach.Length > 1 ? (float)i / (approach.Length - 1) : 0f;
            var height = playerHeight + ((firstHeight - playerHeight) * progress);
            Path[count++] = view.Project(approach[i] - playerGrid, height - playerHeight);
        }

        for (var i = approach.IsEmpty ? 0 : 1; i < stops.Length; i++)
        {
            Path[count++] = view.Project(stops[i].Grid - playerGrid, stops[i].Height - playerHeight);
        }

        return count;
    }

    private static void DrawFlowPath(ImDrawListPtr draw, ReadOnlySpan<Vector2> points, uint color, uint casing, double time)
    {
        var total = 0f;
        for (var i = 1; i < points.Length; i++)
        {
            total += Vector2.Distance(points[i - 1], points[i]);
        }

        if (total < DashOnPx) return;

        var stretch = MathF.Max(1f, total / (MaxDashes * (DashOnPx + DashGapPx)));
        var dash = DashOnPx * stretch;
        var period = (DashOnPx + DashGapPx) * stretch;
        var offset = -(float)((time * DashSpeedPxPerSec) % period);

        for (var i = 1; i < points.Length; i++)
        {
            var from = points[i - 1];
            var span = points[i] - from;
            var length = span.Length();
            if (length < 0.01f) continue;

            var direction = span / length;
            for (var start = offset; start < length; start += period)
            {
                var head = MathF.Max(0f, start);
                var tail = MathF.Min(length, start + dash);
                if (tail <= head) continue;

                var dashFrom = from + (direction * head);
                var dashTo = from + (direction * tail);
                draw.AddLine(dashFrom, dashTo, casing, RouteCasingThickness);
                draw.AddLine(dashFrom, dashTo, color, RouteThickness);
            }

            offset -= length;
            offset -= period * MathF.Floor(offset / period);
            offset -= period;
        }
    }

    private static void DrawArrow(ImDrawListPtr draw, ReadOnlySpan<Vector2> points, uint color, uint casing)
    {
        var tip = points[^1];
        var span = tip - points[^2];
        var length = span.Length();
        if (length < 1f) return;

        var direction = span / length;
        var wing = new Vector2(-direction.Y, direction.X) * (ArrowPx * 0.5f);
        var baseCenter = tip - (direction * ArrowPx);
        draw.AddTriangle(tip, baseCenter + wing, baseCenter - wing, casing, 3f);
        draw.AddTriangleFilled(tip, baseCenter + wing, baseCenter - wing, color);
    }

    private static void DrawCount(ImDrawListPtr draw, Vector2 end, uint color, int count, int slot)
    {
        if (LabelCounts[slot] != count)
        {
            LabelCounts[slot] = count;
            Labels[slot] = count.ToString(CultureInfo.InvariantCulture);
        }

        var label = Labels[slot];
        var size = ImGui.CalcTextSize(label);
        var origin = end - new Vector2(size.X * 0.5f, size.Y + 12f);
        var padding = new Vector2(4f, 2f);
        draw.AddRectFilled(origin - padding, origin + size + padding, ImGuiHelper.Color(LabelBackground), 3f);
        draw.AddText(origin, color, label);
    }

    private static float PopIn(long firstSeenMs, long nowMs)
    {
        var age = nowMs - firstSeenMs;
        if (age >= PopInMs) return 1f;

        var remaining = 1f - (Math.Max(0L, age) / PopInMs);
        return 1f + (PopInOvershoot * remaining * remaining * remaining);
    }
}
