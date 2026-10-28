using Content.Server.Research.Systems;
using Content.Shared._Fish.Research;
using Content.Shared._Fish.Research.Components;
using Content.Shared._Fish.Research.Prototypes;
using Content.Shared._Fish.Xenobiology;
using Content.Shared.Anomaly.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell.Components;
using Content.Shared.Research.Components;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Fish.Research;

/// <summary>Measures real sample state after a cancellable action and credits only the paired database.</summary>
public sealed partial class FishResearchScannerSystem : EntitySystem
{
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBatterySystem _battery = default!;
    [Dependency] private SharedXenoArtifactSystem _xenoArtifact = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FishResearchScannerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<FishResearchScannerComponent, FishResearchScanDoAfterEvent>(OnScanFinished);
        SubscribeLocalEvent<FishResearchScannerComponent, BoundUserInterfaceMessageAttempt>(OnUiMessageAttempt);
    }

    private void OnUiMessageAttempt(Entity<FishResearchScannerComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (args.UiKey is ResearchClientUiKey && !_hands.IsHolding(args.Actor, ent))
            args.Cancel();
    }

    private void OnAfterInteract(Entity<FishResearchScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach)
            return;

        args.Handled = TryScan(ent, args.User, target);
    }

    /// <summary>Starts a measurement only when the held instrument has an eligible new sample.</summary>
    public bool TryScan(Entity<FishResearchScannerComponent> scanner,
        EntityUid user, EntityUid target, bool quiet = false)
    {
        if (!CanScan(scanner, user, target, out var server, quiet))
            return false;

        var args = new DoAfterArgs(EntityManager, user, scanner.Comp.ScanDelay,
            new FishResearchScanDoAfterEvent { Server = GetNetEntity(server) }, scanner,
            target: target, used: scanner)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 2f,
        };
        return _doAfter.TryStartDoAfter(args);
    }

    /// <summary>Checks ownership, reachability, server membership and sample novelty without modifying state.</summary>
    public bool CanScan(Entity<FishResearchScannerComponent> scanner,
        EntityUid user, EntityUid target, out EntityUid server, bool quiet = false)
    {
        server = default;
        if (TerminatingOrDeleted(scanner) || TerminatingOrDeleted(user) || TerminatingOrDeleted(target) ||
            !_hands.IsHolding(user, scanner) || !_interaction.InRangeUnobstructed(user, target))
            return false;

        if (!_research.TryGetClientServer(scanner, out var pairedServer, out var researchServer) ||
            !researchServer.Clients.Contains(scanner) || !_research.CanCollectFishExperimentSamples(pairedServer.Value))
        {
            if (!quiet)
                _popup.PopupEntity(Loc.GetString("fish-research-scanner-no-server"), scanner, user);
            return false;
        }

        server = pairedServer.Value;
        foreach (var experiment in _prototype.EnumeratePrototypes<FishResearchExperimentPrototype>())
        {
            if (TryGetSampleKey(target, experiment, out var sampleKey) &&
                _research.CanRecordFishExperimentSample(server, experiment, sampleKey))
                return true;
        }

        if (!quiet)
            _popup.PopupEntity(WithAnomalyReadings(target, Loc.GetString("fish-research-scanner-no-sample")), scanner, user);
        return false;
    }

    private void OnScanFinished(Entity<FishResearchScannerComponent> ent, ref FishResearchScanDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        if (!CanScan(ent, args.User, target, out var server, quiet: true) ||
            GetNetEntity(server) != args.Server)
        {
            _popup.PopupEntity(Loc.GetString("fish-research-scanner-measurement-invalid"), ent, args.User);
            return;
        }

        var advanced = 0;
        foreach (var experiment in _prototype.EnumeratePrototypes<FishResearchExperimentPrototype>())
        {
            if (TryGetSampleKey(target, experiment, out var sampleKey) &&
                _research.TryRecordFishExperimentSample(server, experiment, sampleKey))
                advanced++;
        }

        var message = advanced > 0
            ? Loc.GetString("fish-research-scanner-scan-complete", ("count", advanced))
            : Loc.GetString("fish-research-scanner-no-sample");
        _popup.PopupEntity(WithAnomalyReadings(target, message), ent, args.User);
    }

    private string WithAnomalyReadings(EntityUid target, string message)
    {
        if (!TryComp<AnomalyComponent>(target, out var anomaly))
            return message;

        return message + "\n" + Loc.GetString("fish-research-scanner-anomaly-readings",
            ("severity", MathF.Round(anomaly.Severity * 100)),
            ("stability", MathF.Round(anomaly.Stability * 100)),
            ("health", MathF.Round(anomaly.Health * 100)));
    }

    private bool TryGetSampleKey(EntityUid target, FishResearchExperimentPrototype experiment, out string sampleKey)
    {
        sampleKey = string.Empty;
        switch (experiment.Kind)
        {
            case FishResearchSampleKind.Anomaly:
                if (!TryComp<AnomalyComponent>(target, out var anomaly) ||
                    !float.IsFinite(anomaly.Severity) || !float.IsFinite(anomaly.Stability) ||
                    !float.IsFinite(anomaly.Health) || anomaly.Health <= 0 ||
                    anomaly.Severity < experiment.MinimumSeverity ||
                    anomaly.Stability < experiment.MinimumStability ||
                    anomaly.Stability > experiment.MaximumStability || anomaly.Health < experiment.MinimumHealth)
                    return false;

                sampleKey = anomaly.CorePrototype?.ToString() ?? Prototype(target)?.ID ?? string.Empty;
                return sampleKey.Length > 0;

            case FishResearchSampleKind.Artifact:
                if (!TryComp<XenoArtifactComponent>(target, out var artifact))
                    return false;

                var unlocked = 0;
                foreach (var node in _xenoArtifact.GetAllNodes((target, artifact)))
                {
                    if (!node.Comp.Locked)
                        unlocked++;
                }

                if (unlocked < experiment.MinimumUnlockedNodes)
                    return false;

                sampleKey = GetNetEntity(target).ToString();
                return true;

            case FishResearchSampleKind.Humanoid:
                if (!TryComp<HumanoidProfileComponent>(target, out var profile) || !_mobState.IsAlive(target))
                    return false;

                var species = profile.Species;
                sampleKey = species.ToString();
                return true;

            case FishResearchSampleKind.PowerCell:
                if (!HasComp<PowerCellComponent>(target) || Prototype(target) is not { } cellPrototype)
                    return false;

                var (charge, capacity) = _battery.GetCharge(target);
                if (!float.IsFinite(charge) || !float.IsFinite(capacity) || capacity <= 0 ||
                    charge / capacity < experiment.MinimumChargeFraction)
                    return false;

                sampleKey = cellPrototype.ID;
                return true;

            case FishResearchSampleKind.Xenobiology:
                if (!TryComp<FishXenobiologySlimeComponent>(target, out var slime) || slime.Consumed ||
                    !_mobState.IsAlive(target) || experiment.RequireAdultSlime && !slime.Adult ||
                    !_prototype.TryIndex(slime.Breed, out var breed) || breed.Tier < experiment.MinimumSlimeTier)
                    return false;

                sampleKey = breed.ID;
                return true;

            default:
                return false;
        }
    }
}
