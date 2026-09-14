using Robust.Shared.Serialization;

namespace Content.Shared._Fish.Dissolve;

/// <summary>Сервер подтвердил окончательное сгорание тела, а не обычную смерть или удаление.</summary>
[Serializable, NetSerializable]
public sealed class BurnAwayEvent(NetEntity entity) : EntityEventArgs
{
    public readonly NetEntity Entity = entity;
}
