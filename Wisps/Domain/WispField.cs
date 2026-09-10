using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wisps.Domain;

/// <summary>
/// Реестр виспов текущей зоны: и те, что игра показывает сейчас, и те, мимо которых уже прошли.
/// </summary>
/// <remarks>
/// Висп забывается по единственному признаку — игра перестала его подтверждать рядом с игроком.
/// Дальше сетевого пузыря отсутствие подтверждения не значит ничего: сущность выгружена, а висп
/// на месте. Эта память и даёт обзор всей зоны вместо одного текущего пузыря.
/// </remarks>
internal sealed class WispField
{
    // Подтверждение, а не выдержка: IsValid теряет отдельные проходы на разорванном чтении карты
    // сущностей, и один пропуск исчезновением не считается.
    private const int MissedScansBeforeGone = 2;

    // Смещение позиции меньше половины клетки — дрожание чтения, а не переезд виспа.
    private const float PositionEpsilonSq = 0.25f;

    private readonly Dictionary<uint, Entry> entries = [];
    private readonly List<uint> gone = [];
    private readonly int[] counts = new int[WispKinds.Count];
    private readonly int[] collected = new int[WispKinds.Count];

    private WispMark[] snapshot = [];
    private int snapshotCount;
    private int snapshotVersion = -1;

    private int scan;
    private int version;

    private struct Entry
    {
        internal WispMark Mark;
        internal int LastScan;
    }

    // Растёт при любом изменении состава: по нему инвалидируются снимок и подсказка маршрута.
    internal int Version => version;

    internal int Total => entries.Count;

    internal int CountOf(WispKind kind) => counts[(int)kind];

    // Собрано за зону по ярусам. Единственный источник — исчезновение виспа рядом с игроком:
    // об этом сообщает сама игра, гадать по расстоянию и таймеру не нужно.
    internal ReadOnlySpan<int> Collected => collected;

    internal int CollectedOf(WispKind kind) => collected[(int)kind];

    // Растёт с каждым собранным виспом: по нему пересобираются строки худа.
    internal int HarvestVersion { get; private set; }

    internal ReadOnlySpan<WispMark> Marks
    {
        get
        {
            if (snapshotVersion != version) RebuildSnapshot();
            return snapshot.AsSpan(0, snapshotCount);
        }
    }

    internal void Clear()
    {
        entries.Clear();
        Array.Clear(counts);
        Array.Clear(collected);
        HarvestVersion++;
        snapshotCount = 0;
        snapshotVersion = -1;
        scan = 0;
        version++;
    }

    internal void BeginScan() => scan++;

    // Отмечает уже известный висп живым, не читая его компоненты. У Useless-сущности они всё
    // равно заморожены, и повторное чтение каждого прохода — чистая трата на горячем пути.
    // Единственное исключение — нераспознанный ярус: модель могла подгрузиться позже.
    internal bool TryTouch(uint id)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(entries, id);
        if (Unsafe.IsNullRef(ref entry)) return false;

        entry.LastScan = scan;
        return entry.Mark.Kind != WispKind.Unknown;
    }

    internal void Observe(uint id, WispKind kind, Vector2 grid, float height, bool isLarge, long nowMs)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(entries, id, out var existed);
        entry.LastScan = scan;

        if (!existed)
        {
            entry.Mark = new WispMark(kind, grid, height, isLarge, nowMs);
            counts[(int)kind]++;
            version++;
            return;
        }

        // Компоненты Useless-сущности перестают обновляться через несколько кадров, поэтому чтения
        // сходятся к финальному значению — принимаем их, пока сущность жива.
        if (entry.Mark.Kind == kind &&
            entry.Mark.IsLarge == isLarge &&
            Vector2.DistanceSquared(entry.Mark.Grid, grid) <= PositionEpsilonSq)
        {
            return;
        }

        if (entry.Mark.Kind != kind)
        {
            counts[(int)entry.Mark.Kind]--;
            counts[(int)kind]++;
        }

        entry.Mark = entry.Mark with { Kind = kind, Grid = grid, Height = height, IsLarge = isLarge };
        version++;
    }

    // Убирает виспы, которых игра больше не подтверждает в пределах сетевого пузыря: там, где она
    // их видит, молчание означает «подобрали».
    internal void ForgetCollected(Vector2 playerGrid, float bubbleGrid)
    {
        var bubbleSq = bubbleGrid * bubbleGrid;
        gone.Clear();

        foreach (var pair in entries)
        {
            if (scan - pair.Value.LastScan < MissedScansBeforeGone) continue;
            if (Vector2.DistanceSquared(playerGrid, pair.Value.Mark.Grid) > bubbleSq) continue;

            gone.Add(pair.Key);
        }

        foreach (var id in gone)
        {
            if (!entries.Remove(id, out var entry)) continue;

            counts[(int)entry.Mark.Kind]--;
            collected[(int)entry.Mark.Kind]++;
            HarvestVersion++;
            version++;
        }
    }

    private void RebuildSnapshot()
    {
        if (snapshot.Length < entries.Count)
        {
            Array.Resize(ref snapshot, Math.Max(64, entries.Count * 2));
        }

        snapshotCount = 0;
        foreach (var pair in entries)
        {
            snapshot[snapshotCount++] = pair.Value.Mark;
        }

        snapshotVersion = version;
    }
}
