using Robust.Shared.GameStates;

namespace Content.Shared._Fish.Vision;

/// <summary>Entity temperature delivered with the target's ordinary PVS state.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FishThermalSignatureComponent : Component
{
    /// <summary>Server-measured temperature in kelvin, quantized to limit network changes.</summary>
    [AutoNetworkedField]
    public float Temperature;
}
