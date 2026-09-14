using System.Numerics;
using Content.Shared._Fish.LightShafts;
using Content.Shared.Chemistry.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Fish.LightShafts;

/// <summary>Ограниченный по бюджету веер видимости с дешёвым процедурным градиентом.</summary>
public sealed class LightShaftOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;

    private const int MaxSources = 100;
    private const int MaxAngles = 1024;
    private const float MaxLength = 16f;
    private const float CornerEpsilon = 0.0001f;
    private readonly Entry[] _visible = new Entry[MaxSources];
    private readonly ShaderInstance[] _shaders = new ShaderInstance[MaxSources];
    private readonly float[] _parameters = new float[MaxSources * 8];
    private readonly float[] _angles = new float[MaxAngles];
    private readonly DrawVertexUV2D[] _vertices = new DrawVertexUV2D[MaxAngles + 1];
    private readonly Vector2[] _smoke = new Vector2[256];
    private readonly Blocker[] _blockers = new Blocker[256];
    private readonly SceneBlocker[] _sceneBlockers = new SceneBlocker[4096];
    private readonly bool[] _hits = new bool[MaxAngles];
    private readonly SharedTransformSystem _transform;
    private readonly ShaderInstance _debugShader;
    private int _visibleCount;
    private int _smokeCount;
    private int _angleCount;
    private int _occluderCount;
    private int _sceneCount;
    private bool _sceneOverflow;
    private bool _overflow;
    private Vector2 _origin;
    private float _direction;
    private float _halfAngle;
    private float _centerLength;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public LightShaftOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        Array.Fill(_parameters, float.NaN);
        ZIndex = -100;
        for (var i = 0; i < MaxSources; i++)
            _shaders[i] = _prototype.Index<ShaderPrototype>("FishLightShaft").InstanceUnique();
        _debugShader = _prototype.Index<ShaderPrototype>("unshaded").Instance();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _visibleCount = 0;
        _smokeCount = 0;
        if (!_configuration.GetCVar(LightShaftCVars.Enabled) || args.Viewport.Eye == null)
            return false;

        var max = Math.Clamp(_configuration.GetCVar(LightShaftCVars.MaxVisible), 0, MaxSources);
        if (max == 0)
            return false;
        var query = _entities.EntityQueryEnumerator<LightShaftComponent, TransformComponent>();
        var eye = args.Viewport.Eye.Position.Position;
        while (query.MoveNext(out var uid, out var shaft, out var xform))
        {
            if (!shaft.Enabled || xform.MapID != args.MapId || shaft.Intensity < 0.001f || !float.IsFinite(shaft.Intensity) ||
                !float.IsFinite(shaft.Length) || !float.IsFinite(shaft.ConeAngle) || !float.IsFinite(shaft.Direction))
                continue;
            var length = Math.Clamp(shaft.Length, 0.1f, MaxLength);
            var (_, rotation, matrix) = _transform.GetWorldPositionRotationMatrix(xform);
            var origin = Vector2.Transform(shaft.Offset, matrix);
            if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) ||
                !args.WorldAABB.Enlarged(length).Contains(origin))
                continue;
            var intensity = Math.Clamp(shaft.Intensity, 0f, 1f);
            var color = shaft.Color;
            if (_entities.TryGetComponent<PointLightComponent>(uid, out var light))
            {
                if (shaft.FollowLight)
                {
                    if (!light.Enabled || light.ContainerOccluded || !float.IsFinite(light.Energy) || light.Energy <= 0f)
                        continue;
                    intensity *= Math.Clamp(light.Energy, 0f, 2f);
                }
                if (shaft.UseLightColor)
                    color = light.Color;
            }
            if (_entities.TryGetComponent<SpriteComponent>(uid, out var sprite) && (!sprite.Visible || sprite.ContainerOccluded))
                continue;
            if (intensity * color.A < 0.001f)
                continue;

            // Сначала обслуживаем ближайшие источники, оставляя дальние за пределами бюджета.
            var distance = Vector2.DistanceSquared(origin, eye);
            var index = _visibleCount;
            while (index > 0 && distance < _visible[index - 1].Distance)
            {
                if (index < max)
                    _visible[index] = _visible[index - 1];
                index--;
            }
            if (index >= max)
                continue;
            _visible[index] = new Entry(shaft, origin, (float) rotation.Theta + shaft.Direction % 360f * (MathF.PI / 180f),
                length, intensity, color, distance);
            _visibleCount = Math.Min(_visibleCount + 1, max);
        }

        var needsSmoke = false;
        var needsOcclusion = false;
        for (var i = 0; i < _visibleCount; i++)
        {
            needsSmoke |= _visible[i].Component.SmokeReactive;
            needsOcclusion |= _visible[i].Component.OcclusionEnabled;
        }
        _sceneCount = 0;
        _sceneOverflow = false;
        if (needsOcclusion)
        {
            // Публичный QueryAabb создаёт ValueList деревьев. Один ECS-проход не выделяет коллекций
            // и видит новое состояние двери даже до обновления её spatial tree.
            var occluders = _entities.EntityQueryEnumerator<OccluderComponent, TransformComponent>();
            var bounds = args.WorldAABB.Enlarged(MaxLength * 2f);
            while (occluders.MoveNext(out _, out var occluder, out var xform))
            {
                if (!occluder.Enabled || xform.MapID != args.MapId || !xform.ParentUid.IsValid())
                    continue;
                var (_, _, matrix, inverse) = _transform.GetWorldPositionRotationMatrixWithInv(xform.ParentUid);
                var box = occluder.BoundingBox;
                box = box.Translated(xform.LocalPosition);
                var worldBounds = matrix.TransformBox(box);
                if (!worldBounds.Intersects(bounds))
                    continue;
                if (_sceneCount == _sceneBlockers.Length)
                {
                    _sceneOverflow = true;
                    break;
                }
                _sceneBlockers[_sceneCount++] = new SceneBlocker(box, worldBounds, matrix, inverse);
            }
        }
        if (needsSmoke)
        {
            var smoke = _entities.EntityQueryEnumerator<SmokeComponent, TransformComponent>();
            var bounds = args.WorldAABB.Enlarged(MaxLength);
            while (_smokeCount < _smoke.Length && smoke.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID != args.MapId)
                    continue;
                var position = _transform.GetWorldPosition(xform);
                if (bounds.Contains(position))
                    _smoke[_smokeCount++] = position;
            }
        }
        return _visibleCount > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var budget = Math.Clamp(_configuration.GetCVar(LightShaftCVars.RayBudget), 0, 16384);
        var debug = _configuration.GetCVar(LightShaftCVars.Debug);
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        try
        {
            for (var i = 0; i < _visibleCount; i++)
            {
                if (budget < 17)
                    break;
                ref var entry = ref _visible[i];
                var shaft = entry.Component;
                if (shaft.OcclusionEnabled && _sceneOverflow)
                    continue;
                _origin = entry.Origin;
                _direction = entry.Direction;
                _halfAngle = Math.Clamp(shaft.ConeAngle, 5f, 150f) * MathF.PI / 360f;
                _centerLength = entry.Length;
                _angleCount = 0;
                _occluderCount = 0;
                _overflow = false;
                for (var ray = 0; ray <= 16; ray++)
                    AddAngle(-_halfAngle + 2f * _halfAngle * ray / 16f);
                if (shaft.OcclusionEnabled)
                {
                    var bounds = Box2.CenteredAround(_origin, new Vector2(entry.Length * 2f));
                    for (var j = 0; j < _sceneCount; j++)
                    {
                        if (_sceneBlockers[j].WorldBounds.Intersects(bounds))
                            CollectCorners(_sceneBlockers[j]);
                        if (_overflow)
                            break;
                    }
                }
                // Нельзя деградировать до необрезанного конуса: при переполнении безопаснее пропустить источник.
                if (_overflow)
                    continue;
                Array.Sort(_angles, 0, _angleCount);
                var unique = 1;
                for (var j = 1; j < _angleCount; j++)
                {
                    if (_angles[j] != _angles[unique - 1])
                        _angles[unique++] = _angles[j];
                }
                _angleCount = unique;
                if (_angleCount > budget)
                    continue;
                budget -= _angleCount;
                _vertices[0] = new DrawVertexUV2D(_origin, Vector2.Zero);
                var width = entry.Length * MathF.Tan(_halfAngle);
                for (var j = 0; j < _angleCount; j++)
                {
                    var angle = _angles[j];
                    var direction = new Vector2(MathF.Cos(_direction + angle), MathF.Sin(_direction + angle));
                    var distance = entry.Length;
                    // IntersectRay возвращает первый, не обязательно ближайший hit дерева.
                    // Проверяем заранее собранные bbox без повторного обхода/аллокаций дерева на каждый луч.
                    for (var k = 0; k < _occluderCount; k++)
                    {
                        ref var blocker = ref _blockers[k];
                        distance = LightShaftGeometry.ClipRay(blocker.Origin,
                            Vector2.TransformNormal(direction, blocker.Inverse), blocker.Bounds, distance);
                    }
                    _hits[j] = distance < entry.Length;
                    if (_hits[j])
                        distance = MathF.Max(distance - 0.002f, 0f);
                    if (MathF.Abs(angle) < 0.000001f)
                        _centerLength = distance;
                    var point = _origin + direction * distance;
                    _vertices[j + 1] = new DrawVertexUV2D(point,
                        new Vector2(distance * MathF.Cos(angle) / entry.Length, distance * MathF.Sin(angle) / width));
                }
                if (debug)
                {
                    handle.UseShader(_debugShader);
                    handle.DrawCircle(_origin, 0.07f, Color.Yellow);
                    handle.DrawLine(_origin, _origin + new Vector2(MathF.Cos(_direction), MathF.Sin(_direction)) * entry.Length, Color.Cyan);
                    for (var j = 1; j <= _angleCount; j++)
                    {
                        if (_hits[j - 1])
                            handle.DrawCircle(_vertices[j].Position, 0.025f, Color.Red);
                        handle.DrawLine(j == 1 ? _origin : _vertices[j - 1].Position, _vertices[j].Position, Color.Lime);
                    }
                    handle.DrawLine(_vertices[_angleCount].Position, _origin, Color.Lime);
                    continue;
                }
                var shader = _shaders[i];
                SetParameter(i, 0, "intensity", entry.Intensity * SmokeFactor(entry));
                SetParameter(i, 1, "cone_tangent", MathF.Tan(_halfAngle));
                SetParameter(i, 2, "falloff", Safe(shaft.Falloff, 0.25f, 6f, 1.8f));
                SetParameter(i, 3, "softness", Safe(shaft.Softness, 0.02f, 1f, 0.5f));
                SetParameter(i, 4, "noise_amount", Safe(shaft.NoiseAmount, 0f, 0.1f, 0f));
                SetParameter(i, 5, "noise_speed", Safe(shaft.NoiseSpeed, 0f, 2f, 0f));
                SetParameter(i, 6, "levels", Math.Clamp(shaft.Quantization, 0, 128));
                handle.UseShader(shader);
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White,
                    _vertices.AsSpan(0, _angleCount + 1), entry.Color);
            }
        }
        finally
        {
            handle.UseShader(null);
            handle.SetTransform(Matrix3x2.Identity);
        }
    }

    private void CollectCorners(in SceneBlocker entry)
    {
        if (_occluderCount == _blockers.Length)
        {
            _overflow = true;
            return;
        }
        // Как в OccluderSystem: bbox осевой в пространстве родительской grid, не вращается с сущностью.
        var box = entry.Bounds;
        _blockers[_occluderCount++] = new Blocker(box, entry.Inverse, Vector2.Transform(_origin, entry.Inverse));
        AddCorner(Vector2.Transform(box.BottomLeft, entry.Matrix));
        AddCorner(Vector2.Transform(box.BottomRight, entry.Matrix));
        AddCorner(Vector2.Transform(box.TopLeft, entry.Matrix));
        AddCorner(Vector2.Transform(box.TopRight, entry.Matrix));
    }

    private void AddCorner(Vector2 point)
    {
        var delta = point - _origin;
        var angle = MathF.Atan2(delta.Y, delta.X) - _direction;
        angle = MathF.Atan2(MathF.Sin(angle), MathF.Cos(angle));
        if (MathF.Abs(angle) > _halfAngle + CornerEpsilon)
            return;
        AddAngle(angle - CornerEpsilon);
        AddAngle(angle);
        AddAngle(angle + CornerEpsilon);
    }

    private void AddAngle(float angle)
    {
        if (_angleCount == MaxAngles)
        {
            _overflow = true;
            return;
        }
        _angles[_angleCount++] = Math.Clamp(angle, -_halfAngle, _halfAngle);
    }

    private float SmokeFactor(in Entry entry)
    {
        var shaft = entry.Component;
        if (!shaft.SmokeReactive)
            return 1f;
        var density = Safe(shaft.FogDensity, 0f, 1f, 0f);
        var direction = new Vector2(MathF.Cos(entry.Direction), MathF.Sin(entry.Direction));
        var samples = 0;
        for (var j = 1; j <= 3; j++)
        {
            var point = entry.Origin + direction * (_centerLength * j / 4f);
            for (var k = 0; k < _smokeCount; k++)
            {
                if (Vector2.DistanceSquared(point, _smoke[k]) > 1.44f)
                    continue;
                samples++;
                break;
            }
        }
        density = MathF.Max(density, samples / 3f);
        return float.Lerp(Safe(shaft.CleanAirFactor, 0f, 1f, 0.12f),
            Safe(shaft.SmokeMultiplier, 0f, 4f, 1.5f), density);
    }

    private static float Safe(float value, float min, float max, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private void SetParameter(int slot, int parameter, string name, float value)
    {
        ref var previous = ref _parameters[slot * 8 + parameter];
        if (previous == value)
            return;
        // SetParameter в Clyde упаковывает float в object; не повторяем неизменившиеся значения.
        previous = value;
        _shaders[slot].SetParameter(name, value);
    }

    protected override void DisposeBehavior()
    {
        foreach (var shader in _shaders)
            shader.Dispose();
        base.DisposeBehavior();
    }

    private readonly record struct Entry(LightShaftComponent Component, Vector2 Origin,
        float Direction, float Length, float Intensity, Color Color, float Distance);

    private readonly record struct Blocker(Box2 Bounds, Matrix3x2 Inverse, Vector2 Origin);
    private readonly record struct SceneBlocker(Box2 Bounds, Box2 WorldBounds, Matrix3x2 Matrix, Matrix3x2 Inverse);
}
