using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Fish.Storage;

[Serializable, NetSerializable]
public enum FishOreBagFilterUiKey : byte
{
    Key,
}

/// <summary>
/// Enables or disables automatic pickup of one ore stack type.
/// </summary>
[Serializable, NetSerializable]
public sealed class FishOreBagFilterSetStackMessage(ProtoId<StackPrototype> stackType, bool enabled) : BoundUserInterfaceMessage
{
    public ProtoId<StackPrototype> StackType = stackType;
    public bool Enabled = enabled;
}

/// <summary>
/// Enables or disables automatic pickup of items outside the ore stack catalog.
/// </summary>
[Serializable, NetSerializable]
public sealed class FishOreBagFilterSetOtherMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

/// <summary>
/// Enables or disables all automatic pickup categories together.
/// </summary>
[Serializable, NetSerializable]
public sealed class FishOreBagFilterSetAllMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}
