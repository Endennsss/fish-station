using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Shared.Research.Systems;

public abstract partial class SharedResearchSystem
{
    /// <summary>
    /// Gets the price after the strongest completed practical experiment for this technology.
    /// Prerequisites and point balance are validated separately by the purchasing system.
    /// </summary>
    public int GetTechnologyCost(TechnologyDatabaseComponent database, TechnologyPrototype technology)
    {
        var discount = 0f;
        foreach (var (id, progress) in database.FishExperimentProgress)
        {
            if (!ProtoMan.TryIndex(id, out var experiment) || experiment.RequiredSamples <= 0 ||
                progress < experiment.RequiredSamples || experiment.RewardTechnology != technology.ID ||
                !float.IsFinite(experiment.DiscountFraction))
                continue;

            discount = MathF.Max(discount, Math.Clamp(experiment.DiscountFraction, 0f, 1f));
        }

        // Fish-edit: округляем итоговую цену вверх, чтобы дробная скидка не давала лишних очков.
        return (int) Math.Ceiling(Math.Max(0, technology.Cost) * (1.0 - discount));
    }

    private void UpdateFishTechnologyChoices(EntityUid uid, TechnologyDatabaseComponent database)
    {
        var available = GetAvailableTechnologies(uid, database);
        available.Sort(static (left, right) => string.Compare(left.ID, right.ID, StringComparison.Ordinal));
        database.CurrentTechnologyCards.Clear();
        foreach (var technology in available)
            database.CurrentTechnologyCards.Add(technology.ID);
        Dirty(uid, database);
    }
}
