using System.Numerics;

namespace Wisps.Domain;

// Отметка виспа на карте зоны. Координаты — в клетках сетки, высота — рельеф под виспом:
// изометрическая проекция карты требует обе.
internal readonly record struct WispMark(
    WispKind Kind,
    Vector2 Grid,
    float Height,
    bool IsLarge,
    long FirstSeenMs);
