using System.Collections.Concurrent;
using System.Numerics;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameOffsets.Objects.States.InGameState;
using Wisps.Domain;

namespace Wisps.Services;

// Одно наблюдение виспа: что прочитано из его компонентов за проход.
internal readonly record struct WispSighting(WispKind Kind, Vector2 Grid, float Height, bool IsLarge);

/// <summary>
/// Сбор виспов из памяти игры: частый проход по активным сущностям и редкий фоновый просмотр
/// «спящей» карты, которая знает объекты далеко за сетевым пузырём.
/// </summary>
internal sealed class WispScanner(WispField wisps)
{
    private const int AwakeIntervalMs = 150;

    // Спящая карта зоны не меняется, пока игрок стоит на месте, и новые её куски подгружаются
    // только по мере ухода вперёд: обход привязан к пройденному пути, а не к таймеру.
    private const int DeepIntervalMs = 6000;
    private const float DeepMoveGrid = 120f;
    private const int MaxDeepRunsPerArea = 16;

    private const float BubbleGrid = AreaInstanceConstants.NETWORK_BUBBLE_RADIUS;

    private readonly Func<string, bool> wispPathFilter =
        static path => path.StartsWith(GameLiterals.WispEntityPath, StringComparison.Ordinal);

    private ConcurrentDictionary<uint, WispSighting>? deepSink;
    private Task? deepScan;

    private string areaHash = string.Empty;
    private nint areaAddress;
    private long nextAwakeAtMs;
    private long nextDeepAtMs;
    private Vector2 lastDeepFrom = new(float.MinValue);
    private int deepRuns;

    // Сколько виспов последний глубокий проход принёс из-за пузыря: цена этого прохода видна
    // оператору в настройках, и по ней же его отключают.
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

    // Смена зоны читается из самой зоны, а не из события: пропущенное событие оставило бы на карте
    // виспы предыдущей локации.
    private bool NoteArea(AreaInstance area)
    {
        if (area.Address == areaAddress && string.Equals(area.AreaHash, areaHash, StringComparison.Ordinal))
        {
            return false;
        }

        areaAddress = area.Address;
        areaHash = area.AreaHash ?? string.Empty;
        wisps.Clear();

        // Ссылка на идущий проход сохраняется: его результат уже не нужен, но запускать второй
        // поверх него нельзя. Пустой приёмник и есть признак «выбросить, когда закончится».
        deepSink = null;
        lastDeepFrom = new Vector2(float.MinValue);
        deepRuns = 0;
        DeepFound = 0;
        return true;
    }

    private void ScanAwake(AreaInstance area, long nowMs)
    {
        // Обход самого словаря, а не AwakeEntities.Values: у ConcurrentDictionary свойство Values
        // копирует весь список на каждое обращение.
        foreach (var pair in area.AwakeEntities)
        {
            var entity = pair.Value;
            if (!entity.IsValid || !entity.Path.StartsWith(GameLiterals.WispEntityPath, StringComparison.Ordinal))
            {
                continue;
            }

            // Известный висп только отмечается живым: его компоненты заморожены, и перечитывать
            // их каждый проход — самая дорогая строка этого цикла и самая бесполезная.
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

        // Проход по спящей карте читает память из фонового потока и его обратный вызов
        // параллелен: собственный приёмник на проход снимает вопрос гонок с реестром.
        var sink = new ConcurrentDictionary<uint, WispSighting>();
        deepSink = sink;

        // Выделенный поток, а не пул: ReadStdMap внутри сам раскладывается через Parallel.ForEach,
        // а тот же пул каждый кадр нужен ядру для собственного чтения карты сущностей — из потока
        // отрисовки. Занятый нами воркер там оборачивается замиранием кадра.
        deepScan = Task.Factory.StartNew(
            () => area.ScanSleepingEntities(
                wispPathFilter,
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

        // Зона сменилась, пока проход шёл: его находки относятся к предыдущей локации.
        if (sink is null) return;

        var found = 0;
        foreach (var pair in sink)
        {
            // Внутри пузыря источник правды — активные сущности: спящая карта может ещё держать
            // подобранный висп, и он воскрес бы на месте, где его уже нет.
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

        // Сущность первых кадров ещё не заполнена: нулевая позиция значит «читать пока нечего»,
        // а не «висп стоит в углу карты».
        if (!float.IsFinite(grid.X) || !float.IsFinite(grid.Y) || grid == Vector2.Zero) return false;

        var model = entity.TryGetComponent<Animated>(out var animated) ? animated.ModelPath : string.Empty;
        sighting = new WispSighting(
            GameLiterals.ResolveKind(model),
            grid,
            render.TerrainHeight,
            GameLiterals.IsLargeModel(model));

        return true;
    }
}
