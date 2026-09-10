using System.Numerics;
using GameHelper.RemoteObjects.States.InGameStateObjects;

namespace Wisps.Navigation;

internal readonly record struct Walkability(byte[] Cells, int Width, int Height, int MaxClearance)
{
    private const int NearestWalkableRadius = 12;

    internal bool IsReady => Width > 0 && Cells.Length >= Width * Height;

    internal int ClearanceAt(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? Cells[(y * Width) + x] : -1;

    internal bool IsWalkable(int x, int y) => ClearanceAt(x, y) > 0;

    // Прямая проходимость по Брезенхэму без срезания углов стен.
    internal bool HasClearLine(Vector2 from, Vector2 to)
    {
        if (!IsReady) return false;

        var x0 = (int)MathF.Round(from.X);
        var y0 = (int)MathF.Round(from.Y);
        var x1 = (int)MathF.Round(to.X);
        var y1 = (int)MathF.Round(to.Y);

        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var stepX = x0 < x1 ? 1 : -1;
        var stepY = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            if (!IsWalkable(x0, y0)) return false;
            if (x0 == x1 && y0 == y1) return true;

            var doubled = error * 2;
            var movesX = doubled >= dy;
            var movesY = doubled <= dx;

            if (movesX && movesY && (!IsWalkable(x0 + stepX, y0) || !IsWalkable(x0, y0 + stepY))) return false;

            if (movesX)
            {
                error += dy;
                x0 += stepX;
            }

            if (movesY)
            {
                error += dx;
                y0 += stepY;
            }
        }
    }

    internal bool TryFindNearestWalkable(int x, int y, out int foundX, out int foundY)
    {
        foundX = x;
        foundY = y;
        if (IsWalkable(x, y)) return true;

        for (var radius = 1; radius <= NearestWalkableRadius; radius++)
        {
            for (var offset = -radius; offset <= radius; offset++)
            {
                if (TryAccept(x + offset, y - radius, out foundX, out foundY)) return true;
                if (TryAccept(x + offset, y + radius, out foundX, out foundY)) return true;
                if (TryAccept(x - radius, y + offset, out foundX, out foundY)) return true;
                if (TryAccept(x + radius, y + offset, out foundX, out foundY)) return true;
            }
        }

        return false;
    }

    private bool TryAccept(int x, int y, out int foundX, out int foundY)
    {
        foundX = x;
        foundY = y;
        return IsWalkable(x, y);
    }
}

internal sealed class TerrainGrid
{
    private const int TileToGridConversion = 23;

    private byte[] source = [];
    private Walkability snapshot;

    internal Walkability Snapshot => snapshot;

    internal bool IsReady => snapshot.IsReady;

    internal int Generation { get; private set; }

    internal void Sync(AreaInstance area)
    {
        var data = area.GridWalkableData;
        if (ReferenceEquals(data, source)) return;

        source = data;
        Generation++;

        var metadata = area.TerrainMetadata;
        var bytesPerRow = metadata.BytesPerRow;
        if (data is null || bytesPerRow <= 0 || data.Length < bytesPerRow)
        {
            snapshot = default;
            return;
        }

        var height = data.Length / bytesPerRow;

        // Байт хранит 2 клетки; фактическая ширина ограничена размером тайлов.
        var packedWidth = bytesPerRow * 2;
        var tiledWidth = (int)metadata.TotalTiles.X * TileToGridConversion;
        var width = tiledWidth > 0 ? Math.Min(packedWidth, tiledWidth) : packedWidth;

        snapshot = Decode(data, bytesPerRow, width, height);
    }

    internal void Release()
    {
        if (source.Length == 0 && !snapshot.IsReady) return;

        source = [];
        snapshot = default;
        Generation++;
    }

    private static Walkability Decode(byte[] data, int bytesPerRow, int width, int height)
    {
        var decoded = new byte[width * height];
        var maxClearance = 1;

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * bytesPerRow;
            var target = y * width;
            for (var x = 0; x < width; x++)
            {
                var index = rowStart + (x / 2);
                if ((uint)index >= (uint)data.Length) continue;

                var value = (byte)((data[index] >> ((x & 1) == 0 ? 0 : 4)) & 0xF);
                decoded[target + x] = value;
                if (value > maxClearance) maxClearance = value;
            }
        }

        return new Walkability(decoded, width, height, maxClearance);
    }
}
