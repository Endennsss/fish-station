namespace Content.Shared._Fish.Research.Components;

/// <summary>A handheld instrument that contributes validated samples to its paired research server.</summary>
[RegisterComponent]
public sealed partial class FishResearchScannerComponent : Component
{
    /// <summary>The uninterrupted time required to measure a sample.</summary>
    [DataField]
    public TimeSpan ScanDelay = TimeSpan.FromSeconds(2);
}
