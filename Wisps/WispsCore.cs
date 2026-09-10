using System.Numerics;
using GameHelper.Plugin;
using GameHelper.RemoteObjects.Components;
using Wisps.Domain;
using Wisps.Navigation;
using Wisps.Services;
using Wisps.UI;

namespace Wisps;

public sealed class WispsCore : PCore<WispsSettings>
{
    private readonly WispField wisps = new();
    private readonly WispRoute haulRoute = new();
    private readonly WispRoute evenRoute = new();
    private readonly TerrainGrid terrain = new();
    private readonly AreaField field = new();
    private readonly WispScanner scanner;

    private TextCatalog text = new(string.Empty);
    private string settingsPath = string.Empty;

    public WispsCore() => scanner = new WispScanner(wisps);

    public override string GetDescription() => text.T(
        "plugin.description",
        "Shows Wildwood wisps on the Overlay Map (Tab): every tier in its own colour, plus the walkable route that collects the most of them.");

    public override void OnEnable(bool isGameOpened)
    {
        var root = PluginPaths.Root(DllDirectory);
        settingsPath = PluginPaths.ConfigFile(root);
        text = new TextCatalog(PluginPaths.LocalizationDirectory(root));

        Settings = SettingsFile.Load(settingsPath);
        text.Use(Settings.Language);
    }

    public override void OnDisable()
    {
        wisps.Clear();
        haulRoute.Clear();
        evenRoute.Clear();
        field.Reset();
        terrain.Release();
        scanner.Reset();
    }

    public override void SaveSettings()
    {
        if (settingsPath.Length == 0) return;

        SettingsFile.Save(settingsPath, Settings);
    }

    public override void DrawSettings()
    {
        text.Use(Settings.Language);
        SettingsPanel.Draw(Settings, scanner, text);
    }

    private void Advance(WispRoute route, bool enabled, KindMask visible, bool balanced, long nowMs)
    {
        if (enabled)
        {
            route.Tick(wisps, terrain, field, visible, balanced, nowMs);
        }
        else if (route.HasRoute)
        {
            route.Clear();
        }
    }

    public override void DrawUI()
    {
        text.Use(Settings.Language);

        if (!GameMemory.TryGetArea(out var area) ||
            !area.Player.TryGetComponent<Render>(out var player))
        {
            return;
        }

        var playerGrid = new Vector2(player.GridPosition.X, player.GridPosition.Y);
        if (!float.IsFinite(playerGrid.X) || !float.IsFinite(playerGrid.Y)) return;

        var nowMs = Environment.TickCount64;

        scanner.Tick(area, playerGrid, nowMs, Settings.DeepAreaScan);

        if (Settings.ShowRoute || Settings.ShowEvenRoute)
        {
            terrain.Sync(area);
            field.Tick(terrain, playerGrid, nowMs);
        }
        else
        {
            field.Reset();
            terrain.Release();
        }

        var visible = Settings.VisibleKinds();
        Advance(haulRoute, Settings.ShowRoute, visible, balanced: false, nowMs);
        Advance(evenRoute, Settings.ShowEvenRoute, visible, balanced: true, nowMs);

        WispHud.Draw(Settings, wisps, haulRoute, evenRoute, text);

        var view = MapProjector.ForLargeMap();
        if (!view.IsValid) return;

        WispMapView.Draw(
            Settings,
            wisps,
            Settings.ShowRoute ? haulRoute : null,
            Settings.ShowEvenRoute ? evenRoute : null,
            text,
            view,
            playerGrid,
            player.TerrainHeight,
            nowMs);
    }
}
