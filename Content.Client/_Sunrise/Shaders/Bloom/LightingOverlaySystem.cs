using Content.Shared._Fish.Shaders.Bloom;
using Content.Shared._Sunrise.SunriseCCVars;
using Content.Shared.Examine;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Sunrise.Shaders.Bloom;

/// <summary>
/// Собирает совместимые источники свечения и передаёт их состояние в bloom overlay.
/// </summary>
public sealed class LightingOverlaySystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly BloomOverlayTreeSystem _bloomTree = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SpriteTreeSystem _spriteTree = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private EntityQuery<FishEmissiveBloomComponent> _emissiveQuery;
    private EntityQuery<BloomOverlayVisualsComponent> _bloomVisualsQuery;
    private EntityQuery<PointLightComponent> _pointLightQuery;
    private PointLightingOverlay? _bloomOverlay;
    private float _bloomStrength = 0.45f; // FIsh edit - новая нейтральная интенсивность по умолчанию

    public override void Initialize()
    {
        base.Initialize();

        _emissiveQuery = GetEntityQuery<FishEmissiveBloomComponent>();
        _bloomVisualsQuery = GetEntityQuery<BloomOverlayVisualsComponent>();
        _pointLightQuery = GetEntityQuery<PointLightComponent>();
        Subs.CVar(_configuration, SunriseCCVars.LightBloomEnabled, OnBloomEnabledChanged, true);
        Subs.CVar(_configuration, SunriseCCVars.LightBloomStrength, OnBloomStrengthChanged, true);
    }

    public override void Shutdown()
    {
        if (_bloomOverlay is { } overlay)
        {
            if (_overlay.HasOverlay<PointLightingOverlay>())
                _overlay.RemoveOverlay(overlay);

            overlay.Dispose();
            _bloomOverlay = null;
        }

        base.Shutdown();
    }

    private void OnBloomEnabledChanged(bool isEnabled)
    {
        if (!isEnabled)
        {
            if (_bloomOverlay is not { } overlay)
                return;

            if (_overlay.HasOverlay<PointLightingOverlay>())
                _overlay.RemoveOverlay(overlay);

            overlay.Dispose();
            _bloomOverlay = null;
            return;
        }

        if (_overlay.HasOverlay<PointLightingOverlay>())
            return;

        _bloomOverlay ??= new PointLightingOverlay(
            _bloomTree,
            _examine,
            _spriteTree,
            _prototype,
            _sprite,
            _transform,
            _emissiveQuery,
            _bloomVisualsQuery,
            _pointLightQuery,
            (int) DrawDepth.Effects,
            _bloomStrength);

        _overlay.AddOverlay(_bloomOverlay);
    }

    private void OnBloomStrengthChanged(float strength)
    {
        _bloomStrength = Math.Clamp(strength, 0f, 1f);
        if (_bloomOverlay is { } overlay)
            overlay.BloomStrength = _bloomStrength;
    }
}
