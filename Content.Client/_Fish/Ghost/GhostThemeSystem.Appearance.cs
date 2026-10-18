using System.Numerics;
using Content.Client.Clothing;
using Content.Shared._Fish.Ghost;
using Content.Shared._Sunrise.GhostTheme;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;

namespace Content.Client._Sunrise.GhostTheme;

/// <summary>
/// Displays a ghost's saved head markings and clothing only for themes that opt in.
/// </summary>
public sealed partial class GhostThemeSystem
{
    [Dependency] private ClientClothingSystem _clothing = default!;

    // Порядок совпадает с опорными слоями головы в BaseSpeciesLayers.
    private static readonly HumanoidVisualLayers[] HeadLayers =
    [
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.Snout,
        HumanoidVisualLayers.Eyes,
        HumanoidVisualLayers.SnoutCover,
        HumanoidVisualLayers.FacialHair,
        HumanoidVisualLayers.Hair,
        HumanoidVisualLayers.Dreadlocks,
        HumanoidVisualLayers.Rings,
        HumanoidVisualLayers.HeadSide,
        HumanoidVisualLayers.HeadTop,
        HumanoidVisualLayers.Special,
    ];

    private static readonly string[] ClothingSlots =
    [
        "bra", "jumpsuit", "gloves", "ears", "eyes",
        "belt", "id", "outerClothing", "back", "neck", "suitstorage",
    ];

    private static readonly string[] HeadClothingSlots = ["mask", "head"];

    private void InitializeGhostAppearance()
    {
        SubscribeLocalEvent<GhostAppearanceComponent, AfterAutoHandleStateEvent>(OnAppearanceState);
        SubscribeLocalEvent<GhostAppearanceComponent, ComponentShutdown>(OnAppearanceShutdown);
    }

    private void OnAppearanceState(Entity<GhostAppearanceComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshAppearance(ent);
    }

    private void OnAppearanceShutdown(Entity<GhostAppearanceComponent> ent, ref ComponentShutdown args)
    {
        ClearLayers(ent);
    }

    private void RefreshSavedAppearance(EntityUid ghost)
    {
        if (TryComp<GhostAppearanceComponent>(ghost, out var appearance))
            RefreshAppearance((ghost, appearance));
    }

    private void ClearSavedAppearance(EntityUid ghost)
    {
        if (TryComp<GhostAppearanceComponent>(ghost, out var appearance))
            ClearLayers((ghost, appearance));
    }

    private void RefreshAppearance(Entity<GhostAppearanceComponent> ent)
    {
        ClearLayers(ent);
        if (!TryComp<SpriteComponent>(ent, out var sprite) ||
            !TryComp<GhostThemeComponent>(ent, out var theme) ||
            theme.GhostTheme == null ||
            !_proto.TryIndex<GhostThemePrototype>(theme.GhostTheme, out var prototype) ||
            prototype.UseUpstreamSprite || (!prototype.ShowHeadAppearance && !prototype.ShowClothing))
            return;

        Entity<SpriteComponent?> spriteEnt = (ent, sprite);
        foreach (var headLayer in HeadLayers)
        {
            if (headLayer == HumanoidVisualLayers.SnoutCover && prototype.ShowClothing)
                AddClothingLayers(ent, spriteEnt, prototype, ClothingSlots);

            var visibleLayer = headLayer == HumanoidVisualLayers.Special ? HumanoidVisualLayers.HeadTop : headLayer;
            if (!prototype.ShowHeadAppearance || (prototype.ShowClothing && ent.Comp.HiddenLayers.Contains(visibleLayer)))
                continue;

            foreach (var marking in ent.Comp.Markings)
            {
                if (!_proto.TryIndex(marking.MarkingId, out var markingPrototype) || markingPrototype.BodyPart != headLayer)
                    continue;

                for (var i = 0; i < markingPrototype.Sprites.Count; i++)
                {
                    var layer = _sprite.AddLayer(spriteEnt, markingPrototype.Sprites[i]);
                    var color = i < marking.MarkingColors.Count ? marking.MarkingColors[i] : Color.White;
                    FinishLayer(ent, spriteEnt, layer, color, prototype);
                }
            }
        }

        if (prototype.ShowClothing)
            AddClothingLayers(ent, spriteEnt, prototype, HeadClothingSlots);
    }

    private void AddClothingLayers(Entity<GhostAppearanceComponent> ent, Entity<SpriteComponent?> sprite,
        GhostThemePrototype theme, string[] slots)
    {
        foreach (var slot in slots)
        {
            foreach (var clothing in ent.Comp.Clothing)
            {
                if (clothing.Slot != slot)
                    continue;

                foreach (var data in _clothing.GetGhostAppearanceLayers(clothing))
                {
                    var layer = _sprite.AddLayer(sprite, data, null);
                    var shader = slot is "head" or "mask" or "ears" or "eyes" or "gloves"
                        ? "unshaded"
                        : "FishGhostUpperClothing";
                    FinishLayer(ent, sprite, layer, data.Color ?? Color.White, theme, data.Scale, shader);
                }
            }
        }
    }

    private void FinishLayer(Entity<GhostAppearanceComponent> ent, Entity<SpriteComponent?> sprite,
        int layer, Color color, GhostThemePrototype theme, Vector2? scale = null, string shader = "unshaded")
    {
        var key = $"FishGhostAppearance-{ent.Comp.AppliedLayers.Count}";
        _sprite.LayerMapSet(sprite, key, layer);
        _sprite.LayerSetData(sprite, layer, new PrototypeLayerData
        {
            Color = color * theme.SpriteColor,
            Scale = (scale ?? Vector2.One) * theme.Scale,
            Shader = shader,
        });
        ent.Comp.AppliedLayers.Add(key);
    }

    private void ClearLayers(Entity<GhostAppearanceComponent> ent)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            foreach (var key in ent.Comp.AppliedLayers)
                _sprite.RemoveLayer((ent, sprite), key, false);
        }

        ent.Comp.AppliedLayers.Clear();
    }
}
