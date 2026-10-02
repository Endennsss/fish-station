using Content.Shared.Interaction;
using Content.Shared.Mech.Components;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server.Mech.Systems;

public sealed partial class MechSystem
{
    private bool HandleFishBrainInteraction(EntityUid uid, MechComponent component, InteractUsingEvent args)
    {
        if (!CanInsert(uid, args.Used, component) || !CanInsertBrain(uid, args.Used))
            return false;

        // FIsh edit — ЛКМ с мозгом, pAI или MMI в руке вставляет его в мех.
        args.Handled = TryInsert(uid, args.Used, component);
        return args.Handled;
    }
}
