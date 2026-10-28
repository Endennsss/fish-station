using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Server.Power.EntitySystems;
using Content.Shared._Fish.Research.Components;
using Content.Shared._Fish.Research.Prototypes;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using ServerResearchSystem = Content.Server.Research.Systems.ResearchSystem;

namespace Content.IntegrationTests.Tests._Fish.Research;

[TestFixture, NonParallelizable]
public sealed class FishResearchProductionTest
{
    private static readonly ProtoId<LatheRecipePrototype> ProcessingRecipe = "FishProcessedUranium";
    private static readonly ProtoId<LatheRecipePrototype> ScalpelRecipe = "Scalpel";
    private static readonly ProtoId<LatheRecipePrototype> ScannerRecipe = "FishResearchScanner";

    /// <summary>A fresh protolathe prints the scanner without research and retains its original static recipes.</summary>
    [Test]
    public async Task FreshProtolathePrintsScannerWithoutResearchAndPreservesStaticRecipes()
    {
        await WithLathe("Protolathe", async context =>
        {
            var server = context.Pair.Server;
            var lathe = server.System<LatheSystem>();
            List<ProtoId<LatheRecipePrototype>> available = [];
            var inserted = false;
            var queued = false;
            var started = false;
            await server.WaitPost(() =>
            {
                available = lathe.GetAvailableRecipes(context.Lathe,
                    server.EntMan.GetComponent<LatheComponent>(context.Lathe));
                inserted = InsertRecipeMaterials(context, ScannerRecipe, 1);
                queued = lathe.TryAddToQueue(context.Lathe, server.ProtoMan.Index(ScannerRecipe), 1);
                started = lathe.TryStartProducing(context.Lathe);
                if (started)
                    lathe.FinishProducing(context.Lathe);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(context.ResearchServer)
                    .UnlockedTechnologies, Is.Empty);
                Assert.That(available, Does.Contain(ScannerRecipe));
                foreach (var recipe in new[] { "ProtoSheetOfficePaper", "Beaker", "Scalpel" })
                    Assert.That(available, Does.Contain((ProtoId<LatheRecipePrototype>) recipe),
                        "Adding the scanner must preserve the existing paper, chemistry and surgery recipes.");
                Assert.That(inserted && queued && started, Is.True);

                var map = server.EntMan.GetComponent<TransformComponent>(context.Lathe).MapUid;
                var printed = new List<EntityUid>();
                var query = server.EntMan.EntityQueryEnumerator<FishResearchScannerComponent, MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var metadata, out var transform))
                {
                    if (metadata.EntityPrototype?.ID == "FishResearchScanner" && transform.MapUid == map)
                        printed.Add(uid);
                }
                Assert.That(printed, Has.Count.EqualTo(1),
                    "The unresearched recipe must create a usable physical experimental scanner.");
            });
        });
    }

    /// <summary>Queueing does not count, and identical manufactured recipes cannot replace variety.</summary>
    [TestCase("Protolathe", "FishMedicalFabrication", "Scalpel", "Hemostat", "Cautery")]
    [TestCase("ExosuitFabricator", "FishRoboticsFabrication", "CyborgEndoskeleton", "TorsoBorg", "LightHeadBorg")]
    public async Task ManufacturingCreditsOnlyDistinctCompletedRecipes(string machine, string experimentId,
        string first, string second, string third)
    {
        await WithLathe(machine, async context =>
        {
            var server = context.Pair.Server;
            var lathe = server.System<LatheSystem>();
            var queued = false;
            var started = false;
            var inserted = false;
            var diagnostics = string.Empty;
            await server.WaitPost(() =>
            {
                GrantRecipe(context, first);
                inserted = InsertRecipeMaterials(context, first, 2);
                diagnostics = DescribeProduction(context, first);
                queued = lathe.TryAddToQueue(context.Lathe, server.ProtoMan.Index<LatheRecipePrototype>(first), 2);
                started = lathe.TryStartProducing(context.Lathe);
            });
            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(inserted, Is.True, $"Material insertion failed: {diagnostics}");
                    Assert.That(queued, Is.True, $"Queue rejected the recipe: {diagnostics}");
                    Assert.That(started, Is.True, $"Production did not start: {diagnostics}");
                });
                AssertProgress(context, experimentId, 0);
            });
            await server.WaitPost(() => lathe.FinishProducing(context.Lathe));
            await server.WaitAssertion(() => AssertProgress(context, experimentId, 1));
            await server.WaitPost(() => lathe.FinishProducing(context.Lathe));
            await server.WaitAssertion(() => AssertProgress(context, experimentId, 1));
            await server.WaitRunTicks(1);

            foreach (var recipe in new[] { second, third })
            {
                await server.WaitPost(() =>
                {
                    GrantRecipe(context, recipe);
                    inserted = InsertRecipeMaterials(context, recipe, 1);
                    diagnostics = DescribeProduction(context, recipe);
                    queued = lathe.TryAddToQueue(context.Lathe, server.ProtoMan.Index<LatheRecipePrototype>(recipe), 1);
                    started = lathe.TryStartProducing(context.Lathe);
                    if (started)
                        lathe.FinishProducing(context.Lathe);
                });
                await server.WaitAssertion(() => Assert.Multiple(() =>
                {
                    Assert.That(inserted, Is.True, $"Material insertion failed: {diagnostics}");
                    Assert.That(queued, Is.True, $"Queue rejected the recipe: {diagnostics}");
                    Assert.That(started, Is.True, $"Production did not start: {diagnostics}");
                }));
                await server.WaitRunTicks(1);
            }
            await server.WaitAssertion(() =>
            {
                AssertProgress(context, experimentId, 3);
                var experiment = server.ProtoMan.Index<FishResearchExperimentPrototype>(experimentId);
                var reward = server.ProtoMan.Index(experiment.RewardTechnology);
                var database = server.EntMan.GetComponent<TechnologyDatabaseComponent>(context.ResearchServer);
                Assert.That(server.System<ServerResearchSystem>().GetTechnologyCost(database, reward),
                    Is.EqualTo((int) Math.Ceiling(reward.Cost * 0.75)));
            });
        });
    }

    /// <summary>Only five newly printed fuel units count, even when output merges with an existing stack.</summary>
    [Test]
    public async Task ProcessingCountsNewUnitsBeforeStackMergeAndStopsAtFive()
    {
        await WithLathe("Protolathe", async context =>
        {
            var server = context.Pair.Server;
            var lathe = server.System<LatheSystem>();
            var inserted = false;
            var queued = false;
            var started = false;
            await server.WaitPost(() =>
            {
                server.EntMan.SpawnEntity("FishSheetProcessedUranium1", context.Coordinates);
                GrantRecipe(context, "FishProcessedUranium");
                inserted = InsertRecipeMaterials(context, "FishProcessedUranium", 6);
                queued = lathe.TryAddToQueue(context.Lathe,
                    server.ProtoMan.Index(ProcessingRecipe), 6);
                started = lathe.TryStartProducing(context.Lathe);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(inserted && queued && started, Is.True);
                AssertProgress(context, "FishUraniumProcessingExperiment", 0);
            });

            for (var printed = 1; printed <= 6; printed++)
            {
                await server.WaitPost(() => lathe.FinishProducing(context.Lathe));
                await server.WaitRunTicks(1);
                await server.WaitAssertion(() =>
                {
                    AssertProgress(context, "FishUraniumProcessingExperiment", Math.Min(printed, 5));
                    var stacks = OutputStacks(context);
                    Assert.That(stacks.Sum(stack => stack.Count), Is.EqualTo(printed + 1));
                    if (printed > 1)
                        Assert.That(stacks.Count, Is.LessThan(printed + 1),
                            "The scenario must exercise output merging, including the already existing fuel unit.");
                });
            }
        });
    }

    /// <summary>Cancellation and loss of power leave progress untouched until production actually resumes and finishes.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task InterruptedProductionDoesNotCreditItsReservedRecipe(bool powerLoss)
    {
        await WithLathe("Protolathe", async context =>
        {
            var server = context.Pair.Server;
            var lathe = server.System<LatheSystem>();
            var queued = false;
            var started = false;
            await server.WaitPost(() =>
            {
                GrantRecipe(context, "FishProcessedUranium");
                InsertRecipeMaterials(context, "FishProcessedUranium", 1);
                queued = lathe.TryAddToQueue(context.Lathe,
                    server.ProtoMan.Index(ProcessingRecipe), 1);
                started = lathe.TryStartProducing(context.Lathe);
                if (powerLoss)
                {
                    server.System<PowerReceiverSystem>().SetNeedsPower(context.Lathe, true);
                    server.System<PowerReceiverSystem>().SetPowerDisabled(context.Lathe, true);
                }
                else
                    lathe.AbortProduction(context.Lathe);
            });
            await context.Pair.RunSeconds(0.3f);
            await server.WaitAssertion(() =>
            {
                Assert.That(queued && started, Is.True);
                AssertProgress(context, "FishUraniumProcessingExperiment", 0);
                Assert.That(OutputStacks(context), Is.Empty);
                Assert.That(server.EntMan.GetComponent<LatheComponent>(context.Lathe).CurrentRecipe, Is.Null);
            });
            await server.WaitPost(() =>
            {
                var power = server.System<PowerReceiverSystem>();
                power.SetPowerDisabled(context.Lathe, false);
                power.SetNeedsPower(context.Lathe, false);
            });
            await server.WaitRunTicks(1);
            ProtoId<LatheRecipePrototype>? resumedRecipe = null;
            await server.WaitPost(() =>
            {
                var component = server.EntMan.GetComponent<LatheComponent>(context.Lathe);
                // Fish-edit: восстановление питания уже могло запустить сохранённую очередь через PowerChangedEvent.
                if (component.CurrentRecipe == null)
                    lathe.TryStartProducing(context.Lathe);
                resumedRecipe = component.CurrentRecipe;
                if (resumedRecipe == ProcessingRecipe)
                    lathe.FinishProducing(context.Lathe);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(resumedRecipe, Is.EqualTo(ProcessingRecipe),
                    "Restored power or an explicit restart must resume the interrupted recipe.");
                AssertProgress(context, "FishUraniumProcessingExperiment", 1);
            });
        });
    }

    /// <summary>Only the explicitly selected server receives output; printing without a selection gives no progress.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ProductionCreditsOnlyItsSelectedServer(bool unpaired)
    {
        await WithLathe("Protolathe", async context =>
        {
            var server = context.Pair.Server;
            var research = server.System<ServerResearchSystem>();
            var lathe = server.System<LatheSystem>();
            EntityUid replacement = default;
            var started = false;
            await server.WaitPost(() =>
            {
                replacement = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer", context.Coordinates);
                server.System<PowerReceiverSystem>().SetNeedsPower(replacement, false);
                GrantRecipe(context, "Scalpel");
                InsertRecipeMaterials(context, "Scalpel", 1);
                lathe.TryAddToQueue(context.Lathe, server.ProtoMan.Index(ScalpelRecipe), 1);
                started = lathe.TryStartProducing(context.Lathe);
                research.UnregisterClient(context.Lathe);
                if (!unpaired)
                    research.RegisterClient(context.Lathe, replacement);
            });
            await server.WaitRunTicks(1);
            await server.WaitPost(() => lathe.FinishProducing(context.Lathe));
            await server.WaitAssertion(() =>
            {
                Assert.That(started, Is.True);
                AssertProgress(context, "FishMedicalFabrication", 0);
                Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(replacement)
                    .FishExperimentProgress.GetValueOrDefault("FishMedicalFabrication"), Is.EqualTo(unpaired ? 0 : 1));
            });
        });
    }

    private static void GrantRecipe(LatheContext context, string recipe)
    {
        var server = context.Pair.Server;
        foreach (var technology in server.ProtoMan.EnumeratePrototypes<TechnologyPrototype>())
        {
            if (technology.RecipeUnlocks.Contains((ProtoId<LatheRecipePrototype>) recipe))
                server.System<ServerResearchSystem>().AddTechnology(context.ResearchServer, technology);
        }
    }

    private static bool InsertRecipeMaterials(LatheContext context, string recipe, int quantity)
    {
        var server = context.Pair.Server;
        var materials = server.System<MaterialStorageSystem>();
        var inserted = true;
        foreach (var (material, amount) in server.ProtoMan.Index<LatheRecipePrototype>(recipe).Materials)
        {
            var materialPrototype = server.ProtoMan.Index(material);
            if (materialPrototype.StackEntity is not { } stackEntity ||
                !server.ProtoMan.Index(stackEntity).TryComp<PhysicalCompositionComponent>(out var composition,
                    server.EntMan.ComponentFactory))
                return false;
            var perSheet = composition.MaterialComposition.GetValueOrDefault(material);
            if (perSheet <= 0)
                return false;
            // Fish-edit: физические листы неделимы, поэтому для стоимости 250 нужны три листа по 100.
            var supplied = (int) Math.Ceiling((double) amount * quantity / perSheet) * perSheet;
            var sheets = materials.SpawnMultipleFromMaterial(supplied, materialPrototype, context.Coordinates);
            inserted &= sheets.Count != 0;
            foreach (var sheet in sheets)
                inserted &= materials.TryInsertMaterialEntity(context.User, sheet, context.Lathe);
        }
        return inserted;
    }

    private static string DescribeProduction(LatheContext context, string recipe)
    {
        var server = context.Pair.Server;
        var component = server.EntMan.GetComponent<LatheComponent>(context.Lathe);
        var lathe = server.System<LatheSystem>();
        var materials = server.System<MaterialStorageSystem>();
        var costs = server.ProtoMan.Index<LatheRecipePrototype>(recipe).Materials.Select(material =>
            $"{material.Key}: stored={materials.GetMaterialAmount(context.Lathe, material.Key)}, baseCost={material.Value}");
        return $"recipe={recipe}, available={lathe.GetAvailableRecipes(context.Lathe, component).Contains(recipe)}, " +
               $"powered={server.System<PowerReceiverSystem>().IsPowered(context.Lathe)}, " +
               $"current={component.CurrentRecipe}, queuedBatches={component.Queue.Count}; {string.Join(", ", costs)}";
    }

    private static List<StackComponent> OutputStacks(LatheContext context)
    {
        var results = new List<StackComponent>();
        var entMan = context.Pair.Server.EntMan;
        var map = entMan.GetComponent<TransformComponent>(context.Lathe).MapUid;
        var query = entMan.EntityQueryEnumerator<StackComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out var stack, out var metadata, out var transform))
        {
            if (metadata.EntityPrototype?.ID == "FishSheetProcessedUranium1" && transform.MapUid == map)
                results.Add(stack);
        }
        return results;
    }

    private static void AssertProgress(LatheContext context, string experiment, int expected) =>
        Assert.That(context.Pair.Server.EntMan.GetComponent<TechnologyDatabaseComponent>(context.ResearchServer)
            .FishExperimentProgress.GetValueOrDefault(experiment), Is.EqualTo(expected));

    private static async Task WithLathe(string prototype, Func<LatheContext, Task> scenario)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid researchServer = default;
        EntityUid lathe = default;
        EntityUid user = default;
        var completed = false;
        try
        {
            await server.WaitPost(() =>
            {
                researchServer = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
                lathe = server.EntMan.SpawnEntity(prototype, map.GridCoords);
                user = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
                var power = server.System<PowerReceiverSystem>();
                power.SetNeedsPower(researchServer, false);
                power.SetNeedsPower(lathe, false);
                server.System<ServerResearchSystem>().RegisterClient(lathe, researchServer);
            });
            await server.WaitRunTicks(1);
            await scenario(new LatheContext(pair, researchServer, lathe, user, map.GridCoords));
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

    private sealed record LatheContext(TestPair Pair, EntityUid ResearchServer, EntityUid Lathe, EntityUid User,
        EntityCoordinates Coordinates);
}
