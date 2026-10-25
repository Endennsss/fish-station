namespace Content.Shared._Fish.Storage;

/// <summary>
/// Raised on a magnet before automatic insertion, including insertion that merges an existing stack.
/// </summary>
[ByRefEvent]
public record struct MagnetPickupAttemptEvent(EntityUid Item)
{
    /// <summary>
    /// Whether this automatic pickup should be skipped.
    /// </summary>
    public bool Cancelled;
}
