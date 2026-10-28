using Content.Server.Research.Systems;
using Content.Shared._Fish.Research.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Fish.Research.Components;

/// <summary>Server-only sample identities and actual payments; never copied from a research client.</summary>
[RegisterComponent, Access(typeof(ResearchSystem))]
public sealed partial class FishResearchLedgerComponent : Component
{
    /// <summary>Sample identities already accepted by each distinct-sample experiment.</summary>
    public Dictionary<ProtoId<FishResearchExperimentPrototype>, HashSet<string>> Samples = new();

    /// <summary>The amount actually charged for each successfully purchased technology.</summary>
    public Dictionary<ProtoId<TechnologyPrototype>, int> PaidCosts = new();
}
