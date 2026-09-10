using System.Numerics;
using System.Runtime.InteropServices;
using Wisps.Navigation;

namespace Wisps.Domain;

// Точка сбора на маршруте: сам висп, к которому идём.
internal readonly record struct RouteStop(Vector2 Grid, float Height, WispKind Kind);

/// <summary>
/// Маршрут сбора: подход к началу полосы в обход рельефа и цепочка виспов вдоль неё.
/// </summary>
/// <remarks>
/// Виспы стоят полосами, поэтому маршрут — ломаная по самим виспам, а не круг вокруг них. Перебор
/// цепочек идёт в фоновом потоке по снятому снимку: поток отрисовки только снимает вход и забирает
/// готовый результат, считать он не должен ничего.
/// </remarks>
internal sealed class WispRoute
{
    internal const int MaxStops = 48;
    internal const int MaxApproachPoints = 40;

    // Насколько далеко может стоять следующий висп полосы. Больше — маршрут начинает прыгать
    // между соседними полосами, меньше — рвётся на разрывах.
    private const float ChainStepGrid = 45f;

    // Цена дороги относительно добычи: во сколько клеток обходится один «вес» виспа.
    private const float ApproachScaleGrid = 90f;

    // Скопление важнее близости: маршрут ведёт туда, где виспов много, а не туда, где ближайший.
    // Плотность считается по ячейкам сетки с суммой по блоку 3x3, поэтому граница ячейки не режет
    // скопление пополам.
    private const float DensityCellGrid = 30f;
    private const float DensityFactor = 2f;

    // Насколько сильно отстающий ярус может перевесить ведущий в равномерном наборе.
    private const float MaxDeficit = 4f;

    private const int SeedCount = 3;
    private const int DirectionsPerSeed = 2;
    private const int MaxCandidates = 16;

    // Точки входа обязаны стоять врозь: три соседних виспа одной полосы дают три одинаковые
    // цепочки и ни одной альтернативы для сравнения.
    private const float SeedSeparationGrid = ChainStepGrid * 2f;

    private const int RebuildIntervalMs = 500;

    // Рисуемое состояние. Принадлежит потоку отрисовки.
    private readonly RouteStop[] stops = new RouteStop[MaxStops];
    private readonly Vector2[] approach = new Vector2[MaxApproachPoints];
    private readonly int[] byKind = new int[WispKinds.Count];
    private int stopCount;
    private int approachCount;

    // Вход фонового счёта. Пишется потоком отрисовки только пока счёт не идёт.
    private readonly float[] tierGain = new float[WispKinds.Count];
    private WispMark[] inputMarks = [];
    private float[] inputCost = [];
    private bool[] inputReachable = [];
    private Walkability inputTerrain;
    private KindMask inputVisible;
    private int inputCount;

    // Рабочие буферы фонового счёта.
    private readonly RouteStop[] pendingStops = new RouteStop[MaxStops];
    private readonly RouteStop[] chainStops = new RouteStop[MaxStops];
    private readonly Candidate[] candidates = new Candidate[MaxCandidates];
    private readonly List<int> seeds = new(SeedCount);
    private readonly Dictionary<long, float> cells = [];
    private bool[] taken = [];
    private float[] density = [];
    private float[] boost = [];
    private int pendingCount;
    private int chainCount;

    private Task? build;
    private bool discardBuild;

    private Vector2 approachTarget = new(float.MinValue);
    private int approachRevision = -1;

    private int builtWispVersion = -1;
    private int builtFieldRevision = -1;
    private int builtMask = -1;
    private bool builtBalanced;
    private long nextRebuildAtMs;

    private readonly record struct Candidate(int Index, float Score, float Distance);

    internal ReadOnlySpan<RouteStop> Stops => stops.AsSpan(0, stopCount);

    internal ReadOnlySpan<Vector2> Approach => approach.AsSpan(0, approachCount);

    internal ReadOnlySpan<int> ByKind => byKind;

    internal int Count => stopCount;

    internal bool HasRoute => stopCount > 0;

    internal void Clear()
    {
        stopCount = 0;
        Array.Clear(byKind);
        ClearApproach();

        builtWispVersion = -1;
        builtFieldRevision = -1;
        builtMask = -1;
        nextRebuildAtMs = 0;

        // Идущий перебор досчитает в свой буфер, но его результат уже не о том, что показано.
        if (build is not null) discardBuild = true;
    }

    internal void Tick(WispField wisps, TerrainGrid grid, AreaField field, KindMask visible, bool balanced, long nowMs)
    {
        Publish(field, grid.Snapshot);

        if (build is not null || nowMs < nextRebuildAtMs) return;

        var stale = wisps.Version != builtWispVersion ||
                    field.Revision != builtFieldRevision ||
                    visible.Bits != builtMask ||
                    balanced != builtBalanced;

        if (!stale) return;

        nextRebuildAtMs = nowMs + RebuildIntervalMs;
        builtWispVersion = wisps.Version;
        builtFieldRevision = field.Revision;
        builtMask = visible.Bits;
        builtBalanced = balanced;

        if (!Snapshot(wisps, grid, field, visible, balanced))
        {
            stopCount = 0;
            Array.Clear(byKind);
            ClearApproach();
            return;
        }

        // Выделенный поток, а не пул: пул каждый кадр нужен ядру фреймворка для параллельного
        // чтения карты сущностей из потока отрисовки.
        build = Task.Factory.StartNew(
            RunBuild,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    // Снимок входа: дальше фоновый поток не касается ни реестра, ни поля, ни сетки — только
    // собственных массивов, которые здесь и заполнены.
    private bool Snapshot(WispField wisps, TerrainGrid grid, AreaField field, KindMask visible, bool balanced)
    {
        var marks = wisps.Marks;
        inputTerrain = grid.Snapshot;
        inputVisible = visible;
        inputCount = 0;

        if (marks.IsEmpty || !field.IsReady || !inputTerrain.IsReady) return false;

        EnsureBuffers(marks.Length);
        marks.CopyTo(inputMarks);
        inputCount = marks.Length;

        for (var i = 0; i < inputCount; i++)
        {
            inputReachable[i] = field.TryCost(inputMarks[i].Grid, out inputCost[i]);
        }

        BuildTierGain(wisps, balanced);
        return true;
    }

    private void Publish(AreaField field, in Walkability terrain)
    {
        if (build is not { IsCompleted: true } finished) return;

        build = null;
        var discarded = discardBuild;
        discardBuild = false;

        if (finished.IsFaulted)
        {
            Console.WriteLine($"[Wisps] перебор маршрута прерван: {finished.Exception?.GetBaseException().Message}");
            return;
        }

        if (discarded) return;

        stopCount = pendingCount;
        pendingStops.AsSpan(0, pendingCount).CopyTo(stops);

        Array.Clear(byKind);
        for (var i = 0; i < stopCount; i++)
        {
            byKind[(int)stops[i].Kind]++;
        }

        if (stopCount == 0)
        {
            ClearApproach();
            return;
        }

        RefreshApproach(field, terrain);
    }

    // Подход перекладывается только при смене цели или самого поля: спуск по клеткам со
    // сглаживанием остаётся на потоке отрисовки, потому что читает живое поле расстояний.
    private void RefreshApproach(AreaField field, in Walkability terrain)
    {
        if (approachRevision == field.Revision && approachTarget == stops[0].Grid) return;

        approachRevision = field.Revision;
        approachTarget = stops[0].Grid;
        approachCount = field.TracePath(terrain, stops[0].Grid, approach);
    }

    private void ClearApproach()
    {
        approachCount = 0;
        approachRevision = -1;
        approachTarget = new Vector2(float.MinValue);
    }

    private void EnsureBuffers(int marks)
    {
        if (inputMarks.Length >= marks) return;

        var size = Math.Max(64, marks * 2);
        inputMarks = new WispMark[size];
        inputCost = new float[size];
        inputReachable = new bool[size];
        taken = new bool[size];
        density = new float[size];
        boost = new float[size];
    }

    // Ценность яруса на эту пересборку. Обычный ход считает по редкости яруса; равномерный
    // гасит редкость и смотрит на две вещи, которые сама игра и сообщает: чего уже собрано меньше
    // и чего в зоне меньше — второго не хватит, если разбирать зону подряд.
    private void BuildTierGain(WispField wisps, bool balanced)
    {
        if (!balanced)
        {
            for (var i = 0; i < tierGain.Length; i++)
            {
                tierGain[i] = WispKinds.TierWeight((WispKind)i);
            }

            return;
        }

        var mostCollected = 0;
        var mostAvailable = 0;
        for (var i = 0; i < tierGain.Length; i++)
        {
            mostCollected = Math.Max(mostCollected, wisps.CollectedOf((WispKind)i));
            mostAvailable = Math.Max(mostAvailable, wisps.CountOf((WispKind)i));
        }

        for (var i = 0; i < tierGain.Length; i++)
        {
            var kind = (WispKind)i;
            var available = wisps.CountOf(kind);
            var collected = wisps.CollectedOf(kind);

            // Ярус, которого в зоне нет и не было, в равнении не участвует.
            if (available == 0 && collected == 0)
            {
                tierGain[i] = 0f;
                continue;
            }

            var behind = MathF.Min(MaxDeficit, (mostCollected + 1f) / (collected + 1f));
            var scarcity = mostAvailable > 0 ? 1f / (1f + ((float)available / mostAvailable)) : 1f;
            tierGain[i] = behind * scarcity;
        }
    }

    private void RunBuild()
    {
        pendingCount = 0;
        BuildDensity();
        CollectSeeds();
        if (seeds.Count == 0) return;

        var bestValue = 0f;

        // Точка входа обычно стоит в середине полосы, и жадный шаг уводит по ней в одну сторону.
        // Второй прогон отказывается от первого шага в пользу следующего кандидата и уходит в
        // другую: без этого половина полосы просто не рассматривается.
        foreach (var seed in seeds)
        {
            for (var direction = 0; direction < DirectionsPerSeed; direction++)
            {
                var value = Chain(seed, direction);
                if (value <= bestValue) continue;

                bestValue = value;
                pendingCount = chainCount;
                chainStops.AsSpan(0, chainCount).CopyTo(pendingStops);
            }
        }
    }

    // Вес окрестности каждой отметки: сколько добычи стоит вокруг неё, а не в ней одной.
    private void BuildDensity()
    {
        cells.Clear();
        for (var i = 0; i < inputCount; i++)
        {
            if (!inputVisible.Has(inputMarks[i].Kind)) continue;

            ref var cell = ref CollectionsMarshal.GetValueRefOrAddDefault(cells, CellKey(inputMarks[i].Grid), out _);
            cell += Gain(inputMarks[i]);
        }

        var peak = 0f;
        for (var i = 0; i < inputCount; i++)
        {
            density[i] = inputVisible.Has(inputMarks[i].Kind) ? BlockWeight(inputMarks[i].Grid) : 0f;
            if (density[i] > peak) peak = density[i];
        }

        for (var i = 0; i < inputCount; i++)
        {
            boost[i] = peak > 0f ? 1f + (DensityFactor * (density[i] / peak)) : 1f;
        }
    }

    private float BlockWeight(Vector2 grid)
    {
        var cellX = (int)MathF.Floor(grid.X / DensityCellGrid);
        var cellY = (int)MathF.Floor(grid.Y / DensityCellGrid);
        var weight = 0f;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (cells.TryGetValue(PackCell(cellX + dx, cellY + dy), out var cell)) weight += cell;
            }
        }

        return weight;
    }

    // Три точки входа вместо одной: у ближайшего виспа и у начала дальней богатой полосы разные
    // итоги, и сравнить их можно, только собрав обе цепочки.
    private void CollectSeeds()
    {
        seeds.Clear();
        while (seeds.Count < SeedCount)
        {
            var best = -1;
            var bestScore = 0f;

            for (var i = 0; i < inputCount; i++)
            {
                if (!inputReachable[i] || !inputVisible.Has(inputMarks[i].Kind)) continue;
                if (IsNearSeed(inputMarks[i].Grid)) continue;

                var score = density[i] / (1f + (inputCost[i] / ApproachScaleGrid));
                if (score <= bestScore) continue;

                bestScore = score;
                best = i;
            }

            if (best < 0) return;

            seeds.Add(best);
        }
    }

    private bool IsNearSeed(Vector2 grid)
    {
        foreach (var seed in seeds)
        {
            if (Vector2.DistanceSquared(inputMarks[seed].Grid, grid) <= SeedSeparationGrid * SeedSeparationGrid) return true;
        }

        return false;
    }

    // Жадная цепочка по полосе. Возвращает ценность хода: добыча против пройденного пути.
    private float Chain(int seed, int firstStepChoice)
    {
        Array.Clear(taken, 0, inputCount);

        var current = inputMarks[seed].Grid;
        var gain = Gain(inputMarks[seed]);
        var travel = inputCost[seed];

        taken[seed] = true;
        chainCount = 0;
        chainStops[chainCount++] = StopAt(seed);

        var bestScore = gain / (1f + (travel / ApproachScaleGrid));
        var bestPrefix = chainCount;

        while (chainCount < MaxStops)
        {
            var found = RankNeighbours(current);
            var stepped = false;
            var skip = chainCount == 1 ? firstStepChoice : 0;

            for (var i = 0; i < found; i++)
            {
                var candidate = candidates[i];
                if (!inputTerrain.HasClearLine(current, inputMarks[candidate.Index].Grid)) continue;

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                taken[candidate.Index] = true;
                current = inputMarks[candidate.Index].Grid;
                gain += Gain(inputMarks[candidate.Index]);
                travel += candidate.Distance;
                chainStops[chainCount++] = StopAt(candidate.Index);

                stepped = true;
                break;
            }

            if (!stepped) break;

            var score = gain / (1f + (travel / ApproachScaleGrid));
            if (score <= bestScore) continue;

            bestScore = score;
            bestPrefix = chainCount;
        }

        // Жадный ход под конец уходит в разреженное и тянет вниз всё отношение: оставляем лучший
        // его отрезок, а не весь пройденный.
        chainCount = bestPrefix;
        return bestScore;
    }

    // Ближайшие невзятые виспы по убыванию выгоды. Проходимость проверяется потом и только для
    // них: луч по клеткам дороже сравнения расстояний, и платить за него всем подряд незачем.
    private int RankNeighbours(Vector2 from)
    {
        var found = 0;
        for (var i = 0; i < inputCount; i++)
        {
            if (taken[i] || !inputVisible.Has(inputMarks[i].Kind)) continue;

            var distance = Vector2.Distance(from, inputMarks[i].Grid);
            if (distance > ChainStepGrid) continue;

            var score = Gain(inputMarks[i]) * boost[i] / (1f + distance);
            if (found == MaxCandidates && score <= candidates[found - 1].Score) continue;

            var slot = found < MaxCandidates ? found++ : MaxCandidates - 1;
            while (slot > 0 && candidates[slot - 1].Score < score)
            {
                candidates[slot] = candidates[slot - 1];
                slot--;
            }

            candidates[slot] = new Candidate(i, score, distance);
        }

        return found;
    }

    private RouteStop StopAt(int index) =>
        new(inputMarks[index].Grid, inputMarks[index].Height, inputMarks[index].Kind);

    private float Gain(in WispMark mark) => tierGain[(int)mark.Kind] * WispKinds.SizeFactor(mark.IsLarge);

    private static long CellKey(Vector2 grid) =>
        PackCell((int)MathF.Floor(grid.X / DensityCellGrid), (int)MathF.Floor(grid.Y / DensityCellGrid));

    private static long PackCell(int x, int y) => ((long)x << 32) | (uint)y;
}
