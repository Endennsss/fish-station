using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Fish.Storage;

/// <summary>
/// Filters magnet pickup and manages the predicted settings of ore bags.
/// </summary>
public sealed partial class FishOreBagFilterSystem : EntitySystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;

    private static readonly ProtoId<TagPrototype> OreTag = "Ore";
    private static readonly ProtoId<TagPrototype> FragmentTag = "ArtifactFragment";

    private readonly Dictionary<ProtoId<StackPrototype>, StackPrototype> _stackTypes = new();
    private bool _stackTypesLoaded;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FishOreBagFilterComponent, MagnetPickupAttemptEvent>(OnMagnetPickupAttempt);
        SubscribeLocalEvent<FishOreBagFilterComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<FishOreBagFilterComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
        SubscribeLocalEvent<BoundUserInterfaceMessageAttempt>(OnUiMessageAttempt);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        Subs.BuiEvents<FishOreBagFilterComponent>(FishOreBagFilterUiKey.Key, subs =>
        {
            subs.Event<FishOreBagFilterSetStackMessage>(OnSetStack);
            subs.Event<FishOreBagFilterSetOtherMessage>(OnSetOther);
            subs.Event<FishOreBagFilterSetAllMessage>(OnSetAll);
        });
    }

    private void OnMagnetPickupAttempt(Entity<FishOreBagFilterComponent> ent, ref MagnetPickupAttemptEvent args)
    {
        if (!args.Cancelled && !CanCollect(ent, args.Item))
            args.Cancelled = true;
    }

    private void OnGetVerbs(Entity<FishOreBagFilterComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!ent.Comp.Enabled || !args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("fish-ore-bag-filter-verb"),
            Act = () => _ui.TryToggleUi(ent.Owner, FishOreBagFilterUiKey.Key, user),
        });
    }

    private void OnAfterAutoHandleState(Entity<FishOreBagFilterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateUi(ent);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<StackPrototype>() && !args.WasModified<EntityPrototype>())
            return;

        _stackTypes.Clear();
        _stackTypesLoaded = false;
    }

    private void OnUiMessageAttempt(BoundUserInterfaceMessageAttempt args)
    {
        if (args.Cancelled || args.UiKey is not FishOreBagFilterUiKey.Key ||
            !TryComp<FishOreBagFilterComponent>(args.Target, out var filter))
        {
            return;
        }

        if (!CanAccessSettings((args.Target, filter), args.Actor))
            args.Cancel();
    }

    private void OnSetStack(Entity<FishOreBagFilterComponent> ent, ref FishOreBagFilterSetStackMessage args)
    {
        TrySetStackEnabled(ent, args.StackType, args.Enabled);
    }

    private void OnSetOther(Entity<FishOreBagFilterComponent> ent, ref FishOreBagFilterSetOtherMessage args)
    {
        TrySetOtherEnabled(ent, args.Enabled);
    }

    private void OnSetAll(Entity<FishOreBagFilterComponent> ent, ref FishOreBagFilterSetAllMessage args)
    {
        TrySetAllEnabled(ent, args.Enabled);
    }

    /// <summary>
    /// Checks only automatic pickup preferences; normal storage rules are checked separately.
    /// </summary>
    public bool CanCollect(Entity<FishOreBagFilterComponent> ent, EntityUid item)
    {
        if (!ent.Comp.Enabled)
            return true;

        EnsureStackTypes();
        if (_stackQuery.TryComp(item, out var stack) && _stackTypes.ContainsKey(stack.StackTypeId))
            return !ent.Comp.DisabledStackTypes.Contains(stack.StackTypeId);

        return ent.Comp.CollectOther;
    }

    /// <summary>
    /// Changes one category, rejecting stack types outside the filter catalog.
    /// </summary>
    public bool TrySetStackEnabled(Entity<FishOreBagFilterComponent> ent, ProtoId<StackPrototype> stackType, bool enabled)
    {
        if (!CanSetStackEnabled(ent, stackType))
            return false;

        var changed = enabled
            ? ent.Comp.DisabledStackTypes.Remove(stackType)
            : ent.Comp.DisabledStackTypes.Add(stackType);
        if (changed)
            SettingsChanged(ent);

        return true;
    }

    /// <summary>
    /// Checks whether a stack type is a selectable category on this bag.
    /// </summary>
    public bool CanSetStackEnabled(Entity<FishOreBagFilterComponent> ent, ProtoId<StackPrototype> stackType)
    {
        if (!CanChangeSettings(ent) || string.IsNullOrEmpty(stackType.Id))
            return false;

        EnsureStackTypes();
        return _stackTypes.ContainsKey(stackType);
    }

    /// <summary>
    /// Validates physical access for both opening the settings and every incoming settings message.
    /// </summary>
    public bool CanAccessSettings(Entity<FishOreBagFilterComponent> ent, EntityUid user)
    {
        return CanChangeSettings(ent) &&
               _interaction.InRangeAndAccessible(user, ent.Owner, _ui.GetUiRange(ent.Owner, FishOreBagFilterUiKey.Key));
    }

    /// <summary>
    /// Changes automatic pickup of other items allowed by the bag's storage whitelist.
    /// </summary>
    public bool TrySetOtherEnabled(Entity<FishOreBagFilterComponent> ent, bool enabled)
    {
        if (!CanChangeSettings(ent))
            return false;

        if (ent.Comp.CollectOther != enabled)
        {
            ent.Comp.CollectOther = enabled;
            SettingsChanged(ent);
        }

        return true;
    }

    /// <summary>
    /// Changes all ore categories and the other-items category together.
    /// </summary>
    public bool TrySetAllEnabled(Entity<FishOreBagFilterComponent> ent, bool enabled)
    {
        if (!CanChangeSettings(ent))
            return false;

        EnsureStackTypes();
        if (enabled)
        {
            if (ent.Comp.DisabledStackTypes.Count == 0 && ent.Comp.CollectOther)
                return true;

            ent.Comp.DisabledStackTypes.Clear();
        }
        else
        {
            if (!ent.Comp.CollectOther && ent.Comp.DisabledStackTypes.SetEquals(_stackTypes.Keys))
                return true;

            ent.Comp.DisabledStackTypes.Clear();
            foreach (var stackType in _stackTypes.Keys)
                ent.Comp.DisabledStackTypes.Add(stackType);
        }

        ent.Comp.CollectOther = enabled;
        SettingsChanged(ent);
        return true;
    }

    /// <summary>
    /// Refreshes and returns the ore and artifact-fragment categories for a settings window.
    /// </summary>
    public IReadOnlyCollection<StackPrototype> GetFilterableStackTypes()
    {
        // BUI получает перезагрузку прототипов до события, которое сбрасывает кеш системы.
        _stackTypes.Clear();
        _stackTypesLoaded = false;
        EnsureStackTypes();
        return _stackTypes.Values;
    }

    private static bool CanChangeSettings(Entity<FishOreBagFilterComponent> ent)
    {
        return ent.Comp.Enabled;
    }

    private void SettingsChanged(Entity<FishOreBagFilterComponent> ent)
    {
        Dirty(ent);
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<FishOreBagFilterComponent> ent)
    {
        if (_ui.TryGetOpenUi(ent.Owner, FishOreBagFilterUiKey.Key, out var bui))
            bui.Update();
    }

    private void EnsureStackTypes()
    {
        if (_stackTypesLoaded)
            return;

        _stackTypesLoaded = true;
        foreach (var stack in ProtoMan.EnumeratePrototypes<StackPrototype>())
        {
            if (!ProtoMan.TryIndex<EntityPrototype>(stack.Spawn, out var prototype) ||
                !prototype.TryComp<TagComponent>(out var tags, Factory))
            {
                continue;
            }

            if (_tag.HasTag(tags, OreTag) || _tag.HasTag(tags, FragmentTag))
                _stackTypes.Add(stack.ID, stack);
        }
    }
}
