using System.Numerics;

namespace Content.Client._Fish.LightShafts;

/// <summary>Пересечения геометрии shaft без временных коллекций.</summary>
public static class LightShaftGeometry
{
    /// <summary>
    /// Ограничивает дальность луча ближайшей стороной bbox в том же пространстве.
    /// Направление единичное; источник внутри bbox даёт нулевую дальность.
    /// </summary>
    public static float ClipRay(Vector2 origin, Vector2 direction, Box2 bounds, float length)
    {
        var near = 0f;
        var far = length;
        if (!ClipAxis(origin.X, direction.X, bounds.Left, bounds.Right, ref near, ref far) ||
            !ClipAxis(origin.Y, direction.Y, bounds.Bottom, bounds.Top, ref near, ref far))
            return length;
        return near;
    }

    private static bool ClipAxis(float origin, float direction, float min, float max, ref float near, ref float far)
    {
        // В отличие от деления на ноль в общем Ray.Intersects, граница параллельного луча не создаёт NaN.
        if (MathF.Abs(direction) < 0.0000001f)
            return origin >= min && origin <= max;
        var first = (min - origin) / direction;
        var last = (max - origin) / direction;
        near = MathF.Max(near, MathF.Min(first, last));
        far = MathF.Min(far, MathF.Max(first, last));
        return near <= far;
    }
}
