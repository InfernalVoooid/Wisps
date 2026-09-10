using System.Collections.Concurrent;
using System.Numerics;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameOffsets.Objects.States.InGameState;
using Wisps.Domain;

namespace Wisps.Services;

internal readonly record struct WispSighting(WispKind Kind, Vector2 Grid, float Height, bool IsLarge);

internal sealed class WispScanner(WispField wisps)
{
    private const int AwakeIntervalMs = 150;
    private const int DeepIntervalMs = 6000;
    private const float DeepMoveGrid = 120f;
    private const int MaxDeepRunsPerArea = 16;
    private const float BubbleGrid = AreaInstanceConstants.NETWORK_BUBBLE_RADIUS;

    private static readonly Func<string, bool> WispPathFilter = GameLiterals.IsWispPath;

    private ConcurrentDictionary<uint, WispSighting>? deepSink;
    private Task? deepScan;

    private string areaHash = string.Empty;
    private nint areaAddress;
    private long nextAwakeAtMs;
    private long nextDeepAtMs;
    private Vector2 lastDeepFrom = new(float.MinValue);
    private int deepRuns;

    internal int DeepFound { get; private set; }

    internal void Tick(AreaInstance area, Vector2 playerGrid, long nowMs, bool deepEnabled)
    {
        if (NoteArea(area))
        {
            nextAwakeAtMs = 0;
            nextDeepAtMs = 0;
        }

        if (nowMs < nextAwakeAtMs) return;

        nextAwakeAtMs = nowMs + AwakeIntervalMs;

        wisps.BeginScan();
        ScanAwake(area, nowMs);
        DrainDeepScan(playerGrid, nowMs);
        wisps.ForgetCollected(playerGrid, BubbleGrid);

        if (deepEnabled) StartDeepScan(area, playerGrid, nowMs);
    }

    internal void Reset()
    {
        areaAddress = nint.Zero;
        areaHash = string.Empty;
        nextAwakeAtMs = 0;
        nextDeepAtMs = 0;
        lastDeepFrom = new Vector2(float.MinValue);
        deepRuns = 0;
        deepSink = null;
        DeepFound = 0;
    }

    private bool NoteArea(AreaInstance area)
    {
        if (area.Address == areaAddress && string.Equals(area.AreaHash, areaHash, StringComparison.Ordinal))
        {
            return false;
        }

        areaAddress = area.Address;
        areaHash = area.AreaHash ?? string.Empty;
        wisps.Clear();

        deepSink = null;
        lastDeepFrom = new Vector2(float.MinValue);
        deepRuns = 0;
        DeepFound = 0;
        return true;
    }

    private void ScanAwake(AreaInstance area, long nowMs)
    {
        foreach (var pair in area.AwakeEntities)
        {
            var entity = pair.Value;
            if (!entity.IsValid || !GameLiterals.IsWispPath(entity.Path)) continue;

            // Компоненты Useless-сущности заморожены; повторно не читаем.
            if (wisps.TryTouch(pair.Key.id) || !TryRead(entity, out var sighting)) continue;

            wisps.Observe(pair.Key.id, sighting.Kind, sighting.Grid, sighting.Height, sighting.IsLarge, nowMs);
        }
    }

    private void StartDeepScan(AreaInstance area, Vector2 playerGrid, long nowMs)
    {
        if (nowMs < nextDeepAtMs || deepScan is not null || deepRuns >= MaxDeepRunsPerArea) return;
        if (Vector2.DistanceSquared(playerGrid, lastDeepFrom) < DeepMoveGrid * DeepMoveGrid) return;

        nextDeepAtMs = nowMs + DeepIntervalMs;
        lastDeepFrom = playerGrid;
        deepRuns++;

        var sink = new ConcurrentDictionary<uint, WispSighting>();
        deepSink = sink;

        // LongRunning поток: ReadStdMap fanning out over pool не должен блокировать поток отрисовки хоста.
        deepScan = Task.Factory.StartNew(
            () => area.ScanSleepingEntities(
                WispPathFilter,
                (key, entity) =>
                {
                    if (TryRead(entity, out var sighting)) sink[key.id] = sighting;
                }),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void DrainDeepScan(Vector2 playerGrid, long nowMs)
    {
        if (deepScan is not { IsCompleted: true } finished) return;

        var sink = deepSink;
        deepScan = null;
        deepSink = null;

        if (finished.IsFaulted)
        {
            Console.WriteLine($"[Wisps] глубокий проход по зоне прерван: {finished.Exception?.GetBaseException().Message}");
            return;
        }

        if (sink is null) return;

        var found = 0;
        foreach (var pair in sink)
        {
            if (Vector2.DistanceSquared(playerGrid, pair.Value.Grid) <= BubbleGrid * BubbleGrid) continue;

            var sighting = pair.Value;
            wisps.Observe(pair.Key, sighting.Kind, sighting.Grid, sighting.Height, sighting.IsLarge, nowMs);
            found++;
        }

        DeepFound = found;
    }

    private static bool TryRead(Entity entity, out WispSighting sighting)
    {
        sighting = default;
        if (!entity.TryGetComponent<Render>(out var render)) return false;

        var grid = new Vector2(render.GridPosition.X, render.GridPosition.Y);

        // Игнорируем нулевые координаты незагруженной сущности.
        if (!float.IsFinite(grid.X) || !float.IsFinite(grid.Y) || grid == Vector2.Zero) return false;

        var model = entity.TryGetComponent<Animated>(out var animated) ? animated.ModelPath : string.Empty;
        sighting = new WispSighting(
            GameLiterals.ResolveKind(entity.Path, model),
            grid,
            render.TerrainHeight,
            GameLiterals.IsLargeModel(model));

        return true;
    }
}
