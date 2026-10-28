using Robust.Shared.Serialization;

namespace Content.Shared._Fish.Xenobiology;

[Serializable, NetSerializable]
public enum FishXenobiologyUiKey : byte
{
    Key,
}

/// <summary>Read-only measurements from a powered laboratory instrument.</summary>
[Serializable, NetSerializable]
public sealed class FishXenobiologyBoundUserInterfaceState : BoundUserInterfaceState
{
    public bool Powered;
    public bool Processor;
    public bool CanProcess;
    public bool HasInput;
    public float Progress;
    public List<FishXenobiologySpecimen> Specimens = new();
}

/// <summary>A measured specimen; no client-supplied identity is used for laboratory actions.</summary>
[Serializable, NetSerializable]
public sealed class FishXenobiologySpecimen
{
    public string Breed = string.Empty;
    public bool Adult;
    public bool Dead;
    public int Generation;
    public float Growth;
    public float Biomass;
    public float Hunger;
    public float Temperature;
    public float MutationChance;
    public int Offspring;
    public int ExtractYield;
    public float GrowthRate;
    public float Docility;
    public float ThermalTolerance;
}

/// <summary>Requests processing of the machine's existing input, with server-side revalidation.</summary>
[Serializable, NetSerializable]
public sealed class FishXenobiologyProcessMessage : BoundUserInterfaceMessage;

/// <summary>Requests safe release of the machine's existing input.</summary>
[Serializable, NetSerializable]
public sealed class FishXenobiologyEjectMessage : BoundUserInterfaceMessage;
