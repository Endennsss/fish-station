using Content.Server._Fish.Research.Components;
using Content.Shared._Fish.Research;
using Content.Shared._Fish.Research.Prototypes;
using Content.Shared.Database;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    /// <summary>Tracks practical results and exact payments separately from free technology grants.</summary>
    private void InitializeFishExperiments()
    {
        SubscribeLocalEvent<LatheComponent, FishLatheProducedEvent>(OnFishLatheProduced);
    }

    /// <summary>Whether an explicitly paired research server can currently accept measurements.</summary>
    public bool CanCollectFishExperimentSamples(EntityUid server)
    {
        return !TerminatingOrDeleted(server) && HasComp<ResearchServerComponent>(server) &&
               HasComp<TechnologyDatabaseComponent>(server) && CanRun(server);
    }

    /// <summary>Checks progress and novelty without changing the server or creating its ledger.</summary>
    public bool CanRecordFishExperimentSample(EntityUid server,
        FishResearchExperimentPrototype experiment, string sampleKey, int quantity = 1)
    {
        if (quantity <= 0 || string.IsNullOrWhiteSpace(sampleKey) || sampleKey.Length > 128 ||
            experiment.RequiredSamples <= 0 || !float.IsFinite(experiment.DiscountFraction) ||
            !CanCollectFishExperimentSamples(server) ||
            !ProtoMan.TryIndex(experiment.RewardTechnology, out var reward))
            return false;

        var database = Comp<TechnologyDatabaseComponent>(server);
        if (!database.SupportedDisciplines.Contains(reward.Discipline) ||
            database.FishExperimentProgress.GetValueOrDefault(experiment.ID) >= experiment.RequiredSamples)
            return false;

        return !experiment.DistinctSamples || !TryComp<FishResearchLedgerComponent>(server, out var ledger) ||
               !ledger.Samples.TryGetValue(experiment.ID, out var samples) || !samples.Contains(sampleKey);
    }

    /// <summary>
    /// Records a result from a trusted server-side action. Clients cannot submit experiment progress.
    /// Distinct samples contribute one unit; quantity experiments contribute only their remaining amount.
    /// </summary>
    public bool TryRecordFishExperimentSample(EntityUid server,
        FishResearchExperimentPrototype experiment, string sampleKey, int quantity = 1)
    {
        if (!CanRecordFishExperimentSample(server, experiment, sampleKey, quantity))
            return false;

        var database = Comp<TechnologyDatabaseComponent>(server);
        var ledger = EnsureComp<FishResearchLedgerComponent>(server);
        if (experiment.DistinctSamples)
        {
            if (!ledger.Samples.TryGetValue(experiment.ID, out var samples))
                ledger.Samples.Add(experiment.ID, samples = new HashSet<string>(StringComparer.Ordinal));
            samples.Add(sampleKey);
        }

        var previous = Math.Max(0, database.FishExperimentProgress.GetValueOrDefault(experiment.ID));
        var increase = experiment.DistinctSamples ? 1 : Math.Min(quantity, experiment.RequiredSamples - previous);
        var progress = previous + increase;
        database.FishExperimentProgress[experiment.ID] = progress;

        if (progress >= experiment.RequiredSamples)
        {
            RefundFishResearchPurchase(server, database, ledger, experiment.RewardTechnology);
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(server)} completed research experiment {experiment.ID}.");
        }

        Dirty(server, database);
        var ev = new TechnologyDatabaseModifiedEvent(null);
        RaiseLocalEvent(server, ref ev);
        return true;
    }

    private void RecordFishResearchPurchase(EntityUid server, ProtoId<TechnologyPrototype> technology, int paid)
    {
        var ledger = EnsureComp<FishResearchLedgerComponent>(server);
        ledger.PaidCosts.TryAdd(technology, Math.Max(0, paid));
    }

    private void RefundFishResearchPurchase(EntityUid server, TechnologyDatabaseComponent database,
        FishResearchLedgerComponent ledger, ProtoId<TechnologyPrototype> technologyId)
    {
        if (!database.UnlockedTechnologies.Contains(technologyId) ||
            !ledger.PaidCosts.TryGetValue(technologyId, out var paid) ||
            !ProtoMan.TryIndex(technologyId, out var technology))
            return;

        var alreadyRefunded = database.FishResearchRefunds.GetValueOrDefault(technologyId);
        var refund = paid - alreadyRefunded - GetTechnologyCost(database, technology);
        if (refund <= 0)
            return;

        // Fish-edit: возврат не масштабируется населением и не может переполнить баланс сервера.
        refund = (int) Math.Min(refund, (long) int.MaxValue - Comp<ResearchServerComponent>(server).Points);
        if (refund <= 0)
            return;

        database.FishResearchRefunds[technologyId] = alreadyRefunded + refund;
        ModifyServerPoints(server, refund);
    }

    private static void CopyFishExperimentSnapshot(TechnologyDatabaseComponent destination,
        TechnologyDatabaseComponent source)
    {
        destination.FishExperimentProgress = new(source.FishExperimentProgress);
        destination.FishResearchRefunds = new(source.FishResearchRefunds);
    }

    private static void ClearFishExperimentSnapshot(TechnologyDatabaseComponent database)
    {
        database.FishExperimentProgress = new();
        database.FishResearchRefunds = new();
    }

    private void OnFishLatheProduced(Entity<LatheComponent> ent, ref FishLatheProducedEvent args)
    {
        if (!TryGetClientServer(ent, out var server, out _))
            return;

        var quantity = TryComp<StackComponent>(args.Result, out var stack) ? stack.Count : 1;
        foreach (var experiment in ProtoMan.EnumeratePrototypes<FishResearchExperimentPrototype>())
        {
            if (experiment.Kind != FishResearchSampleKind.Manufacture ||
                !experiment.AcceptedRecipes.Contains(args.Recipe))
                continue;

            TryRecordFishExperimentSample(server.Value, experiment, args.Recipe.ToString(), quantity);
        }
    }
}
