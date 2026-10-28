using Content.Shared._Fish.Research.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Research.Components;

public sealed partial class TechnologyDatabaseComponent
{
    /// <summary>The server's bounded experiment progress, copied to each connected console.</summary>
    [AutoNetworkedField]
    public Dictionary<ProtoId<FishResearchExperimentPrototype>, int> FishExperimentProgress = new();

    /// <summary>The exact research points already refunded for each paid technology.</summary>
    [AutoNetworkedField]
    public Dictionary<ProtoId<TechnologyPrototype>, int> FishResearchRefunds = new();
}
