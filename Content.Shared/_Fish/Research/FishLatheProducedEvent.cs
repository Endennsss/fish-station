using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Fish.Research;

/// <summary>Raised on a lathe after producing a real item, before its stack can merge or disappear.</summary>
[ByRefEvent]
public readonly record struct FishLatheProducedEvent(ProtoId<LatheRecipePrototype> Recipe, EntityUid Result);
