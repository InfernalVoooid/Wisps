using System.Numerics;
using GameOffsets.Objects.UiElement;
using Wisps.Services;

namespace Wisps.UI;

/// <summary>
/// Проекция координат сетки на игровую большую карту (Tab) с учётом угла камеры и зума.
/// </summary>
/// <remarks>
/// Калибровочные константы взяты из плагина Radar эталонной сборки GameHelper2: карта рисуется
/// самой игрой и в памяти её нет, есть только центр, сдвиги и зум её UI-элемента.
/// </remarks>
internal static class MapProjector
{
    private const double CameraAngleRad = 38.7 * Math.PI / 180.0;

    private const float LargeMapScaleBaseline = 0.187812f;
    private const float MapScaleDivisor = 240f;
    private const float HeightPerGridCell = 10.86957f;

    private static readonly Vector2 CenterBias = new(0.6f, 0.3f);

    // Параметры проекции на кадр.
    internal readonly record struct MapView(Vector2 Center, Vector2 Origin, Vector2 Size, float Cos, float Sin)
    {
        internal bool IsValid => Cos > 0f && Sin > 0f;

        // Проецирует смещение в клетках относительно игрока в экранные пиксели карты.
        internal Vector2 Project(Vector2 gridDelta, float heightDelta) => Center + new Vector2(
            (gridDelta.X - gridDelta.Y) * Cos,
            ((heightDelta / HeightPerGridCell) - (gridDelta.X + gridDelta.Y)) * Sin);

        // Круг в клетках проецируется в эллипс с полуосями 2R*Cos и 2R*Sin; для ореола скопления
        // берём среднюю полуось — разница осей здесь меньше, чем размытие самого ореола.
        internal float GridToPixels(float gridRadius) => gridRadius * (Cos + Sin);
    }

    // Проекция для открытой большой карты. Недействительна, если карта скрыта или поверх неё
    // открыта карта мира.
    internal static MapView ForLargeMap()
    {
        var map = GameMemory.LargeMap;
        if (map.Address == nint.Zero || !map.IsVisible || GameMemory.IsWorldMapOpen) return default;

        var size = map.Size;
        if (size.Y <= 0f) return default;

        // Игра масштабирует карту по вертикали окна; ширина на масштаб не влияет.
        var baseResolution = UiElementBaseFuncs.BaseResolution;
        var baseDiagonal = Math.Sqrt((baseResolution.X * baseResolution.X) + (baseResolution.Y * baseResolution.Y));
        var diagonal = baseDiagonal * size.Y / baseResolution.Y;

        var scale = map.Zoom * LargeMapScaleBaseline;
        if (scale <= 0f) return default;

        var mapScale = MapScaleDivisor / scale;
        var cos = (float)(diagonal * Math.Cos(CameraAngleRad) / mapScale);
        var sin = (float)(diagonal * Math.Sin(CameraAngleRad) / mapScale);

        return new(
            Center: map.Center + map.Shift + map.DefaultShift + CenterBias,
            Origin: map.Position,
            Size: size,
            Cos: cos,
            Sin: sin);
    }
}
