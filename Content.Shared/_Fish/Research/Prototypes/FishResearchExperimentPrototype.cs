using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Fish.Research.Prototypes;

/// <summary>
/// A practical research objective shared by clients of one research server.
/// Completion discounts a technology without changing its prerequisites.
/// </summary>
[Prototype]
public sealed partial class FishResearchExperimentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The localized experiment title.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The localized instructions, formatted using the configured sample thresholds.</summary>
    [DataField(required: true)]
    public LocId Description;

    /// <summary>The editor suffix identifying this fork's definition.</summary>
    [DataField]
    public string Suffix = "Fish";

    /// <summary>The action and sample category that can advance this experiment.</summary>
    [DataField(required: true)]
    public FishResearchSampleKind Kind;

    /// <summary>The number of qualifying samples or produced units required for completion.</summary>
    [DataField]
    public int RequiredSamples = 1;

    /// <summary>Whether only the first occurrence of each sample key contributes one unit.</summary>
    [DataField]
    public bool DistinctSamples = true;

    /// <summary>The technology receiving the completed experiment's discount.</summary>
    [DataField(required: true)]
    public ProtoId<TechnologyPrototype> RewardTechnology;

    /// <summary>The fraction of base price saved; only the strongest completed discount applies.</summary>
    [DataField]
    public float DiscountFraction = 0.25f;

    /// <summary>The recipes accepted by a manufacturing experiment.</summary>
    [DataField]
    public HashSet<ProtoId<LatheRecipePrototype>> AcceptedRecipes = new();

    /// <summary>The minimum anomaly severity, expressed as a fraction.</summary>
    [DataField]
    public float MinimumSeverity;

    /// <summary>The minimum anomaly instability, expressed as a fraction.</summary>
    [DataField]
    public float MinimumStability;

    /// <summary>The maximum anomaly instability, expressed as a fraction.</summary>
    [DataField]
    public float MaximumStability = 1f;

    /// <summary>The minimum anomaly health, expressed as a fraction.</summary>
    [DataField]
    public float MinimumHealth;

    /// <summary>The number of actually unlocked nodes a scanned artifact must contain.</summary>
    [DataField]
    public int MinimumUnlockedNodes;

    /// <summary>The minimum current charge divided by a power cell's positive maximum charge.</summary>
    [DataField]
    public float MinimumChargeFraction;

    /// <summary>The lowest mutation tier accepted by a xenobiology survey.</summary>
    [DataField]
    public int MinimumSlimeTier;

    /// <summary>Whether a xenobiology survey requires mature, living specimens.</summary>
    [DataField]
    public bool RequireAdultSlime;
}
