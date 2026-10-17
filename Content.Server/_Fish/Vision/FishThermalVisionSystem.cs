using Content.Shared._Fish.Vision;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Temperature.Components;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Fish.Vision;

/// <summary>Samples nearby entity temperatures without sending contacts or expanding visibility.</summary>
public sealed partial class FishThermalVisionSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<Entity<TemperatureComponent>> _candidates = new();
    private readonly HashSet<EntityUid> _sampled = new();
    private TimeSpan _nextSample;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TemperatureComponent, ComponentShutdown>(OnTemperatureShutdown);
    }

    private void OnTemperatureShutdown(Entity<TemperatureComponent> ent, ref ComponentShutdown args)
    {
        RemCompDeferred<FishThermalSignatureComponent>(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSample)
            return;

        _nextSample = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        _sampled.Clear();
        var viewers = EntityQueryEnumerator<FishThermalVisionComponent, ActorComponent>();
        while (viewers.MoveNext(out var uid, out var vision, out var actor))
        {
            if (actor.PlayerSession.AttachedEntity != uid || HasComp<EyeProtectionComponent>(uid) ||
                !float.IsFinite(vision.Range) || vision.Range <= 0f)
                continue;

            _candidates.Clear();
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid), Math.Min(vision.Range, 32f),
                _candidates, LookupFlags.Uncontained);
            foreach (var candidate in _candidates)
            {
                if (!_sampled.Add(candidate) || _container.IsEntityInContainer(candidate) ||
                    TerminatingOrDeleted(candidate))
                    continue;

                var value = candidate.Comp.CurrentTemperature;
                if (!float.IsFinite(value) || value < 0f)
                {
                    RemCompDeferred<FishThermalSignatureComponent>(candidate);
                    continue;
                }

                // Обычный PVS сам решает, каким сессиям доступна сущность и её температура.
                var signature = EnsureComp<FishThermalSignatureComponent>(candidate);
                value = MathF.Round(value);
                if (signature.Temperature == value)
                    continue;

                signature.Temperature = value;
                Dirty(candidate, signature);
            }
        }
    }
}
