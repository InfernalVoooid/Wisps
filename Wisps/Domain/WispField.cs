using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wisps.Domain;

internal sealed class WispField
{
    // Защита от потери кадра при разорванном чтении сущностей.
    private const int MissedScansBeforeGone = 2;

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

    internal int Version => version;

    internal int Total => entries.Count;

    internal int CountOf(WispKind kind) => counts[(int)kind];

    internal ReadOnlySpan<int> Collected => collected;

    internal int CollectedOf(WispKind kind) => collected[(int)kind];

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

    // Компоненты Useless-сущности заморожены; перечитываем только при Unknown ярусе.
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
