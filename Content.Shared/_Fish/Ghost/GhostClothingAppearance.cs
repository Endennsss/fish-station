using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Fish.Ghost;

/// <summary>
/// Visual-only snapshot of an equipped item, with no reference to the item or its wearer.
/// </summary>
[Serializable, NetSerializable]
public sealed record GhostClothingAppearance(
    string Slot,
    EntProtoId? Prototype,
    string? RsiPath,
    string? EquippedPrefix,
    string? EquippedState,
    Vector2 Scale,
    Vector2 Offset,
    string? Species,
    string? BodyType,
    List<PrototypeLayerData>? Layers);
