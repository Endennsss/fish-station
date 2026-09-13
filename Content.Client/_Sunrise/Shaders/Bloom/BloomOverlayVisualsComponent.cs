using System.Numerics;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;

namespace Content.Client._Sunrise.Shaders.Bloom;

/// <summary>
/// Marks a point light that should receive the client-side bloom effect.
/// </summary>
[RegisterComponent]
public sealed partial class BloomOverlayVisualsComponent : Component, IComponentTreeEntry<BloomOverlayVisualsComponent>
{
    /// <summary>
    /// Определяет, участвует ли источник света в дереве bloom-эффекта.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    [DataField]
    public SpriteSpecifier MaskSprite = new SpriteSpecifier.Rsi(
        new ResPath("_Sunrise/Effects/LightMasks/64.rsi"),
        "light_point");

    [DataField]
    public Vector2 MaskOffset = new(0f, 0.45f);

    [DataField]
    public Color BloomColor = Color.White;

    /// <summary>
    /// Яркость компактного ядра bloom относительно общей настройки эффекта.
    /// </summary>
    [DataField]
    public float CoreStrength = 0.7f;

    /// <summary>
    /// Масштаб локального ореола вокруг маски источника.
    /// </summary>
    [DataField]
    public float HaloRadius = 1.2f;

    /// <summary>
    /// Мягкость ореола от нуля для резкого источника до единицы для рассеянного света.
    /// </summary>
    [DataField]
    public float Softness = 0.75f;

    public EntityUid? TreeUid { get; set; }

    public DynamicTree<ComponentTreeEntry<BloomOverlayVisualsComponent>>? Tree { get; set; }

    public bool AddToTree => Enabled;

    public bool TreeUpdateQueued { get; set; }
}
