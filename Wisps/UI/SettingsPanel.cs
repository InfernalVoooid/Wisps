using System.Numerics;
using GameHelper.Utils;
using ImGuiNET;
using Wisps.Domain;
using Wisps.Services;

namespace Wisps.UI;

internal static class SettingsPanel
{
    private static readonly string[] ShowIds = ["##WispShow0", "##WispShow1", "##WispShow2", "##WispShow3", "##WispShow4"];
    private static readonly string[] ColorIds = ["##WispColor0", "##WispColor1", "##WispColor2", "##WispColor3", "##WispColor4"];
    private static readonly string[] Names = new string[WispKinds.Count];
    private static readonly string[] LanguageItems = new string[3];
    private static readonly string[] CornerItems = new string[4];

    private const ImGuiColorEditFlags SwatchFlags =
        ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar;

    internal static void Draw(WispsSettings settings, WispScanner scanner, TextCatalog text)
    {
        ImGui.TextWrapped(text.T("settings.intro", "Marks Azmeri wisps on the large map (Tab) in their own colours."));

        DrawLanguage(settings, text);

        ImGui.SeparatorText(text.T("settings.kinds", "Wisp tiers"));
        DrawKinds(settings, text);

        if (ImGui.SmallButton(text.Label("settings.restore_colors", "Restore colours", "WispRestoreColors")))
        {
            settings.RestoreDefaultColors();
        }

        ImGui.SeparatorText(text.T("settings.map", "Map"));
        ImGui.SliderFloat(
            text.Label("settings.marker_size", "Marker size", "WispMarkerSize"),
            ref settings.MarkerSize,
            WispsSettings.MinMarkerSize,
            WispsSettings.MaxMarkerSize,
            "%.1f px");

        ImGui.Checkbox(text.Label("settings.route", "Draw a collection route", "WispRoute"), ref settings.ShowRoute);
        ImGuiHelper.ToolTip(text.T(
            "settings.route.tooltip",
            "Draws the run that gathers the most wisps: it heads for the densest trails and favours large wisps " +
            "and rarer tiers, discounting the walk to get there. The number at the end is what the run collects."));

        ImGui.Checkbox(text.Label("settings.route_even", "Even-harvest route", "WispRouteEven"), ref settings.ShowEvenRoute);
        ImGuiHelper.ToolTip(text.T(
            "settings.route_even.tooltip",
            "A second, turquoise line for evening out the tiers: it ignores which tier is worth more and heads for the one " +
            "you have taken least of and the one this area holds least of. Show it next to the main run to compare, " +
            "or on its own."));

        ImGui.Checkbox(text.Label("settings.deep_scan", "Deep area scan", "WispDeepScan"), ref settings.DeepAreaScan);
        ImGuiHelper.ToolTip(text.T(
            "settings.deep_scan.tooltip",
            "Reveals wisps far outside the area you have walked through, so the map shows the whole layout instead of your trail. " +
            "Costs a little CPU every few seconds: turn it off if the overlay stutters."));
        ImGui.SameLine();
        ImGui.TextDisabled(text.F("settings.deep_scan_yield", "(+{0} beyond the bubble)", scanner.DeepFound));

        ImGui.SeparatorText(text.T("settings.hud", "On-screen counter"));
        ImGui.Checkbox(text.Label("settings.show_hud", "Show the counter", "WispShowHud"), ref settings.ShowHud);
        ImGuiHelper.ToolTip(text.T(
            "settings.show_hud.tooltip",
            "A click-through panel over the game with how many wisps of each tier the area still holds and how many you have taken."));

        ImGui.BeginDisabled(!settings.ShowHud);
        DrawCorner(settings, text);
        ImGui.EndDisabled();
    }

    private static void DrawLanguage(WispsSettings settings, TextCatalog text)
    {
        LanguageItems[0] = text.T("settings.language.host", "Follow GameHelper");
        LanguageItems[1] = "English";
        LanguageItems[2] = "Русский";

        var selected = (int)settings.Language;
        if (ImGui.Combo(text.Label("settings.language", "Plugin language", "WispLanguage"), ref selected, LanguageItems, LanguageItems.Length))
        {
            settings.Language = (PluginLanguage)selected;
            text.Use(settings.Language);
        }

        ImGuiHelper.ToolTip(text.T(
            "settings.language.tooltip",
            "Independent of the GameHelper language, so an English overlay can carry a Russian plugin."));
    }

    private static void DrawCorner(WispsSettings settings, TextCatalog text)
    {
        CornerItems[0] = text.T("settings.corner.top_left", "Top left");
        CornerItems[1] = text.T("settings.corner.top_right", "Top right");
        CornerItems[2] = text.T("settings.corner.bottom_left", "Bottom left");
        CornerItems[3] = text.T("settings.corner.bottom_right", "Bottom right");

        var selected = (int)settings.Hud;
        if (ImGui.Combo(text.Label("settings.corner", "Counter corner", "WispHudCorner"), ref selected, CornerItems, CornerItems.Length))
        {
            settings.Hud = (HudCorner)selected;
        }
    }

    private static void DrawKinds(WispsSettings settings, TextCatalog text)
    {
        var column = 0f;
        for (var i = 0; i < WispKinds.Count; i++)
        {
            var info = WispKinds.All[i];
            Names[i] = text.T(info.NameKey, info.NameFallback);
            column = MathF.Max(column, ImGui.CalcTextSize(Names[i]).X);
        }

        var style = ImGui.GetStyle();
        column += ImGui.GetFrameHeight() + (style.ItemInnerSpacing.X * 2f) + style.ItemSpacing.X;

        for (var i = 0; i < WispKinds.Count; i++)
        {
            ImGui.Checkbox(Names[i] + ShowIds[i], ref settings.Kinds[i].Show);
            ImGui.SameLine(column);
            ImGui.ColorEdit4(ColorIds[i], ref settings.Kinds[i].Color, SwatchFlags);
        }
    }
}
