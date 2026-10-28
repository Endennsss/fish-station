namespace Content.Shared.Research.Components;

public sealed partial class ResearchClientComponent
{
    /// <summary>Whether initialization and anchoring may automatically select a research server.</summary>
    [DataField]
    public bool FishAutoConnect = true;
}
