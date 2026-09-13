using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._Fish.Shaders.Bloom;
using Content.Shared.Examine;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
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
    private static readonly ProtoId<ShaderPrototype> BloomDownsampleShader = "SunriseBloomDownsample";
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    private const int BloomPaddingPixels = 96;
    private const float NearBlurRadius = 3f;
    private const float MediumBlurRadius = 10f;
    private const float WideBlurRadius = 26f;
    // FIsh edit - широкий ореол предметов и более сдержанный bloom ламп.
    private const float PointLightStrength = 0.65f;
    private const float AutoEmissiveStrength = 1.3f;
    private const float AutoEmissiveRadius = 1.5f;
    private const float MaxBloomRadius = 2f;
    private const float MaxLightCoreStrength = 2f;
    private const float MinLightHaloRadius = 0.5f;
    private const float MaxLightHaloRadius = 2f;

    private readonly BloomOverlayTreeSystem _bloomTree;
    private readonly IClyde _clyde;
    private readonly ExamineSystemShared _examine;
    private readonly EntityQuery<FishEmissiveBloomComponent> _emissiveQuery;
    private readonly EntityQuery<BloomOverlayVisualsComponent> _bloomVisualsQuery;
    private readonly Dictionary<BloomMaskKey, BloomMaskData> _maskCache = [];
    private readonly EntityQuery<PointLightComponent> _pointLightQuery;
    private readonly IPrototypeManager _prototype;
    private readonly OverlayResourceCache<BloomResources> _resources = new();
    private ShaderInstance? _compositeShader;
    private ShaderInstance? _downsampleShader;
    private readonly SpriteSystem _sprite;
    private readonly SpriteTreeSystem _spriteTree;
    private readonly TransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;
    public override bool RequestScreenTexture => true;

    private readonly List<EmissiveBloomEntry> _visibleEmissives = [];
    private readonly List<BloomLightEntry> _visibleLights = [];
    public float BloomStrength;

    public PointLightingOverlay(
        BloomOverlayTreeSystem bloomTree,
        IClyde clyde,
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
        _clyde = clyde;
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
        var visibleArea = args.WorldAABB.Enlarged(4f);
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
        if (ScreenTexture == null || args.Viewport.Eye is not { } eye)
            return;

        var viewport = args.Viewport;
        var resources = _resources.GetForViewport(viewport, static _ => new BloomResources());
        var paddedSize = viewport.Size + new Vector2i(BloomPaddingPixels * 2, BloomPaddingPixels * 2);
        EnsureTargets(resources, paddedSize);

        RenderBloomMask(args.WorldHandle, viewport, eye, resources.Mask!);
        Downsample(args.RenderHandle.DrawingHandleScreen, args.WorldHandle, resources.Mask!, resources.Near!);
        Downsample(args.RenderHandle.DrawingHandleScreen, args.WorldHandle, resources.Near!, resources.Medium!);
        Downsample(args.RenderHandle.DrawingHandleScreen, args.WorldHandle, resources.Medium!, resources.Wide!);

        _clyde.BlurRenderTarget(viewport, resources.Near!, resources.NearBuffer!, eye, NearBlurRadius);
        _clyde.BlurRenderTarget(viewport, resources.Medium!, resources.MediumBuffer!, eye, MediumBlurRadius);
        _clyde.BlurRenderTarget(viewport, resources.Wide!, resources.WideBuffer!, eye, WideBlurRadius);

        _compositeShader ??= _prototype.Index(BloomShader).InstanceUnique();
        _compositeShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _compositeShader.SetParameter("near_texture", resources.Near!.Texture);
        _compositeShader.SetParameter("medium_texture", resources.Medium!.Texture);
        _compositeShader.SetParameter("wide_texture", resources.Wide!.Texture);
        _compositeShader.SetParameter("bloom_strength", BloomStrength);

        var paddingWorld = BloomPaddingPixels * eye.Zoom.X /
            (EyeManager.PixelsPerMeter * viewport.RenderScale.X);
        var bloomBounds = args.WorldBounds.Enlarged(paddingWorld);
        args.WorldHandle.SetTransform(Matrix3x2.Identity);
        args.WorldHandle.UseShader(_compositeShader);
        args.WorldHandle.DrawTextureRect(resources.Mask!.Texture, bloomBounds);
        args.WorldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        _compositeShader?.Dispose();
        _downsampleShader?.Dispose();
        base.DisposeBehavior();
    }

    private void RenderBloomMask(
        DrawingHandleWorld handle,
        IClydeViewport viewport,
        IEye eye,
        IRenderTexture target)
    {
        handle.RenderInRenderTarget(target, () =>
        {
            var worldToTarget = target.GetWorldToLocalMatrix(eye, viewport.RenderScale);
            handle.UseShader(_prototype.Index(UnshadedShader).Instance());

            foreach (var light in _visibleLights)
            {
                handle.SetTransform(Matrix3x2.Multiply(light.WorldMatrix, worldToTarget));
                var size = light.MaskTexture.Size / (float) EyeManager.PixelsPerMeter;
                var center = light.MaskOffset + size / 2f;
                var softness = Math.Clamp(light.Softness, 0f, 1f);
                var haloRadius = Math.Clamp(light.HaloRadius, MinLightHaloRadius, MaxLightHaloRadius);

                // FIsh edit - отдельный слабый halo делает профиль лампы мягким, не раздувая белое ядро.
                var haloSize = size * haloRadius;
                var haloQuad = Box2.FromDimensions(center - haloSize / 2f, haloSize);
                var haloStrength = PointLightStrength * (0.16f + softness * 0.24f) /
                    MathF.Sqrt(haloRadius);
                handle.DrawTextureRect(light.MaskTexture, haloQuad, ScaleColor(light.Color, haloStrength));

                var coreStrength = Math.Clamp(light.CoreStrength, 0f, MaxLightCoreStrength);
                if (coreStrength <= 0f)
                    continue;

                var coreScale = 1f - softness * 0.18f;
                var coreSize = size * coreScale;
                var coreQuad = Box2.FromDimensions(center - coreSize / 2f, coreSize);
                handle.DrawTextureRect(
                    light.MaskTexture,
                    coreQuad,
                    ScaleColor(light.Color, PointLightStrength * coreStrength));
            }

            foreach (var emissive in _visibleEmissives)
            {
                var radius = Math.Clamp(emissive.Radius, 0f, MaxBloomRadius);
                var strength = Math.Clamp(emissive.Strength, 0f, 2f);
                if (radius <= 0f || strength <= 0f)
                    continue;

                // FIsh edit - radius регулирует вклад предмета в дальний общий halo.
                var maskStrength = strength * (0.75f + 0.25f * radius / MaxBloomRadius);
                DrawEmissiveLayers(handle, emissive, eye.Rotation, maskStrength, worldToTarget);
            }
        }, Color.Black.WithAlpha(0f));
    }

    private void Downsample(
        DrawingHandleScreen screenHandle,
        DrawingHandleWorld worldHandle,
        IRenderTexture source,
        IRenderTexture target)
    {
        _downsampleShader ??= _prototype.Index(BloomDownsampleShader).InstanceUnique();
        worldHandle.RenderInRenderTarget(target, () =>
        {
            screenHandle.SetTransform(Matrix3x2.Identity);
            screenHandle.UseShader(_downsampleShader);
            screenHandle.DrawTextureRect(source.Texture, UIBox2.FromDimensions(Vector2.Zero, (Vector2) target.Size));
            screenHandle.UseShader(null);
        }, Color.Black.WithAlpha(0f));
    }

    private void EnsureTargets(BloomResources resources, Vector2i size)
    {
        EnsureTarget(ref resources.Mask, size, "fish-bloom-mask");
        EnsureBlurLevel(ref resources.Near, ref resources.NearBuffer, size / 2, "near");
        EnsureBlurLevel(ref resources.Medium, ref resources.MediumBuffer, size / 4, "medium");
        EnsureBlurLevel(ref resources.Wide, ref resources.WideBuffer, size / 8, "wide");
    }

    private void EnsureBlurLevel(
        ref IRenderTexture? target,
        ref IRenderTexture? buffer,
        Vector2i size,
        string name)
    {
        EnsureTarget(ref target, size, $"fish-bloom-{name}");
        EnsureTarget(ref buffer, size, $"fish-bloom-{name}-buffer");
    }

    private void EnsureTarget(ref IRenderTexture? target, Vector2i size, string name)
    {
        size = Vector2i.ComponentMax(size, Vector2i.One);
        if (target?.Size == size)
            return;

        target?.Dispose();
        target = _clyde.CreateRenderTarget(
            size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            name: name);
    }

    private void DrawEmissiveLayers(
        DrawingHandleWorld handle,
        in EmissiveBloomEntry emissive,
        Angle eyeRotation,
        float strength,
        Matrix3x2 worldToTarget)
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
                    worldToTarget);
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
                worldToTarget);
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
        Matrix3x2 worldToTarget)
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
            worldToTarget);
    }

    private void DrawEmissiveLayer(
        DrawingHandleWorld handle,
        SpriteComponent.Layer layer,
        ref Matrix3x2 spriteMatrix,
        Angle angle,
        Direction? overrideDirection,
        Color color,
        float strength,
        Matrix3x2 worldToTarget)
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

        var worldMatrix = Matrix3x2.Multiply(layerMatrix, spriteMatrix);
        handle.SetTransform(Matrix3x2.Multiply(worldMatrix, worldToTarget));

        var textureSize = texture.Size / (float) EyeManager.PixelsPerMeter;
        var quad = Box2.FromDimensions(textureSize / -2f, textureSize);
        handle.DrawTextureRect(texture, quad, ScaleColor(color, strength));
    }

    private static Color ScaleColor(Color color, float scale)
    {
        return new Color(
            color.R * scale,
            color.G * scale,
            color.B * scale,
            color.A);
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
            pointLight.Color * bloomVisuals.BloomColor,
            bloomVisuals.CoreStrength,
            bloomVisuals.HaloRadius,
            bloomVisuals.Softness));

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

    private readonly record struct BloomLightEntry(
        Matrix3x2 WorldMatrix,
        Texture MaskTexture,
        Vector2 MaskOffset,
        Color Color,
        float CoreStrength,
        float HaloRadius,
        float Softness);

    private readonly record struct EmissiveBloomEntry(
        EntityUid Uid,
        SpriteComponent Sprite,
        FishEmissiveBloomComponent? Component,
        float Strength,
        float Radius,
        Color Color,
        Vector2 WorldPosition,
        Angle WorldRotation);

    private sealed class BloomResources : IDisposable
    {
        public IRenderTexture? Mask;
        public IRenderTexture? Near;
        public IRenderTexture? NearBuffer;
        public IRenderTexture? Medium;
        public IRenderTexture? MediumBuffer;
        public IRenderTexture? Wide;
        public IRenderTexture? WideBuffer;

        public void Dispose()
        {
            Mask?.Dispose();
            Near?.Dispose();
            NearBuffer?.Dispose();
            Medium?.Dispose();
            MediumBuffer?.Dispose();
            Wide?.Dispose();
            WideBuffer?.Dispose();
        }
    }
}
