using Content.Server.Ghost;
using Content.Shared._Fish.Ghost;
using Content.Shared._Sunrise.Humanoid;
using Content.Shared.Body;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Ghost;
using Content.Shared.Gibbing;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Fish.Ghost;

/// <summary>
/// Captures head markings and clothing before the body is lost and copies them to its ghost.
/// </summary>
public sealed partial class GhostAppearanceSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ClothingSystem _clothing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MindComponent, BeforeMindGotRemovedEvent>(OnMindRemoved);
        SubscribeLocalEvent<GhostComponent, GhostSpawnedEvent>(OnGhostSpawned);
        SubscribeLocalEvent<GhostComponent, MindAddedMessage>(OnGhostMindAdded);
        SubscribeLocalEvent<MindContainerComponent, BeingGibbedEvent>(OnBeingGibbed);
        SubscribeLocalEvent<VisualOrganMarkingsComponent, BodyRelayedEvent<CollectHeadMarkingsEvent>>(OnCollectHeadMarkings);
    }

    private void OnMindRemoved(Entity<MindComponent> ent, ref BeforeMindGotRemovedEvent args)
    {
        StoreAppearance(ent, args.Container);
    }

    private void OnGhostSpawned(Entity<GhostComponent> ent, ref GhostSpawnedEvent args)
    {
        if (args.Mind.Comp.OwnedEntity is { } body)
            StoreAppearance(args.Mind, body);

        CopyAppearance(ent, args.Mind);
    }

    private void OnGhostMindAdded(Entity<GhostComponent> ent, ref MindAddedMessage args)
    {
        // SpawnGhost уже сохранил внешность до переноса разума, включая выход с возможностью возвращения.
        if (!HasComp<GhostAppearanceComponent>(ent))
            CopyAppearance(ent, args.Mind);
    }

    private void OnBeingGibbed(Entity<MindContainerComponent> ent, ref BeingGibbedEvent args)
    {
        if (ent.Comp.Mind is not { } mind)
            return;

        // Порядок выпадения мозга и одежды не должен менять снимок внешности.
        StoreAppearance(mind, ent);
        if (TryComp<StoredGhostAppearanceComponent>(mind, out var stored))
            stored.GibbedBody = ent;
    }

    private void OnCollectHeadMarkings(Entity<VisualOrganMarkingsComponent> ent, ref BodyRelayedEvent<CollectHeadMarkingsEvent> args)
    {
        var expanded = true;
        while (expanded)
        {
            expanded = false;
            foreach (var (layer, dependents) in ent.Comp.DependentHidingLayers)
            {
                if (layer is not HumanoidVisualLayers source || !args.Args.HiddenLayers.Contains(source))
                    continue;

                foreach (var dependent in dependents)
                {
                    if (dependent is HumanoidVisualLayers hiddenLayer)
                        expanded |= args.Args.HiddenLayers.Add(hiddenLayer);
                }
            }
        }

        foreach (var markings in ent.Comp.Markings.Values)
        {
            foreach (var marking in markings)
            {
                if (!_prototype.TryIndex(marking.MarkingId, out var prototype) ||
                    prototype.BodyPart is not (
                        HumanoidVisualLayers.Head or HumanoidVisualLayers.Eyes or
                        HumanoidVisualLayers.Hair or HumanoidVisualLayers.FacialHair or
                        HumanoidVisualLayers.Snout or HumanoidVisualLayers.SnoutCover or
                        HumanoidVisualLayers.HeadSide or HumanoidVisualLayers.HeadTop or
                        HumanoidVisualLayers.Special or HumanoidVisualLayers.Dreadlocks or HumanoidVisualLayers.Rings))
                    continue;

                // Копируем цвета, не привязывая призрака к изменяемым эффектам живого тела.
                args.Args.Markings.Add(new Marking(marking.MarkingId, marking.MarkingColors));
            }
        }
    }

    private void StoreAppearance(EntityUid mind, EntityUid source)
    {
        // У мозга после гиба нет тела. Сохранённая внешность должна пережить такой перенос разума.
        if (HasComp<GhostComponent>(source) || !TryComp<BodyComponent>(source, out var body))
            return;

        if (TryComp<StoredGhostAppearanceComponent>(mind, out var previous) && previous.GibbedBody == source)
            return;

        var hiddenLayers = TryComp<HideableHumanoidLayersComponent>(source, out var hidden)
            ? new HashSet<HumanoidVisualLayers>(hidden.HiddenLayers.Keys)
            : [];
        var collect = new CollectHeadMarkingsEvent([], hiddenLayers);
        _body.RelayEvent((source, body), ref collect);
        var stored = EnsureComp<StoredGhostAppearanceComponent>(mind);
        stored.Markings = collect.Markings;
        stored.Clothing = [];
        stored.HiddenLayers = hiddenLayers;
        stored.GibbedBody = null;

        if (!TryComp<InventoryComponent>(source, out var inventory))
            return;

        var bodyType = TryComp<SunriseHumanoidProfileComponent>(source, out var profile) &&
                       _prototype.TryIndex(profile.BodyType, out var bodyTypePrototype)
            ? bodyTypePrototype.VisualKey
            : null;
        var slots = _inventory.GetSlotEnumerator((source, inventory));
        while (slots.NextItem(out var item, out var slot))
        {
            if ((slot.SlotFlags & (SlotFlags.FEET | SlotFlags.LEGS | SlotFlags.PANTS | SlotFlags.SOCKS | SlotFlags.POCKET)) != 0)
                continue;

            if (!TryComp<ClothingComponent>(item, out var clothing) || (clothing.Slots & slot.SlotFlags) == 0)
                continue;

            stored.Clothing.Add(_clothing.CaptureGhostAppearance((item, clothing), slot, inventory.SpeciesId, bodyType));
        }
    }

    private void CopyAppearance(EntityUid ghost, EntityUid mind)
    {
        if (!TryComp<StoredGhostAppearanceComponent>(mind, out var stored))
            return;

        var appearance = EnsureComp<GhostAppearanceComponent>(ghost);
        appearance.Markings = new List<Marking>(stored.Markings);
        appearance.Clothing = new List<GhostClothingAppearance>(stored.Clothing);
        appearance.HiddenLayers = new HashSet<HumanoidVisualLayers>(stored.HiddenLayers);
        Dirty(ghost, appearance);
    }

    [ByRefEvent]
    private readonly record struct CollectHeadMarkingsEvent(List<Marking> Markings, HashSet<HumanoidVisualLayers> HiddenLayers);
}
