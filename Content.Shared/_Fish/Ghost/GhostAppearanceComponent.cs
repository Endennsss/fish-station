using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.GameStates;

namespace Content.Shared._Fish.Ghost;

/// <summary>
/// A snapshot of the deceased character's head markings and worn clothing, independent of their body.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class GhostAppearanceComponent : Component
{
    /// <summary>
    /// Head markings and their resolved colors at the time the ghost was created.
    /// </summary>
    [AutoNetworkedField]
    public List<Marking> Markings = [];

    /// <summary>
    /// Equipped clothing visuals at the time the ghost was created.
    /// </summary>
    [AutoNetworkedField]
    public List<GhostClothingAppearance> Clothing = [];

    /// <summary>
    /// Head layers hidden by the saved outfit.
    /// </summary>
    [AutoNetworkedField]
    public HashSet<HumanoidVisualLayers> HiddenLayers = [];

    /// <summary>
    /// Client-only keys of the sprite layers currently displaying the snapshot.
    /// </summary>
    public List<string> AppliedLayers = [];
}
