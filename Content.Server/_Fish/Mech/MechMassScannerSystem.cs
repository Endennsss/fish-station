using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Shuttles.BUIStates;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Fish.Mech;

public sealed partial class MechMassScannerSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MechComponent, MechOpenMassScannerEvent>(OnOpenMassScanner);
    }

    private void OnOpenMassScanner(EntityUid uid, MechComponent component, MechOpenMassScannerEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryOpenMassScanner(uid, component, args.Performer);
    }

    private bool TryOpenMassScanner(EntityUid uid, MechComponent component, EntityUid performer)
    {
        if (!CanOpenMassScanner(uid, component, performer))
            return false;

        var pilot = component.PilotSlot.ContainedEntity!.Value;
        var actor = Comp<ActorComponent>(pilot);
        DoOpenMassScanner(uid, actor);
        return true;
    }

    private bool CanOpenMassScanner(EntityUid uid, MechComponent component, EntityUid performer)
    {
        if (component.PilotSlot.ContainedEntity != performer)
            return false;

        if (!TryComp<ActorComponent>(performer, out _))
            return false;

        return _ui.HasUi(uid, RadarConsoleUiKey.Key);
    }

    private void DoOpenMassScanner(EntityUid uid, ActorComponent actor)
    {
        _ui.TryToggleUi(uid, RadarConsoleUiKey.Key, actor.PlayerSession);
    }
}
