using Content.Shared._Fish.Dissolve;
using Robust.Shared.Player;

namespace Content.Server._Fish.Dissolve;

/// <summary>Уведомляет наблюдателей о сгорании, не задерживая серверное удаление тела.</summary>
public sealed class BurnAwaySystem : EntitySystem
{
    public void NotifyBurn(EntityUid uid)
    {
        RaiseNetworkEvent(new BurnAwayEvent(GetNetEntity(uid)), Filter.Pvs(uid, entityManager: EntityManager));
    }
}
