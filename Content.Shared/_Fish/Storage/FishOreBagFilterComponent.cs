using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Fish.Storage;

/// <summary>
/// Stores an ore bag's automatic pickup preferences without restricting manual storage insertion.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class FishOreBagFilterComponent : Component
{
    /// <summary>
    /// Whether this bag provides an automatic pickup filter and its settings interface.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    /// Stack types excluded from automatic pickup.
    /// </summary>
    [AutoNetworkedField]
    public HashSet<ProtoId<StackPrototype>> DisabledStackTypes = new();

    /// <summary>
    /// Whether automatic pickup accepts admissible items outside the ore stack catalog.
    /// </summary>
    [AutoNetworkedField]
    public bool CollectOther = true;
}
