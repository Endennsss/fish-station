using Content.Shared._Fish.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;

namespace Content.Server._Fish.Ghost;

/// <summary>
/// Retains appearance on a mind when its body is removed before a ghost is spawned.
/// </summary>
[RegisterComponent]
public sealed partial class StoredGhostAppearanceComponent : Component
{
    /// <summary>
    /// The last body's head markings with independent color lists.
    /// </summary>
    public List<Marking> Markings = [];

    /// <summary>
    /// Visual snapshots of the last body's equipped clothing.
    /// </summary>
    public List<GhostClothingAppearance> Clothing = [];

    /// <summary>
    /// Head layers hidden by the saved clothing.
    /// </summary>
    public HashSet<HumanoidVisualLayers> HiddenLayers = [];

    /// <summary>
    /// Body captured before gibbing began, whose emptied inventory must not replace the snapshot.
    /// </summary>
    public EntityUid? GibbedBody;
}
