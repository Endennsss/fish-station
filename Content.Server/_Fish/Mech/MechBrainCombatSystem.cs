using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
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

    private void OnPilotStartup(Entity<MechPilotComponent> ent, ref ComponentStartup args)
    {
        var uid = ent.Owner;
        if (!HasComp<BorgBrainComponent>(uid))
            return;

        var hadCombatMode = HasComp<CombatModeComponent>(uid);
        var hadBlockMovement = TryComp<BlockMovementComponent>(uid, out var existingBlockMovement);
        var brainCombat = EnsureComp<MechBrainCombatComponent>(uid);
        brainCombat.AddedBlockMovement = !hadBlockMovement;
        brainCombat.BlockInteraction = existingBlockMovement?.BlockInteraction ?? true;
        brainCombat.AddedCombatMode = !hadCombatMode;

        var blockMovement = existingBlockMovement ?? EnsureComp<BlockMovementComponent>(uid);
        blockMovement.BlockInteraction = false;

        if (!hadCombatMode)
            EnsureComp<CombatModeComponent>(uid);
    }

    private void OnPilotShutdown(Entity<MechPilotComponent> ent, ref ComponentShutdown args)
    {
        var uid = ent.Owner;
        if (!TryComp<MechBrainCombatComponent>(uid, out var brainCombat))
            return;

        if (brainCombat.AddedCombatMode && TryComp<CombatModeComponent>(uid, out var combat))
        {
            _combatMode.SetInCombatMode(uid, false, combat);
            RemComp<CombatModeComponent>(uid);
        }

        if (brainCombat.AddedBlockMovement)
            RemComp<BlockMovementComponent>(uid);
        else if (TryComp<BlockMovementComponent>(uid, out var blockMovement))
        {
            blockMovement.BlockInteraction = brainCombat.BlockInteraction;
        }

        RemComp<MechBrainCombatComponent>(uid);
        _actionBlocker.UpdateCanMove(uid);
    }
}
