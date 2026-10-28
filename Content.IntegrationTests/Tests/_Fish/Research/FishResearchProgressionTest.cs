using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Power.EntitySystems;
using Content.Shared._Fish.Research.Prototypes;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using ServerResearchSystem = Content.Server.Research.Systems.ResearchSystem;

namespace Content.IntegrationTests.Tests._Fish.Research;

[TestFixture, NonParallelizable]
public sealed class FishResearchProgressionTest
{
    private static readonly ProtoId<TechnologyPrototype> AdvancedCleaning = "AdvancedCleaning";
    private static readonly ProtoId<TechnologyPrototype> Modsuits = "Modsuits";

    [TestPrototypes]
    private const string Experiments = """
- type: fishResearchExperiment
  id: FishStrongerMedicalExperimentTest
  suffix: Fish
  name: fish-research-experiment-medical-fabrication-name
  description: fish-research-experiment-medical-fabrication-description
  rewardTechnology: AdvancedCleaning
  discountFraction: 0.5
  requiredSamples: 1
  kind: Manufacture
  acceptedRecipes: []
""";

    [Test]
    public async Task AvailableTechnologiesIncludeEveryEligibleTechnologyAndRemainStable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var research = server.System<ServerResearchSystem>();
        EntityUid researchServer = default;
        string[] available = [];
        var multipleInOneDiscipline = false;
        var refreshes = new List<string[]>();
        var completed = false;

        try
        {
            await server.WaitPost(() =>
            {
                researchServer = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
                var technologies = research.GetAvailableTechnologies(researchServer);
                available = technologies.Select(technology => technology.ID).ToArray();
                multipleInOneDiscipline = technologies.GroupBy(technology => technology.Discipline)
                    .Any(discipline => discipline.Count() > 1);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(available, Is.Not.Empty);
                Assert.That(multipleInOneDiscipline, Is.True,
                    "The scenario must cover several eligible technologies from the same discipline.");
            });

            for (var refresh = 0; refresh < 5; refresh++)
            {
                await server.WaitPost(() =>
                {
                    research.UpdateTechnologyCards(researchServer);
                    var snapshot = new List<string>();
                    var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                    foreach (var technology in database.CurrentTechnologyCards)
                        snapshot.Add(technology);
                    refreshes.Add(snapshot.ToArray());
                });
            }

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    foreach (var technologies in refreshes)
                    {
                        Assert.That(technologies, Is.EquivalentTo(available),
                            "Every eligible technology must be offered, including technologies sharing a discipline.");
                        Assert.That(technologies, Is.EqualTo(refreshes[0]),
                            "Refreshing an unchanged database must preserve technology IDs and their order.");
                    }
                });
            });
            completed = true;
        }
        finally
        {
            await server.WaitPost(() => server.EntMan.DeleteEntity(map.MapUid));
            if (completed)
                await pair.CleanReturnAsync();
            else
                await pair.DisposeAsync();
        }
    }

    /// <summary>Buying a technology is not a substitute for completing a practical experiment.</summary>
    [Test]
    public async Task ResearchingTechnologyDoesNotCompleteHandsOnExperiment()
    {
        await WithResearchConsole(async (pair, researchServer, _, _) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var target = server.ProtoMan.Index(AdvancedCleaning);

            await server.WaitPost(() => research.AddTechnology(researchServer, "SurgeryTech"));
            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                Assert.That(research.GetTechnologyCost(database, target), Is.EqualTo(target.Cost),
                    "A technology purchase must not complete a hands-on experiment or award its discount.");
            });
        });
    }

    /// <summary>A practical experiment changes the price without gating access to its reward.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task OptionalExperimentDeterminesAdvancedCleaningPurchasePrice(bool completeExperiment)
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var target = server.ProtoMan.Index(AdvancedCleaning);
            var expectedCost = completeExperiment ? (int) Math.Ceiling(target.Cost * 0.75) : target.Cost;
            var purchased = false;

            await server.WaitPost(() =>
            {
                GrantPrerequisites(pair, researchServer, target, new HashSet<string>());
                if (completeExperiment)
                    CompleteExperiment(pair, researchServer, "FishMedicalFabrication");
                research.SyncClientWithServer(console);
                var points = server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points;
                research.ModifyServerPoints(researchServer, expectedCost - points);
            });

            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(console);
                Assert.That(research.IsTechnologyAvailable(database, target), Is.True,
                    "Reward availability must depend on its explicit prerequisites, independently of the experiment.");
                Assert.That(server.EntMan.GetComponent<ResearchClientComponent>(console).Server,
                    Is.EqualTo(researchServer));
            });

            await server.WaitPost(() => purchased = research.UnlockTechnology(console, target, user));

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(purchased, Is.True,
                        "The reward must be purchasable for its current price with or without completing the experiment.");
                    Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer).UnlockedTechnologies,
                        Does.Contain((ProtoId<TechnologyPrototype>) target.ID));
                    Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points, Is.Zero,
                        "The purchase must charge exactly the discounted price.");
                });
            });
        });
    }

    /// <summary>Late discounts refund only the newly earned difference from the actual payment.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task LateExperimentRefundsOnlyPaidDifferenceOnce(bool discountedPurchase)
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var target = server.ProtoMan.Index(AdvancedCleaning);
            var paid = discountedPurchase ? (int) Math.Ceiling(target.Cost * 0.75) : target.Cost;
            const int budget = 100000;
            var purchased = false;

            await server.WaitPost(() =>
            {
                GrantPrerequisites(pair, researchServer, target, new HashSet<string>());
                if (discountedPurchase)
                    CompleteExperiment(pair, researchServer, "FishMedicalFabrication");
                research.ModifyServerPoints(researchServer,
                    budget - server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points);
                purchased = research.UnlockTechnology(console, target, user);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(purchased, Is.True);
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points,
                    Is.EqualTo(budget - paid));
            });

            await server.WaitPost(() => CompleteExperiment(pair, researchServer, "FishMedicalFabrication"));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points,
                    Is.EqualTo(budget - (int) Math.Ceiling(target.Cost * 0.75)));
                Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(console)
                    .FishResearchRefunds.GetValueOrDefault(AdvancedCleaning),
                    Is.EqualTo(discountedPurchase ? 0 : target.Cost - (int) Math.Ceiling(target.Cost * 0.75)));
            });

            await server.WaitPost(() =>
            {
                CompleteExperiment(pair, researchServer, "FishStrongerMedicalExperimentTest");
                CompleteExperiment(pair, researchServer, "FishMedicalFabrication");
                CompleteExperiment(pair, researchServer, "FishStrongerMedicalExperimentTest");
            });
            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                var finalCost = (int) Math.Ceiling(target.Cost * 0.5);
                Assert.That(research.GetTechnologyCost(database, target), Is.EqualTo(finalCost),
                    "The strongest discount must replace the earlier discount without stacking.");
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points,
                    Is.EqualTo(budget - finalCost));
                Assert.That(database.FishResearchRefunds.GetValueOrDefault(AdvancedCleaning), Is.EqualTo(paid - finalCost));
            });
        });
    }

    /// <summary>A grant without payment never creates points when an experiment completes.</summary>
    [Test]
    public async Task FreeTechnologyGrantNeverReceivesExperimentRefund()
    {
        await WithResearchConsole(async (pair, researchServer, console, _) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var originalPoints = 0;
            await server.WaitPost(() =>
            {
                research.AddTechnology(researchServer, AdvancedCleaning);
                originalPoints = server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points;
                CompleteExperiment(pair, researchServer, "FishMedicalFabrication");
                CompleteExperiment(pair, researchServer, "FishStrongerMedicalExperimentTest");
                CompleteExperiment(pair, researchServer, "FishMedicalFabrication");
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points, Is.EqualTo(originalPoints));
                Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(console)
                    .FishResearchRefunds.GetValueOrDefault(AdvancedCleaning), Is.Zero);
            });
        });
    }

    [Test]
    public async Task DuplicateSampleDoesNotCompleteExperimentOrCreateDiscount()
    {
        await WithResearchConsole(async (pair, researchServer, _, _) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            ProtoId<FishResearchExperimentPrototype> experimentId = "FishAnomalySurvey";
            var experiment = server.ProtoMan.Index(experimentId);
            var target = server.ProtoMan.Index(experiment.RewardTechnology);
            var accepted = new List<bool>();
            await server.WaitPost(() =>
            {
                for (var attempt = 0; attempt < experiment.RequiredSamples + 1; attempt++)
                    accepted.Add(research.TryRecordFishExperimentSample(researchServer, experiment, "same-family"));
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(accepted[0], Is.True);
                Assert.That(accepted.Skip(1), Is.All.EqualTo(false));
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                Assert.That(database.FishExperimentProgress.GetValueOrDefault(experiment.ID), Is.EqualTo(1));
                Assert.That(research.GetTechnologyCost(database, target), Is.EqualTo(target.Cost));
            });
        });
    }

    [Test]
    public async Task ExplicitPrerequisitesAllowHigherTierPurchaseWithoutDisciplineCompletionOrLockout()
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var target = server.ProtoMan.Index(Modsuits);
            var available = false;
            var purchased = false;

            await server.WaitPost(() =>
            {
                GrantPrerequisites(pair, researchServer, target, new HashSet<string>());
                research.SyncClientWithServer(console);
                var points = server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points;
                research.ModifyServerPoints(researchServer, target.Cost - points);
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(console);
                available = research.IsTechnologyAvailable(database, target);
                purchased = research.UnlockTechnology(console, target, user);
            });

            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                Assert.Multiple(() =>
                {
                    Assert.That(available, Is.True,
                        "Explicit prerequisites must allow research without completing 75% of earlier discipline tiers.");
                    Assert.That(purchased, Is.True);
                    Assert.That(database.UnlockedTechnologies,
                        Does.Contain((ProtoId<TechnologyPrototype>) target.ID));
                    Assert.That(database.MainDiscipline, Is.Null,
                        "Buying a higher tier must preserve access to other disciplines.");
                });
            });
        });
    }

    /// <summary>Each experiment is shared by its consoles while another server retains its own price and progress.</summary>
    [TestCase("FishAnomalySurvey", "AdvancedAnomalyResearch")]
    [TestCase("FishControlledAnomaly", "AnomalyCoreHarnessing")]
    [TestCase("FishArtifactStudy", "AbnormalArtifactManipulation")]
    [TestCase("FishBiologicalSurvey", "MechanizedMedicalTreatment")]
    [TestCase("FishPowerCellStudy", "FishMicroreactors")]
    [TestCase("FishRoboticsFabrication", "BiochemicalStasis")]
    [TestCase("FishMedicalFabrication", "AdvancedCleaning")]
    [TestCase("FishUraniumProcessingExperiment", "FishBluespaceTheory")]
    public async Task ExperimentDiscountBelongsToItsServerAndDoesNotStack(string experimentId, string rewardId)
    {
        await WithResearchConsole(async (pair, researchServer, console, _) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            ProtoId<TechnologyPrototype> rewardProto = rewardId;
            var experiment = server.ProtoMan.Index<FishResearchExperimentPrototype>(experimentId);
            var reward = server.ProtoMan.Index(rewardProto);
            var discountedCost = (int) Math.Ceiling(reward.Cost * 0.75);
            EntityUid secondConsole = default;
            EntityUid independentServer = default;

            await server.WaitPost(() =>
            {
                var coordinates = server.EntMan.GetComponent<TransformComponent>(console).Coordinates;
                secondConsole = server.EntMan.SpawnEntity("ComputerResearchAndDevelopment", coordinates);
                independentServer = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer", coordinates);
                server.System<PowerReceiverSystem>().SetNeedsPower(independentServer, false);
                research.RegisterClient(secondConsole, researchServer);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(experiment.RewardTechnology, Is.EqualTo(rewardProto));
                Assert.That(research.GetTechnologyCost(server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer), reward),
                    Is.EqualTo(reward.Cost));
            });

            await server.WaitPost(() => CompleteExperiment(pair, researchServer, experimentId));
            await server.WaitAssertion(() =>
            {
                foreach (var entity in new[] { researchServer, console, secondConsole })
                {
                    var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(entity);
                    Assert.That(research.GetTechnologyCost(database, reward), Is.EqualTo(discountedCost));
                    Assert.That(database.FishExperimentProgress.GetValueOrDefault(experimentId),
                        Is.EqualTo(experiment.RequiredSamples));
                }

                var independentDatabase = server.EntMan.GetComponent<TechnologyDatabaseComponent>(independentServer);
                Assert.That(research.GetTechnologyCost(independentDatabase, reward), Is.EqualTo(reward.Cost));
                Assert.That(independentDatabase.FishExperimentProgress.GetValueOrDefault(experimentId), Is.Zero);
            });
            await server.WaitPost(() => CompleteExperiment(pair, researchServer, experimentId));
            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                Assert.That(research.GetTechnologyCost(database, reward), Is.EqualTo(discountedCost),
                    "Repeated experiment completion must not compound its discount.");
            });
        });
    }

    /// <summary>A console snapshot from another database cannot bypass the owning server's cost or prerequisites.</summary>
    [TestCase("discount")]
    [TestCase("prerequisite")]
    public async Task ForeignConsoleSnapshotCannotAuthorizePurchase(string forgedReason)
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var target = server.ProtoMan.Index(AdvancedCleaning);
            var budget = forgedReason == "discount" ? (int) Math.Ceiling(target.Cost * 0.75) : target.Cost;
            var purchased = false;
            EntityUid foreignServer = default;

            await server.WaitPost(() =>
            {
                var coordinates = server.EntMan.GetComponent<TransformComponent>(console).Coordinates;
                foreignServer = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer", coordinates);
                server.System<PowerReceiverSystem>().SetNeedsPower(foreignServer, false);
                GrantPrerequisites(pair, foreignServer, target, new HashSet<string>());
                if (forgedReason == "discount")
                    GrantPrerequisites(pair, researchServer, target, new HashSet<string>());
                var points = server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points;
                research.ModifyServerPoints(researchServer, budget - points);
            });
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                CompleteExperiment(pair, foreignServer, "FishMedicalFabrication");
                research.Sync(console, foreignServer);
            });
            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(console);
                Assert.That(research.IsTechnologyAvailable(database, target), Is.True);
                Assert.That(server.EntMan.GetComponent<ResearchClientComponent>(console).Server, Is.EqualTo(researchServer));
                if (forgedReason == "prerequisite")
                    Assert.That(target.TechnologyPrerequisites, Is.Not.Empty);
            });
            await server.WaitPost(() => purchased = research.UnlockTechnology(console, target, user));
            await server.WaitAssertion(() =>
            {
                Assert.That(purchased, Is.False);
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points, Is.EqualTo(budget));
                Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer).UnlockedTechnologies,
                    Does.Not.Contain(AdvancedCleaning));
            });
        });
    }

    /// <summary>Buying an already unlocked reward cannot consume the server's remaining points.</summary>
    [Test]
    public async Task RepeatedRewardPurchaseDoesNotChargeAgain()
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var target = server.ProtoMan.Index(AdvancedCleaning);
            var firstPurchase = false;
            var secondPurchase = false;
            const int budget = 100000;

            await server.WaitPost(() =>
            {
                GrantPrerequisites(pair, researchServer, target, new HashSet<string>());
                CompleteExperiment(pair, researchServer, "FishMedicalFabrication");
                var points = server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points;
                research.ModifyServerPoints(researchServer, budget - points);
                firstPurchase = research.UnlockTechnology(console, target, user);
                secondPurchase = research.UnlockTechnology(console, target, user);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(firstPurchase, Is.True);
                Assert.That(secondPurchase, Is.False);
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points,
                    Is.EqualTo(budget - (int) Math.Ceiling(target.Cost * 0.75)));
            });
        });
    }

    /// <summary>All supported technologies remain reachable through their dependencies, including high tiers of different disciplines.</summary>
    [Test]
    public async Task EverySupportedTechnologyCanBeReachedByPurchasingDependencies()
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            var expected = new HashSet<string>();
            var purchased = new List<string>();
            var rejected = new List<string>();

            await server.WaitPost(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                var supported = new HashSet<ProtoId<TechDisciplinePrototype>>();
                foreach (var discipline in database.SupportedDisciplines)
                    supported.Add(discipline);
                foreach (var technology in server.ProtoMan.EnumeratePrototypes<TechnologyPrototype>())
                {
                    if (!technology.Hidden && supported.Contains(technology.Discipline))
                        expected.Add(technology.ID);
                }
                research.ModifyServerPoints(researchServer, 100000000);

                for (var pass = 0; pass < expected.Count; pass++)
                {
                    var available = research.GetAvailableTechnologies(console);
                    if (available.Count == 0)
                        break;
                    foreach (var technology in available)
                    {
                        if (research.UnlockTechnology(console, technology, user))
                            purchased.Add(technology.ID);
                        else
                            rejected.Add(technology.ID);
                    }
                    if (rejected.Count != 0)
                        break;
                }
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(expected, Is.Not.Empty);
                Assert.That(rejected, Is.Empty);
                Assert.That(purchased, Is.EquivalentTo(expected),
                    "A dependency cycle, a hidden prerequisite, or a discipline lockout must not strand research.");
                Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer).MainDiscipline, Is.Null);
            });
        });
    }

    /// <summary>Purchasing an old technology preserves its own equipment without granting recipes moved to another branch.</summary>
    [TestCase("OptimizedMicrogalvanism", "PowerCellMicroreactor", "PowerCellHyper")]
    [TestCase("MassExcavation", "OreBagOfHolding", "MiningDrillDiamond")]
    [TestCase("AdvancedCleaning", "TrashBagHolding", "AdvMopItem")]
    public async Task LegacyTechnologyPurchaseDoesNotUnlockMovedRecipe(string technologyId, string movedRecipeId, string retainedRecipeId)
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            ProtoId<TechnologyPrototype> technologyProto = technologyId;
            ProtoId<LatheRecipePrototype> movedRecipe = movedRecipeId;
            ProtoId<LatheRecipePrototype> retainedRecipe = retainedRecipeId;
            var technology = server.ProtoMan.Index(technologyProto);
            var purchased = false;
            await server.WaitPost(() =>
            {
                GrantPrerequisites(pair, researchServer, technology, new HashSet<string>());
                research.ModifyServerPoints(researchServer, 100000);
                purchased = research.UnlockTechnology(console, technology, user);
            });
            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                Assert.That(purchased, Is.True);
                Assert.That(database.UnlockedRecipes, Does.Contain(retainedRecipe));
                if (technologyId == "OptimizedMicrogalvanism")
                    Assert.That(database.UnlockedRecipes, Does.Contain((ProtoId<LatheRecipePrototype>) "SMESSuperExtendedMachineCircuitboard"));
                Assert.That(database.UnlockedRecipes, Does.Not.Contain(movedRecipe),
                    "Purchasing the original technology must not bypass the new research branch.");
            });
        });
    }

    /// <summary>Bluespace storage remains blocked until its experimental theory is researched, then grants every holding bag.</summary>
    [Test]
    public async Task BluespaceStoragePurchaseRequiresExperimentalTheoryAndUnlocksEveryHoldingBag()
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            ProtoId<TechnologyPrototype> technologyProto = "BluespaceStorage";
            var technology = server.ProtoMan.Index(technologyProto);
            var blockedPurchase = false;
            var completedPurchase = false;
            await server.WaitPost(() =>
            {
                research.AddTechnology(researchServer, "BluespaceCargoTransport");
                research.AddTechnology(researchServer, "MechanicalCompression");
                research.ModifyServerPoints(researchServer, 100000);
                blockedPurchase = research.UnlockTechnology(console, technology, user);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(blockedPurchase, Is.False,
                    "Cargo transport and compression must not bypass the experimental bluespace theory requirement.");
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points, Is.EqualTo(100000));
            });
            await server.WaitPost(() =>
            {
                research.AddTechnology(researchServer, "FishBluespaceTheory");
                completedPurchase = research.UnlockTechnology(console, technology, user);
            });
            await server.WaitAssertion(() =>
            {
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                Assert.That(completedPurchase, Is.True);
                Assert.That(technology.Discipline, Is.EqualTo((ProtoId<TechDisciplinePrototype>) "Experimental"));
                foreach (var recipe in new[]
                         {
                             "ClothingBackpackHolding", "ClothingBackpackSatchelHolding", "ClothingBackpackDuffelHolding",
                             "PlantBagOfHolding", "OreBagOfHolding", "TrashBagHolding",
                         })
                    Assert.That(database.UnlockedRecipes, Does.Contain((ProtoId<LatheRecipePrototype>) recipe));
            });
        });
    }

    /// <summary>Each new branch rejects an omitted dependency without charging, then accepts the complete dependency set.</summary>
    [TestCase("FishBluespaceTheory", "BasicAnomalousResearch")]
    [TestCase("FishBluespaceTheory", "BasicXenoArcheology")]
    [TestCase("FishUraniumProcessing", "SalvageEquipment")]
    [TestCase("FishUraniumProcessing", "IndustrialEngineering")]
    [TestCase("FishMicroreactors", "AdvancedPowercells")]
    [TestCase("FishMicroreactors", "AdvancedToolsTechnology")]
    [TestCase("FishMicroreactors", "FishUraniumProcessing")]
    public async Task NewResearchPurchaseRequiresEveryDependency(string technologyId, string missingId)
    {
        await WithResearchConsole(async (pair, researchServer, console, user) =>
        {
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            ProtoId<TechnologyPrototype> technologyProto = technologyId;
            var technology = server.ProtoMan.Index(technologyProto);
            var prerequisites = technologyId switch
            {
                "FishBluespaceTheory" => new[] { "BasicAnomalousResearch", "BasicXenoArcheology" },
                "FishUraniumProcessing" => new[] { "SalvageEquipment", "IndustrialEngineering" },
                _ => new[] { "AdvancedPowercells", "AdvancedToolsTechnology", "FishUraniumProcessing" },
            };
            var expectedCost = technologyId == "FishMicroreactors" ? 10000 : 7500;
            var rejected = false;
            var accepted = false;
            await server.WaitPost(() =>
            {
                foreach (var prerequisite in prerequisites)
                {
                    if (prerequisite != missingId)
                        research.AddTechnology(researchServer, prerequisite);
                }
                research.ModifyServerPoints(researchServer, 100000);
                rejected = research.UnlockTechnology(console, technology, user);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(rejected, Is.False, $"{technologyId} must require {missingId}.");
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points, Is.EqualTo(100000));
            });
            await server.WaitPost(() =>
            {
                research.AddTechnology(researchServer, missingId);
                accepted = research.UnlockTechnology(console, technology, user);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(accepted, Is.True);
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(researchServer).Points,
                    Is.EqualTo(100000 - expectedCost));
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(researchServer);
                if (technologyId == "FishMicroreactors")
                    Assert.That(database.UnlockedRecipes, Does.Contain((ProtoId<LatheRecipePrototype>) "PowerCellMicroreactor"));
                if (technologyId == "FishUraniumProcessing")
                    Assert.That(database.UnlockedRecipes, Does.Contain((ProtoId<LatheRecipePrototype>) "FishProcessedUranium"));
            });
        });
    }

    private static void CompleteExperiment(TestPair pair, EntityUid researchServer, string experimentId)
    {
        var experiment = pair.Server.ProtoMan.Index<FishResearchExperimentPrototype>(experimentId);
        for (var sample = 0; sample < experiment.RequiredSamples; sample++)
            pair.Server.System<ServerResearchSystem>().TryRecordFishExperimentSample(researchServer, experiment, $"sample-{sample}");
    }

    private static void GrantPrerequisites(TestPair pair, EntityUid researchServer,
        TechnologyPrototype technology, HashSet<string> granted)
    {
        foreach (var prerequisite in technology.TechnologyPrerequisites)
        {
            if (!granted.Add(prerequisite))
                continue;

            var prototype = pair.Server.ProtoMan.Index(prerequisite);
            GrantPrerequisites(pair, researchServer, prototype, granted);
            pair.Server.System<ServerResearchSystem>().AddTechnology(researchServer, prototype);
        }
    }

    private static async Task WithResearchConsole(Func<TestPair, EntityUid, EntityUid, EntityUid, Task> scenario)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid researchServer = default;
        EntityUid console = default;
        EntityUid user = default;
        var completed = false;

        try
        {
            await server.WaitPost(() =>
            {
                researchServer = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
                server.System<PowerReceiverSystem>().SetNeedsPower(researchServer, false);
                console = server.EntMan.SpawnEntity("ComputerResearchAndDevelopment", map.GridCoords);
                user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
                server.System<ServerResearchSystem>().RegisterClient(console, researchServer);
            });
            await server.WaitRunTicks(1);
            await scenario(pair, researchServer, console, user);
            completed = true;
        }
        finally
        {
            await server.WaitPost(() => server.EntMan.DeleteEntity(map.MapUid));
            if (completed)
                await pair.CleanReturnAsync();
            else
                await pair.DisposeAsync();
        }
    }
}
