using Content.Shared.Mind;

namespace Content.Server.Ghost;

/// <summary>
/// Raised on a newly spawned ghost before its mind is transferred or starts visiting it.
/// </summary>
[ByRefEvent]
public readonly record struct GhostSpawnedEvent(Entity<MindComponent> Mind);
