using Content.Shared.CombatMode;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction.Components;
using Content.Shared.Mech.Components;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server._Fish.Mech;

public sealed partial class MechBrainCombatSystem : EntitySystem
{
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MechPilotComponent, ComponentStartup>(OnPilotStartup);
        SubscribeLocalEvent<MechPilotComponent, ComponentShutdown>(OnPilotShutdown);
    }

    private void OnPilotStartup(EntityUid uid, MechPilotComponent component, ComponentStartup args)
    {
        if (!HasComp<BorgBrainComponent>(uid) || HasComp<CombatModeComponent>(uid))
            return;

        EnsureComp<MechBrainCombatComponent>(uid);
        var blockMovement = EnsureComp<BlockMovementComponent>(uid);
        blockMovement.BlockInteraction = false;
        EnsureComp<CombatModeComponent>(uid);
    }

    private void OnPilotShutdown(EntityUid uid, MechPilotComponent component, ComponentShutdown args)
    {
        if (!HasComp<MechBrainCombatComponent>(uid))
            return;

        if (TryComp<CombatModeComponent>(uid, out var combat))
        {
            _combatMode.SetInCombatMode(uid, false, combat);
            RemComp<CombatModeComponent>(uid);
        }

        RemComp<MechBrainCombatComponent>(uid);
        _actionBlocker.UpdateCanMove(uid);
    }
}
