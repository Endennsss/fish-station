using System.Numerics;
using Content.Shared._Fish.Shaders.Bloom;
using Content.Shared.Examine;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Sunrise.Shaders.Bloom;

/// <summary>
/// Рисует bloom совместимых точечных источников и декоративных emissive-слоёв спрайтов.
/// </summary>
public sealed class PointLightingOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> BloomShader = "SunriseLightingOverlay";

    // FIsh edit - предметы светятся выразительнее, а лампы дают более сдержанный bloom.
    private const float PointLightStrength = 0.85f;
    private const float PointLightRadius = 0.9f;
    private const float PointLightHaloStrength = 0.9f;
    private const float EmissiveHaloStrength = 0.55f;
    private const float AutoEmissiveStrength = 1.3f;
    private const float AutoEmissiveRadius = 0.8f;
    private const float MaxBloomRadius = 2f;
    private const float WideSampleRadius = 5.25f;

    private readonly BloomOverlayTreeSystem _bloomTree;
    private readonly ExamineSystemShared _examine;
    private readonly EntityQuery<FishEmissiveBloomComponent> _emissiveQuery;
    private readonly EntityQuery<BloomOverlayVisualsComponent> _bloomVisualsQuery;
    private readonly Dictionary<BloomMaskKey, BloomMaskData> _maskCache = [];
    private readonly EntityQuery<PointLightComponent> _pointLightQuery;
    private readonly IPrototypeManager _prototype;
    private readonly Dictionary<BloomShaderKey, ShaderInstance> _shaderCache = [];
    private readonly SpriteSystem _sprite;
    private readonly SpriteTreeSystem _spriteTree;
    private readonly TransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;
    public override bool RequestScreenTexture => true;

    private readonly List<EmissiveBloomEntry> _visibleEmissives = [];
    private readonly List<BloomLightEntry> _visibleLights = [];
    private float _cachedBloomStrength = float.NaN;
    public float BloomStrength;

    public PointLightingOverlay(
        BloomOverlayTreeSystem bloomTree,
        ExamineSystemShared examine,
        SpriteTreeSystem spriteTree,
        IPrototypeManager prototypeManager,
        SpriteSystem spriteSystem,
        TransformSystem transform,
        EntityQuery<FishEmissiveBloomComponent> emissiveQuery,
        EntityQuery<BloomOverlayVisualsComponent> bloomVisualsQuery,
        EntityQuery<PointLightComponent> pointLightQuery,
        int zIndex,
        float strength)
    {
        _bloomTree = bloomTree;
        _examine = examine;
        _spriteTree = spriteTree;
        _prototype = prototypeManager;
        _sprite = spriteSystem;
        _transform = transform;
        _emissiveQuery = emissiveQuery;
        _bloomVisualsQuery = bloomVisualsQuery;
        _pointLightQuery = pointLightQuery;
        BloomStrength = strength;
        ZIndex = zIndex;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (BloomStrength <= 0f || args.Viewport.Eye is not { } eye)
            return false;

        _visibleEmissives.Clear();
        _visibleLights.Clear();
        var visibleArea = args.WorldAABB.Enlarged(1f);
        var lightQueryState = new BloomLightQueryState(
            _visibleLights,
            _maskCache,
            _pointLightQuery,
            _sprite,
            _transform,
            _examine,
            eye.Position,
            eye.DrawFov);
        _bloomTree.QueryAabb(ref lightQueryState, CollectBloomLight, args.MapId, visibleArea);

        var emissiveQueryState = new EmissiveBloomQueryState(
            _visibleEmissives,
            _emissiveQuery,
            _bloomVisualsQuery,
            _sprite,
            _transform,
            _examine,
            eye.Position,
            eye.DrawFov);
        _spriteTree.QueryAabb(ref emissiveQueryState, CollectEmissiveBloom, args.MapId, visibleArea);

        return _visibleLights.Count > 0 || _visibleEmissives.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        if (_cachedBloomStrength != BloomStrength)
        {
            ClearShaderCache();
            _cachedBloomStrength = BloomStrength;
        }

        foreach (var shader in _shaderCache.Values)
            shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        foreach (var light in _visibleLights)
        {
            handle.SetTransform(light.WorldMatrix);
            var size = light.MaskTexture.Size / (float) EyeManager.PixelsPerMeter;
            var quad = Box2.FromDimensions(light.MaskOffset, size);
            DrawBloomTexture(
                handle,
                light.MaskTexture,
                quad,
                light.Color,
                BloomStrength * PointLightStrength,
                PointLightRadius,
                PointLightHaloStrength,
                true);
        }

        if (args.Viewport.Eye is { } eye)
        {
            foreach (var emissive in _visibleEmissives)
            {
                var radius = Math.Clamp(emissive.Radius, 0f, MaxBloomRadius);
                var strength = BloomStrength * Math.Clamp(emissive.Strength, 0f, 2f);
                if (radius <= 0f || strength <= 0f)
                    continue;

                DrawEmissiveLayers(handle, emissive, eye.Rotation, strength, radius);
            }
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    protected override void DisposeBehavior()
    {
        ClearShaderCache();
        base.DisposeBehavior();
    }

    private ShaderInstance GetBloomShader(BloomShaderKey key)
    {
        if (_shaderCache.TryGetValue(key, out var shader))
            return shader;

        shader = _prototype.Index(BloomShader).InstanceUnique();
        shader.SetParameter("SCREEN_TEXTURE", ScreenTexture!);
        shader.SetParameter("bloom_strength", key.Strength);
        shader.SetParameter("bloom_radius", key.Radius);
        shader.SetParameter("halo_strength", key.HaloStrength);
        shader.SetParameter("respect_lighting", key.RespectLighting ? 1f : 0f);
        shader.SetParameter("bloom_quad_scale", key.QuadScale);
        shader.SetParameter("bloom_texture_size", key.TextureSize);
        _shaderCache.Add(key, shader);
        return shader;
    }

    private void ClearShaderCache()
    {
        foreach (var shader in _shaderCache.Values)
            shader.Dispose();

        _shaderCache.Clear();
    }

    private void DrawBloomTexture(
        DrawingHandleWorld handle,
        Texture texture,
        Box2 quad,
        Color color,
        float strength,
        float radius,
        float haloStrength,
        bool respectLighting)
    {
        var padding = MathF.Ceiling(WideSampleRadius * radius + 1f) / EyeManager.PixelsPerMeter;
        var expandedQuad = quad.Enlarged(padding);
        var shader = GetBloomShader(new BloomShaderKey(
            strength,
            radius,
            haloStrength,
            respectLighting,
            expandedQuad.Size / quad.Size,
            (Vector2) texture.Size));
        handle.UseShader(shader);
        handle.DrawTextureRectRegion(texture, expandedQuad, color);
    }

    private void DrawEmissiveLayers(
        DrawingHandleWorld handle,
        in EmissiveBloomEntry emissive,
        Angle eyeRotation,
        float strength,
        float radius)
    {
        var sprite = emissive.Sprite;
        var overrideDirection = sprite.EnableDirectionOverride
            ? sprite.DirectionOverride
            : (Direction?) null;
        var angle = (emissive.WorldRotation + eyeRotation).Reduced().FlipPositive();
        var cardinal = !sprite.NoRotation && sprite.SnapCardinals
            ? angle.RoundToCardinalAngle()
            : Angle.Zero;

        var spriteMatrix = Matrix3Helpers.CreateTransform(
            emissive.WorldPosition,
            sprite.NoRotation ? -eyeRotation : emissive.WorldRotation - cardinal);
        spriteMatrix = Matrix3x2.Multiply(sprite.LocalMatrix, spriteMatrix);

        var defaultMatrix = Matrix3x2.Multiply(
            sprite.LocalMatrix,
            Matrix3Helpers.CreateTransform(emissive.WorldPosition, emissive.WorldRotation));
        var snapMatrix = Matrix3x2.Multiply(
            sprite.LocalMatrix,
            Matrix3Helpers.CreateTransform(
                emissive.WorldPosition,
                emissive.WorldRotation - angle.RoundToCardinalAngle()));
        var noRotationMatrix = Matrix3x2.Multiply(
            sprite.LocalMatrix,
            Matrix3Helpers.CreateTransform(emissive.WorldPosition, -eyeRotation));

        if (emissive.Component is { } component)
        {
            foreach (var layerKey in component.Layers)
            {
                if (!_sprite.TryGetLayer((emissive.Uid, sprite), layerKey, out var layer, false))
                    continue;

                DrawSelectedEmissiveLayer(
                    handle,
                    emissive,
                    layer,
                    ref spriteMatrix,
                    ref defaultMatrix,
                    ref snapMatrix,
                    ref noRotationMatrix,
                    angle,
                    overrideDirection,
                    strength,
                    radius);
            }

            return;
        }

        foreach (var spriteLayer in sprite.AllLayers)
        {
            if (spriteLayer is not SpriteComponent.Layer layer)
                continue;

            if (layer.ShaderPrototype != SpriteSystem.UnshadedId)
                continue;

            DrawSelectedEmissiveLayer(
                handle,
                emissive,
                layer,
                ref spriteMatrix,
                ref defaultMatrix,
                ref snapMatrix,
                ref noRotationMatrix,
                angle,
                overrideDirection,
                strength,
                radius);
        }
    }

    private void DrawSelectedEmissiveLayer(
        DrawingHandleWorld handle,
        in EmissiveBloomEntry emissive,
        SpriteComponent.Layer layer,
        ref Matrix3x2 spriteMatrix,
        ref Matrix3x2 defaultMatrix,
        ref Matrix3x2 snapMatrix,
        ref Matrix3x2 noRotationMatrix,
        Angle angle,
        Direction? overrideDirection,
        float strength,
        float radius)
    {
        if (!layer.Visible || layer.Blank || layer.CopyToShaderParameters != null)
            return;

        var matrix = !emissive.Sprite.GranularLayersRendering
            ? spriteMatrix
            : layer.RenderingStrategy switch
            {
                LayerRenderingStrategy.Default => defaultMatrix,
                LayerRenderingStrategy.NoRotation => noRotationMatrix,
                LayerRenderingStrategy.SnapToCardinals => snapMatrix,
                _ => spriteMatrix,
            };

        DrawEmissiveLayer(
            handle,
            layer,
            ref matrix,
            angle,
            overrideDirection,
            emissive.Sprite.Color * layer.Color * emissive.Color,
            strength,
            radius);
    }

    private void DrawEmissiveLayer(
        DrawingHandleWorld handle,
        SpriteComponent.Layer layer,
        ref Matrix3x2 spriteMatrix,
        Angle angle,
        Direction? overrideDirection,
        Color color,
        float strength,
        float radius)
    {
        var state = layer.ActualState;
        var layerMatrixDirection = state == null
            ? RsiDirection.South
            : SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);

        layer.GetLayerDrawMatrix(layerMatrixDirection, out var layerMatrix);

        var textureDirection = layerMatrixDirection;

        if (overrideDirection != null && state != null)
            textureDirection = overrideDirection.Value.Convert(state.RsiDirections);

        textureDirection = textureDirection.OffsetRsiDir(layer.DirOffset);
        var texture = state?.GetFrame(textureDirection, layer.AnimationFrame) ?? layer.Texture;
        if (texture == null || color.A <= 0f)
            return;

        handle.SetTransform(Matrix3x2.Multiply(layerMatrix, spriteMatrix));

        var textureSize = texture.Size / (float) EyeManager.PixelsPerMeter;
        var quad = Box2.FromDimensions(textureSize / -2f, textureSize);
        DrawBloomTexture(
            handle,
            texture,
            quad,
            color,
            strength,
            radius,
            EmissiveHaloStrength,
            false);
    }

    private static bool CollectBloomLight(
        ref BloomLightQueryState queryState,
        in ComponentTreeEntry<BloomOverlayVisualsComponent> bloomEntry)
    {
        var bloomVisuals = bloomEntry.Component;
        if (!queryState.PointLightQuery.TryComp(bloomEntry.Uid, out var pointLight) ||
            !pointLight.Enabled ||
            pointLight.ContainerOccluded)
        {
            return true;
        }

        var transform = bloomEntry.Transform;
        var (_, _, worldMatrix) = queryState.Transform.GetWorldPositionRotationMatrix(transform);

        if (queryState.CheckOcclusion)
        {
            var bloomUid = bloomEntry.Uid;
            var bloomPosition = new MapCoordinates(
                Vector2.Transform(bloomVisuals.MaskOffset, worldMatrix),
                queryState.EyePosition.MapId);
            var distance = (bloomPosition.Position - queryState.EyePosition.Position).Length();
            if (!queryState.Examine.InRangeUnOccluded(
                    queryState.EyePosition,
                    bloomPosition,
                    distance,
                    bloomUid,
                    static (uid, ignoredUid) => uid == ignoredUid))
            {
                return true;
            }
        }

        var maskKey = new BloomMaskKey(bloomVisuals.MaskSprite, bloomVisuals.MaskOffset);
        if (!queryState.MaskCache.TryGetValue(maskKey, out var mask))
        {
            var texture = queryState.Sprite.Frame0(bloomVisuals.MaskSprite);
            mask = new BloomMaskData(
                texture,
                bloomVisuals.MaskOffset - new Vector2(texture.Width, texture.Height) /
                (2f * EyeManager.PixelsPerMeter));
            queryState.MaskCache.Add(maskKey, mask);
        }

        queryState.VisibleLights.Add(new BloomLightEntry(
            worldMatrix,
            mask.Texture,
            mask.Offset,
            pointLight.Color * bloomVisuals.BloomColor));

        return true;
    }

    private static bool CollectEmissiveBloom(
        ref EmissiveBloomQueryState queryState,
        in ComponentTreeEntry<SpriteComponent> spriteEntry)
    {
        if (!spriteEntry.Component.Visible ||
            spriteEntry.Component.ContainerOccluded)
        {
            return true;
        }

        queryState.EmissiveQuery.TryComp(spriteEntry.Uid, out var emissive);
        if (emissive != null && (emissive.Layers.Count == 0 || emissive.Strength <= 0f))
            return true;

        // Совместимые PointLight уже рисуются собственной цветной маской и не должны получать второй bloom.
        if (emissive == null && queryState.BloomVisualsQuery.HasComp(spriteEntry.Uid))
            return true;

        var hasVisibleLayer = false;
        if (emissive != null)
        {
            foreach (var layerKey in emissive.Layers)
            {
                if (!queryState.Sprite.TryGetLayer(
                        (spriteEntry.Uid, spriteEntry.Component),
                        layerKey,
                        out var layer,
                        false) ||
                    !IsDrawableEmissiveLayer(layer))
                {
                    continue;
                }

                hasVisibleLayer = true;
                break;
            }
        }
        else
        {
            foreach (var spriteLayer in spriteEntry.Component.AllLayers)
            {
                if (spriteLayer is not SpriteComponent.Layer layer)
                    continue;

                if (layer.ShaderPrototype != SpriteSystem.UnshadedId || !IsDrawableEmissiveLayer(layer))
                    continue;

                hasVisibleLayer = true;
                break;
            }
        }

        if (!hasVisibleLayer)
            return true;

        var (worldPosition, worldRotation) = queryState.Transform.GetWorldPositionRotation(spriteEntry.Transform);
        if (queryState.CheckOcclusion)
        {
            var bloomPosition = new MapCoordinates(worldPosition, queryState.EyePosition.MapId);
            var distance = (worldPosition - queryState.EyePosition.Position).Length();
            if (!queryState.Examine.InRangeUnOccluded(
                    queryState.EyePosition,
                    bloomPosition,
                    distance,
                    spriteEntry.Uid,
                    static (uid, ignoredUid) => uid == ignoredUid))
            {
                return true;
            }
        }

        queryState.VisibleEmissives.Add(new EmissiveBloomEntry(
            spriteEntry.Uid,
            spriteEntry.Component,
            emissive,
            emissive?.Strength ?? AutoEmissiveStrength,
            emissive?.Radius ?? AutoEmissiveRadius,
            emissive?.Color ?? Color.White,
            worldPosition,
            worldRotation));
        return true;
    }

    private static bool IsDrawableEmissiveLayer(SpriteComponent.Layer layer)
    {
        return layer.Visible && !layer.Blank && layer.CopyToShaderParameters == null;
    }

    private readonly record struct BloomLightQueryState(
        List<BloomLightEntry> VisibleLights,
        Dictionary<BloomMaskKey, BloomMaskData> MaskCache,
        EntityQuery<PointLightComponent> PointLightQuery,
        SpriteSystem Sprite,
        TransformSystem Transform,
        ExamineSystemShared Examine,
        MapCoordinates EyePosition,
        bool CheckOcclusion);

    private readonly record struct EmissiveBloomQueryState(
        List<EmissiveBloomEntry> VisibleEmissives,
        EntityQuery<FishEmissiveBloomComponent> EmissiveQuery,
        EntityQuery<BloomOverlayVisualsComponent> BloomVisualsQuery,
        SpriteSystem Sprite,
        TransformSystem Transform,
        ExamineSystemShared Examine,
        MapCoordinates EyePosition,
        bool CheckOcclusion);

    private readonly record struct BloomMaskKey(SpriteSpecifier Sprite, Vector2 Offset);

    private readonly record struct BloomMaskData(Texture Texture, Vector2 Offset);

    private readonly record struct BloomShaderKey(
        float Strength,
        float Radius,
        float HaloStrength,
        bool RespectLighting,
        Vector2 QuadScale,
        Vector2 TextureSize);

    private readonly record struct BloomLightEntry(
        Matrix3x2 WorldMatrix,
        Texture MaskTexture,
        Vector2 MaskOffset,
        Color Color);

    private readonly record struct EmissiveBloomEntry(
        EntityUid Uid,
        SpriteComponent Sprite,
        FishEmissiveBloomComponent? Component,
        float Strength,
        float Radius,
        Color Color,
        Vector2 WorldPosition,
        Angle WorldRotation);
}
