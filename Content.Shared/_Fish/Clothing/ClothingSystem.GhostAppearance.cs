using Content.Shared._Fish.Ghost;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared.Clothing.EntitySystems;

public abstract partial class ClothingSystem
{
    [Dependency] private ISerializationManager _ghostSerialization = default!;

    /// <summary>
    /// Captures the current equipped visuals without moving or copying the actual item.
    /// </summary>
    public GhostClothingAppearance CaptureGhostAppearance(Entity<ClothingComponent> ent, SlotDefinition slot,
        string? species, string? bodyType)
    {
        List<PrototypeLayerData>? layers = null;
        if (bodyType != null)
            ent.Comp.ClothingVisuals.TryGetValue($"{slot.Name}-{bodyType}", out layers);
        if (layers == null && species != null)
            ent.Comp.ClothingVisuals.TryGetValue($"{slot.Name}-{species}", out layers);
        if (layers == null)
            ent.Comp.ClothingVisuals.TryGetValue(slot.Name, out layers);

        var prototype = MetaData(ent).EntityPrototype?.ID;
        if (TryComp<ChameleonClothingComponent>(ent, out var chameleon) && chameleon.Default is { } disguise)
            prototype = disguise.Id;

        return new GhostClothingAppearance(slot.Name, prototype,
            ent.Comp.RsiPath, ent.Comp.EquippedPrefix, ent.Comp.EquippedState,
            ent.Comp.Scale, slot.Offset, species, bodyType,
            layers == null ? null : _ghostSerialization.CreateCopy(layers, notNullableOverride: true));
    }
}
