using Content.Shared.Mech.Components;
using Content.Shared.CombatMode;
using Content.Shared.Silicons.Borgs.Components;

#pragma warning disable IDE0130
namespace Content.Shared.Mech.EntitySystems;

public abstract partial class SharedMechSystem
{
    [Dependency] private SharedCombatModeSystem _combatMode = default!;

    private void EnableFishBrainCombat(EntityUid pilot)
    {
        if (!HasComp<BorgBrainComponent>(pilot) || !TryComp<CombatModeComponent>(pilot, out var combatMode))
            return;

        _combatMode.SetInCombatMode(pilot, true, combatMode);
    }

    private void UpdateFishBrainMovement(EntityUid pilot)
    {
        if (HasComp<BorgBrainComponent>(pilot))
            _actionBlocker.UpdateCanMove(pilot);
    }

    private void AddFishMechActions(EntityUid pilot, EntityUid mech, MechComponent component)
    {
        if (component.MechMassScannerAction is not { } action)
            return;

        _actions.AddAction(pilot, ref component.MechMassScannerActionEntity, action, mech);
    }

    protected bool CanInsertBrain(EntityUid mech, EntityUid entity)
    {
        return TryComp<MechBrainComponent>(mech, out var mechBrain) &&
               mechBrain.CanInsertBrain &&
               HasComp<BorgBrainComponent>(entity);
    }
}
