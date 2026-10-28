using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Fish.Research;

/// <summary>A measurement bound to the research server selected when scanning started.</summary>
[Serializable, NetSerializable]
public sealed partial class FishResearchScanDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>The captured server; a later selection cannot redirect an in-progress measurement.</summary>
    [DataField]
    public NetEntity Server;
}
