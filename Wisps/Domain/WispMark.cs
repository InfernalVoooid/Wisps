using System.Numerics;

namespace Wisps.Domain;

internal readonly record struct WispMark(
    WispKind Kind,
    Vector2 Grid,
    float Height,
    bool IsLarge,
    long FirstSeenMs);
