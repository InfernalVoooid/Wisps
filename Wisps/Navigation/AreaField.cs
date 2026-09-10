using System.Numerics;

namespace Wisps.Navigation;

/// <summary>
/// Поле стоимости прохода от игрока по достижимой части зоны.
/// </summary>
/// <remarks>
/// Считается волной Дейкстры по сетке проходимости в фоновом потоке и публикуется целиком: поток
/// отрисовки читает готовое поле, а не наполовину посчитанное. Оно отвечает на два вопроса —
/// сколько идти до точки и каким путём, — и оба нужны, чтобы подход к полосе обходил рельеф.
/// </remarks>
internal sealed class AreaField
{
    private const float Sqrt2 = 1.41421356f;
    private const float Unreached = float.MaxValue;

    // Прижатый к стене путь читается как проход сквозь неё: узкие клетки дороже широких.
    private const float ClearancePenalty = 2f;

    // Границы волны. Маршрут ведёт к ближайшей стоящей полосе, а не через всю зону, поэтому
    // считать дальше — платить кадром за то, чего никто не увидит.
    private const int MaxSettledCells = 120_000;
    private const float MaxCost = 320f;

    // Пересчёт привязан к пройденному пути, а не к таймеру: стоя на месте волна не устаревает.
    private const int RebuildIntervalMs = 1200;
    private const float RebuildMoveGrid = 45f;

    private const int MaxTraceSteps = 1500;
    private const int TraceSampleStep = 10;
    private const int SmoothLookAhead = 8;

    private static readonly (int Dx, int Dy)[] Steps =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    private static readonly float[] StepCost = [1f, Sqrt2, 1f, Sqrt2, 1f, Sqrt2, 1f, Sqrt2];

    private readonly PriorityQueue<int, float> open = new();

    private Wave front = new();
    private Wave back = new();
    private Task? build;

    private List<Vector2> trace = [];
    private List<Vector2> smoothed = [];

    private int builtGeneration = -1;
    private Vector2 builtFrom;

    private int pendingGeneration = -1;
    private Vector2 pendingFrom;

    private long nextBuildAtMs;

    // Волна и её буфер. Список задетых клеток избавляет от заливки всей сетки на каждый пересчёт:
    // платим за то, что реально обошли, а не за размер зоны.
    private sealed class Wave
    {
        internal float[] Cost = [];
        internal readonly List<int> Touched = [];
        internal int Width;
        internal int Height;

        internal void Prepare(int cells, int width, int height)
        {
            Width = width;
            Height = height;

            if (Cost.Length != cells)
            {
                Cost = new float[cells];
                Array.Fill(Cost, Unreached);
                Touched.Clear();
                return;
            }

            foreach (var index in Touched)
            {
                Cost[index] = Unreached;
            }

            Touched.Clear();
        }
    }

    internal bool IsReady => front.Width > 0 && front.Cost.Length > 0;

    // Растёт с каждым опубликованным полем: маршрут по нему понимает, что расстояния сменились.
    internal int Revision { get; private set; }

    // Освобождает буферы волны. Повторный вызов ничего не делает: ревизия не должна расти на
    // каждом кадре с выключенным маршрутом.
    internal void Reset()
    {
        if (front.Cost.Length == 0 && back.Cost.Length == 0 && build is null) return;

        front = new Wave();

        // Буфер идущей волны не трогаем — она в него пишет; освободится следующим сбросом.
        if (build is null) back = new Wave();

        builtGeneration = -1;
        nextBuildAtMs = 0;

        // Ссылка на идущую волну сохраняется: очередь и буфер у неё одни на класс, и вторая волна
        // поверх первой их перепишет. Отрицательное поколение — признак «результат выбросить».
        pendingGeneration = -1;
        Revision++;
    }

    internal void Tick(TerrainGrid grid, Vector2 playerGrid, long nowMs)
    {
        Publish();

        if (!grid.IsReady || build is not null || nowMs < nextBuildAtMs) return;

        var moved = Vector2.DistanceSquared(playerGrid, builtFrom) > RebuildMoveGrid * RebuildMoveGrid;
        if (IsReady && builtGeneration == grid.Generation && !moved) return;

        nextBuildAtMs = nowMs + RebuildIntervalMs;

        // Снимок берётся по значению: массив и его размеры связаны навсегда, и смена зоны под
        // идущей волной не даст ей прочитать новый массив со старыми размерами.
        var terrain = grid.Snapshot;
        var playerX = (int)MathF.Round(playerGrid.X);
        var playerY = (int)MathF.Round(playerGrid.Y);
        if (!terrain.TryFindNearestWalkable(playerX, playerY, out var startX, out var startY)) return;

        var wave = back;
        var startIndex = (startY * terrain.Width) + startX;
        pendingFrom = new Vector2(startX, startY);
        pendingGeneration = grid.Generation;

        // Выделенный поток, а не пул: волна занимает ядро надолго, а пул каждый кадр нужен ядру
        // фреймворка для параллельного чтения карты сущностей из потока отрисовки.
        build = Task.Factory.StartNew(
            () => Run(terrain, wave, open, startIndex),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    internal bool TryCost(Vector2 grid, out float cost)
    {
        cost = 0f;
        var index = IndexOf((int)MathF.Round(grid.X), (int)MathF.Round(grid.Y));
        if (index < 0 || front.Cost[index] >= Unreached) return false;

        cost = front.Cost[index];
        return true;
    }

    /// <summary>Выкладывает путь от игрока до точки в обход рельефа; возвращает число точек.</summary>
    internal int TracePath(in Walkability terrain, Vector2 target, Span<Vector2> points)
    {
        if (points.IsEmpty || !IsReady) return 0;

        var index = IndexOf((int)MathF.Round(target.X), (int)MathF.Round(target.Y));
        if (index < 0 || front.Cost[index] >= Unreached) return 0;

        trace.Clear();
        trace.Add(CellOf(index));

        for (var step = 0; step < MaxTraceSteps; step++)
        {
            if (!TryDescend(index, out var next)) break;

            index = next;
            if ((step + 1) % TraceSampleStep == 0) trace.Add(CellOf(index));
        }

        var start = CellOf(index);
        if (trace[^1] != start) trace.Add(start);

        Smooth(terrain);

        // Волна идёт от игрока, спуск — от цели: точки выкладываются в обратном порядке, чтобы
        // линия бежала к цели, а не от неё. Не поместившийся путь прореживается с сохранением
        // обоих концов: обрезанный хвост оставил бы разрыв там, где подход стыкуется с цепочкой.
        var total = trace.Count;
        if (total <= points.Length)
        {
            for (var i = 0; i < total; i++)
            {
                points[i] = trace[total - 1 - i];
            }

            return total;
        }

        var count = points.Length;
        for (var i = 0; i < count; i++)
        {
            var source = total - 1 - (int)MathF.Round(i * (total - 1f) / (count - 1f));
            points[i] = trace[source];
        }

        return count;
    }

    private void Publish()
    {
        if (build is not { IsCompleted: true } finished) return;

        build = null;
        if (finished.IsFaulted)
        {
            Console.WriteLine($"[Wisps] волна расстояний прервана: {finished.Exception?.GetBaseException().Message}");
            return;
        }

        // Поле сбросили, пока волна шла: её буфер уже никем не удерживается.
        if (pendingGeneration < 0) return;

        (front, back) = (back, front);
        builtFrom = pendingFrom;
        builtGeneration = pendingGeneration;
        Revision++;
    }

    private Vector2 CellOf(int index) => new(index % front.Width, index / front.Width);

    private int IndexOf(int x, int y) =>
        IsReady && (uint)x < (uint)front.Width && (uint)y < (uint)front.Height ? (y * front.Width) + x : -1;

    private bool TryDescend(int index, out int next)
    {
        next = index;
        var cost = front.Cost;
        var width = front.Width;
        var best = cost[index];
        var cx = index % width;
        var cy = index / width;

        foreach (var (dx, dy) in Steps)
        {
            var nx = cx + dx;
            var ny = cy + dy;
            if ((uint)nx >= (uint)width || (uint)ny >= (uint)front.Height) continue;

            var candidate = (ny * width) + nx;
            if (cost[candidate] >= best) continue;

            best = cost[candidate];
            next = candidate;
        }

        return next != index;
    }

    // Спуск по клеткам даёт лесенку, маршруту нужна линия: каждый раз берём самую дальнюю точку
    // впереди, которую ещё видно напрямую. Взгляд вперёд ограничен — за него платят лучом по клеткам.
    private void Smooth(in Walkability terrain)
    {
        if (trace.Count < 3) return;

        smoothed.Clear();
        smoothed.Add(trace[0]);

        var current = 0;
        while (current < trace.Count - 1)
        {
            var limit = Math.Min(current + SmoothLookAhead, trace.Count - 1);
            var next = current + 1;

            for (var candidate = limit; candidate > current + 1; candidate--)
            {
                if (!terrain.HasClearLine(trace[current], trace[candidate])) continue;

                next = candidate;
                break;
            }

            smoothed.Add(trace[next]);
            current = next;
        }

        (trace, smoothed) = (smoothed, trace);
    }

    private static void Run(Walkability terrain, Wave wave, PriorityQueue<int, float> open, int startIndex)
    {
        var cells = terrain.Cells;
        var width = terrain.Width;
        var height = terrain.Height;
        var maxClearance = MathF.Max(1f, terrain.MaxClearance);

        wave.Prepare(cells.Length, width, height);

        var cost = wave.Cost;
        var touched = wave.Touched;

        open.Clear();
        cost[startIndex] = 0f;
        touched.Add(startIndex);
        open.Enqueue(startIndex, 0f);

        var settled = 0;
        while (open.TryDequeue(out var current, out var priority))
        {
            if (priority > cost[current]) continue;
            if (priority > MaxCost || ++settled > MaxSettledCells) break;

            var clearance = MathF.Max(cells[current], 1f);
            var lane = 1f + (ClearancePenalty * (1f - (clearance / maxClearance)));

            var cx = current % width;
            var cy = current / width;

            for (var step = 0; step < Steps.Length; step++)
            {
                var (dx, dy) = Steps[step];
                var nx = cx + dx;
                var ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;

                var index = (ny * width) + nx;
                if (cells[index] == 0) continue;

                // Диагональ мимо угла стены: без этого запрета путь просачивается между двумя
                // клетками, которые сам же обходит.
                if (dx != 0 && dy != 0 && (cells[(cy * width) + nx] == 0 || cells[(ny * width) + cx] == 0)) continue;

                var tentative = priority + (StepCost[step] * lane);
                var known = cost[index];
                if (tentative >= known) continue;

                if (known >= Unreached) touched.Add(index);
                cost[index] = tentative;
                open.Enqueue(index, tentative);
            }
        }

        open.Clear();
    }
}
