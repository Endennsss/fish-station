using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Fish.Research;
using Content.Server.NPC.HTN;
using Content.Server.Power.EntitySystems;
using Content.Shared._Fish.Research.Components;
using Content.Shared._Fish.Xenobiology;
using Content.Shared.Anomaly;
using Content.Shared.Anomaly.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Components;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using ServerResearchSystem = Content.Server.Research.Systems.ResearchSystem;

namespace Content.IntegrationTests.Tests._Fish.Research;

[TestFixture, NonParallelizable]
public sealed class FishResearchExperimentsTest
{
    /// <summary>The validated BUI path requires the scanner to remain held for opening and server selection.</summary>
    [Test]
    public async Task ScannerServerSelectionBuiRejectsMessagesWhileNotHeld()
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            var ui = server.System<UserInterfaceSystem>();
            var key = ResearchClientUiKey.Key;
            EntityUid secondServer = default;
            var opened = false;
            await server.WaitPost(() =>
            {
                secondServer = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer",
                    server.EntMan.GetComponent<TransformComponent>(context.ResearchServer).Coordinates);
                server.System<PowerReceiverSystem>().SetNeedsPower(secondServer, false);
                opened = ui.TryOpenUi(context.Scanner, key, context.User);
                var id = server.EntMan.GetComponent<ResearchServerComponent>(secondServer).Id;
                ui.RaiseUiMessage(context.Scanner, key,
                    new ResearchClientServerSelectedMessage(id) { Actor = context.User });
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(opened, Is.True, "The scanner's server selection must open while held.");
                Assert.That(ui.IsUiOpen(context.Scanner, key, context.User), Is.True);
                Assert.That(server.EntMan.GetComponent<ResearchClientComponent>(context.Scanner).Server,
                    Is.EqualTo(secondServer), "A valid held BUI selection must reach the research system.");
            });

            EntityUid? afterDeselect = default;
            EntityUid? afterSelect = default;
            var dropped = false;
            var openAfterClose = true;
            var openedFromFloor = true;
            await server.WaitPost(() =>
            {
                dropped = server.System<SharedHandsSystem>().TryDrop((context.User, null), context.Scanner);
                openedFromFloor = ui.TryOpenUi(context.Scanner, key, context.User);
                afterDeselect = server.EntMan.GetComponent<ResearchClientComponent>(context.Scanner).Server;
                afterSelect = afterDeselect;
                if (openedFromFloor)
                {
                    // Fish-edit: после настоящего повторного открытия проверяем сообщения с легально созданным Actor.
                    ui.RaiseUiMessage(context.Scanner, key,
                        new ResearchClientServerDeselectedMessage { Actor = context.User });
                    afterDeselect = server.EntMan.GetComponent<ResearchClientComponent>(context.Scanner).Server;
                    var id = server.EntMan.GetComponent<ResearchServerComponent>(context.ResearchServer).Id;
                    ui.RaiseUiMessage(context.Scanner, key,
                        new ResearchClientServerSelectedMessage(id) { Actor = context.User });
                    afterSelect = server.EntMan.GetComponent<ResearchClientComponent>(context.Scanner).Server;
                }
                ui.RaiseUiMessage(context.Scanner, key, new CloseBoundInterfaceMessage { Actor = context.User });
                openAfterClose = ui.IsUiOpen(context.Scanner, key, context.User);
                ui.CloseUi(context.Scanner, key, context.User);
            });
            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(dropped, Is.True);
                    Assert.That(openedFromFloor, Is.False,
                        "A direct OpenBoundInterfaceMessage must not open server selection on a scanner lying nearby.");
                    Assert.That(afterDeselect, Is.EqualTo(secondServer),
                        "Reopening a dropped scanner must not authorize a forged server deselection.");
                    Assert.That(afterSelect, Is.EqualTo(secondServer),
                        "Reopening a dropped scanner must not authorize a forged server selection.");
                    Assert.That(openAfterClose, Is.False, "Closing a dropped scanner's interface must remain allowed.");
                });
            });

            var pickedUp = false;
            await server.WaitPost(() =>
            {
                pickedUp = server.System<SharedHandsSystem>().TryPickupAnyHand(context.User, context.Scanner);
                opened = ui.TryOpenUi(context.Scanner, key, context.User);
                var id = server.EntMan.GetComponent<ResearchServerComponent>(context.ResearchServer).Id;
                ui.RaiseUiMessage(context.Scanner, key,
                    new ResearchClientServerSelectedMessage(id) { Actor = context.User });
                ui.CloseUi(context.Scanner, key, context.User);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(pickedUp, Is.True);
                Assert.That(opened, Is.True, "Picking the scanner up must restore access to server selection.");
                Assert.That(server.EntMan.GetComponent<ResearchClientComponent>(context.Scanner).Server,
                    Is.EqualTo(context.ResearchServer));
            });
        });
    }

    /// <summary>A new scanner cannot collect samples until someone explicitly selects a server.</summary>
    [Test]
    public async Task NewlyCreatedScannerRequiresExplicitServerSelection()
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            EntityUid target = default;
            var equipped = false;
            var started = true;
            await server.WaitPost(() =>
            {
                server.System<SharedHandsSystem>().TryDrop((context.User, null), context.Scanner);
                context.Scanner = server.EntMan.SpawnEntity("FishResearchScanner", context.Coordinates);
                equipped = server.System<SharedHandsSystem>().TryPickupAnyHand(context.User, context.Scanner);
                target = SpawnSample(context, "PowerCellSmall");
                started = StartScan(context, target);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(equipped, Is.True);
                Assert.That(server.EntMan.GetComponent<ResearchClientComponent>(context.Scanner).Server, Is.Null,
                    "A nearby server must not silently claim a newly created portable scanner.");
                Assert.That(server.EntMan.GetComponent<ResearchServerComponent>(context.ResearchServer).Clients,
                    Does.Not.Contain(context.Scanner));
                Assert.That(started, Is.False);
            });
            await context.Pair.RunSeconds(2.3f);
            await AssertProgress(context, "FishPowerCellStudy", 0);

            await server.WaitPost(() => server.System<ServerResearchSystem>()
                .RegisterClient(context.Scanner, context.ResearchServer));
            await Scan(context, target);
            await AssertProgress(context, "FishPowerCellStudy", 1);
        });
    }

    /// <summary>The real held-item interaction can credit an explicitly paired server from another grid.</summary>
    [Test]
    public async Task HeldScannerInteractionCompletesAFieldMeasurementAwayFromItsServer()
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            EntityUid target = default;
            await server.WaitPost(() =>
            {
                var map = server.EntMan.GetComponent<TransformComponent>(context.User).MapUid!.Value;
                var transform = server.System<SharedTransformSystem>();
                transform.SetCoordinates(context.User, new EntityCoordinates(map, 50f, 50f));
                target = server.EntMan.SpawnEntity("PowerCellSmall", new EntityCoordinates(map, 51f, 50f));
            });
            await server.WaitRunTicks(1);
            await server.WaitPost(() => server.System<SharedInteractionSystem>()
                .UserInteraction(context.User, server.EntMan.GetComponent<TransformComponent>(target).Coordinates, target));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.GetComponent<TransformComponent>(context.User).GridUid, Is.Null);
                Assert.That(server.EntMan.GetComponent<DoAfterComponent>(context.User).DoAfters.Values
                    .Any(doAfter => !doAfter.Cancelled && !doAfter.Completed), Is.True);
            });
            await context.Pair.RunSeconds(2.3f);
            await AssertProgress(context, "FishPowerCellStudy", 1);
        });
    }

    /// <summary>Novelty follows the scientific sample rather than its entity or the scanner used.</summary>
    [TestCase("FishPowerCellStudy", "PowerCellSmall", "PowerCellMedium", "PowerCellHigh")]
    [TestCase("FishBiologicalSurvey", "MobHuman", "MobReptilian", "MobMoth")]
    [TestCase("FishAnomalySurvey", "AnomalyPyroclastic", "AnomalyIce", "AnomalyBluespace")]
    [TestCase("FishSlimeDiversity", "FishXenoSlimeGrey", "FishXenoSlimeBlue", "FishXenoSlimePurple")]
    public async Task ScannerCountsDistinctSampleTypesAndSharesNoveltyAcrossScanners(
        string experimentId, string first, string second, string third)
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            EntityUid target = default;
            await server.WaitPost(() => target = SpawnSample(context, first));
            await Scan(context, target);
            await AssertProgress(context, experimentId, 1);
            await Scan(context, target, false);
            await AssertProgress(context, experimentId, 1);

            await server.WaitPost(() =>
            {
                var hands = server.System<SharedHandsSystem>();
                hands.TryDrop((context.User, null), context.Scanner);
                var replacement = server.EntMan.SpawnEntity("FishResearchScanner", context.Coordinates);
                server.System<ServerResearchSystem>().RegisterClient(replacement, context.ResearchServer);
                hands.TryPickupAnyHand(context.User, replacement);
                context.Scanner = replacement;
                server.EntMan.DeleteEntity(target);
                target = SpawnSample(context, first);
            });
            await Scan(context, target, false);
            await AssertProgress(context, experimentId, 1);
            foreach (var prototype in new[] { second, third })
            {
                await server.WaitPost(() =>
                {
                    server.EntMan.DeleteEntity(target);
                    target = SpawnSample(context, prototype);
                });
                await Scan(context, target);
            }
            await AssertProgress(context, experimentId, 3);
        });
    }

    /// <summary>Only cells with a positive capacity and at least ninety percent charge qualify.</summary>
    [TestCase(100f, 89f, 0)]
    [TestCase(100f, 90f, 1)]
    [TestCase(0f, 0f, 0)]
    public async Task CellScanChecksActualChargeAndPositiveCapacity(float capacity, float charge, int expected)
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            EntityUid target = default;
            await server.WaitPost(() =>
            {
                target = SpawnSample(context, "PowerCellSmall");
                var battery = server.System<SharedBatterySystem>();
                battery.SetMaxCharge((target, null), capacity);
                battery.SetCharge((target, null), charge);
            });
            await Scan(context, target, expected != 0);
            await AssertProgress(context, "FishPowerCellStudy", expected);
        });
    }

    [Test]
    public async Task DeadHumanoidDoesNotCountAsBiologicalSample()
    {
        await WithExperimentLab(async context =>
        {
            EntityUid target = default;
            var server = context.Pair.Server;
            await server.WaitPost(() =>
            {
                target = SpawnSample(context, "MobHuman");
                server.System<MobStateSystem>().ChangeMobState(target, MobState.Dead);
            });
            await Scan(context, target, false);
            await AssertProgress(context, "FishBiologicalSurvey", 0);
        });
    }

    /// <summary>Advanced colony surveys require a living adult of at least the configured mutation tier.</summary>
    [TestCase("FishXenoSlimeGreyAdult", false, 0)]
    [TestCase("FishXenoSlimeDarkBlue", false, 0)]
    [TestCase("FishXenoSlimeDarkBlueAdult", false, 1)]
    [TestCase("FishXenoSlimeDarkBlueAdult", true, 0)]
    public async Task XenobiologySurveyChecksAgeTierAndLife(string prototype, bool dead, int expected)
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            EntityUid target = default;
            await server.WaitPost(() =>
            {
                target = SpawnSample(context, prototype);
                if (dead)
                    server.System<MobStateSystem>().ChangeMobState(target, MobState.Dead);
            });
            await Scan(context, target, false);
            await AssertProgress(context, "FishSlimeAdvancedLineages", expected);
        });
    }

    /// <summary>The controlled-anomaly objective requires every configured safety threshold.</summary>
    [TestCase(0.5f, 0.15f, 0.5f, 1)]
    [TestCase(0.5f, 0.5f, 0.5f, 1)]
    [TestCase(0.49f, 0.3f, 1f, 0)]
    [TestCase(0.6f, 0.1f, 1f, 0)]
    [TestCase(0.6f, 0.51f, 1f, 0)]
    [TestCase(0.6f, 0.3f, 0.49f, 0)]
    public async Task ControlledAnomalyChecksEveryThreshold(float severity, float stability, float health, int expected)
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            EntityUid target = default;
            await server.WaitPost(() =>
            {
                target = SpawnSample(context, "AnomalyBluespace");
                var anomaly = server.EntMan.GetComponent<AnomalyComponent>(target);
                var system = server.System<SharedAnomalySystem>();
                system.ChangeAnomalySeverity(target, severity - anomaly.Severity);
                system.ChangeAnomalyStability(target, stability - anomaly.Stability);
                system.ChangeAnomalyHealth(target, health - anomaly.Health);
            });
            await Scan(context, target);
            await AssertProgress(context, "FishControlledAnomaly", expected);
        });
    }

    /// <summary>Artifact scans require real unlocked nodes and count each artifact only once.</summary>
    [Test]
    public async Task ArtifactStudyCountsTwoDistinctArtifactsWithThreeUnlockedNodesEach()
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            EntityUid artifact = default;
            await server.WaitPost(() =>
            {
                artifact = SpawnSample(context, "ComplexXenoArtifact");
                UnlockArtifactNodes(context, artifact, 2);
            });
            await Scan(context, artifact, false);
            await AssertProgress(context, "FishArtifactStudy", 0);
            await server.WaitPost(() => UnlockArtifactNodes(context, artifact, 3));
            await Scan(context, artifact);
            await AssertProgress(context, "FishArtifactStudy", 1);
            await Scan(context, artifact, false);
            await AssertProgress(context, "FishArtifactStudy", 1);
            await server.WaitPost(() =>
            {
                artifact = SpawnSample(context, "ComplexXenoArtifact");
                UnlockArtifactNodes(context, artifact, 3);
            });
            await Scan(context, artifact);
            await AssertProgress(context, "FishArtifactStudy", 2);
        });
    }

    /// <summary>Interrupted scans never award partial progress.</summary>
    [TestCase("cancel")]
    [TestCase("drop")]
    [TestCase("target-away")]
    [TestCase("target-deleted")]
    [TestCase("offline")]
    [TestCase("discharged")]
    public async Task ScanRevalidatesTheCompletedDoAfter(string interruption)
    {
        await WithExperimentLab(async context =>
        {
            var pair = context.Pair;
            var server = pair.Server;
            EntityUid target = default;
            var started = false;
            await server.WaitPost(() =>
            {
                target = SpawnSample(context, "PowerCellSmall");
                started = StartScan(context, target);
            });
            await server.WaitAssertion(() => Assert.That(started, Is.True));
            await pair.RunSeconds(0.2f);
            await AssertProgress(context, "FishPowerCellStudy", 0);
            await server.WaitPost(() =>
            {
                switch (interruption)
                {
                    case "cancel":
                        var doAfters = server.EntMan.GetComponent<DoAfterComponent>(context.User);
                        foreach (var id in doAfters.DoAfters.Keys.ToArray())
                            server.System<SharedDoAfterSystem>().Cancel(context.User, id);
                        break;
                    case "drop":
                        server.System<SharedHandsSystem>().TryDrop((context.User, null), context.Scanner);
                        break;
                    case "target-away":
                        server.System<SharedTransformSystem>().SetCoordinates(target,
                            new EntityCoordinates(context.Coordinates.EntityId, new Vector2(20, 20)));
                        break;
                    case "target-deleted":
                        server.EntMan.DeleteEntity(target);
                        break;
                    case "offline":
                        var power = server.System<PowerReceiverSystem>();
                        power.SetNeedsPower(context.ResearchServer, true);
                        power.SetPowerDisabled(context.ResearchServer, true);
                        break;
                    case "discharged":
                        server.System<SharedBatterySystem>().SetCharge((target, null), 0f);
                        break;
                }
            });
            await pair.RunSeconds(2.3f);
            await AssertProgress(context, "FishPowerCellStudy", 0);
        });
    }

    /// <summary>A server captured at the start cannot be replaced by a newly selected server.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangingScannerServerDuringScanNeverCreditsTheReplacement(bool switchBack)
    {
        await WithExperimentLab(async context =>
        {
            var pair = context.Pair;
            var server = pair.Server;
            var research = server.System<ServerResearchSystem>();
            EntityUid independent = default;
            var started = false;
            await server.WaitPost(() =>
            {
                independent = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer",
                    server.EntMan.GetComponent<TransformComponent>(context.ResearchServer).Coordinates);
                server.System<PowerReceiverSystem>().SetNeedsPower(independent, false);
                started = StartScan(context, SpawnSample(context, "PowerCellSmall"));
            });
            await server.WaitAssertion(() => Assert.That(started, Is.True));
            await pair.RunSeconds(0.2f);
            await server.WaitPost(() =>
            {
                research.UnregisterClient(context.Scanner);
                research.RegisterClient(context.Scanner, independent);
                if (switchBack)
                {
                    research.UnregisterClient(context.Scanner);
                    research.RegisterClient(context.Scanner, context.ResearchServer);
                }
            });
            await pair.RunSeconds(2.3f);
            await AssertProgress(context, "FishPowerCellStudy", switchBack ? 1 : 0);
            await server.WaitAssertion(() => Assert.That(server.EntMan.GetComponent<TechnologyDatabaseComponent>(independent)
                .FishExperimentProgress.GetValueOrDefault("FishPowerCellStudy"), Is.Zero));
        });
    }

    [TestCase("unpaired")]
    [TestCase("unregistered")]
    [TestCase("not-held")]
    [TestCase("out-of-range")]
    public async Task ScannerRejectsMissingOwnershipPairingOrReach(string invalidState)
    {
        await WithExperimentLab(async context =>
        {
            var server = context.Pair.Server;
            var started = true;
            await server.WaitPost(() =>
            {
                var target = SpawnSample(context, "PowerCellSmall");
                switch (invalidState)
                {
                    case "unpaired":
                        server.System<ServerResearchSystem>().UnregisterClient(context.Scanner);
                        break;
                    case "unregistered":
                        server.EntMan.GetComponent<ResearchServerComponent>(context.ResearchServer).Clients.Remove(context.Scanner);
                        break;
                    case "not-held":
                        server.System<SharedHandsSystem>().TryDrop((context.User, null), context.Scanner);
                        break;
                    case "out-of-range":
                        server.System<SharedTransformSystem>().SetCoordinates(target,
                            new EntityCoordinates(context.Coordinates.EntityId, new Vector2(20, 20)));
                        break;
                }
                started = StartScan(context, target);
            });
            await server.WaitAssertion(() => Assert.That(started, Is.False));
            await AssertProgress(context, "FishPowerCellStudy", 0);
        });
    }

    private static EntityUid SpawnSample(ExperimentContext context, string prototype)
    {
        var server = context.Pair.Server;
        var target = server.EntMan.SpawnEntity(prototype, context.Coordinates);
        if (server.EntMan.HasComponent<FishXenobiologySlimeComponent>(target) &&
            server.EntMan.TryGetComponent<HTNComponent>(target, out var htn))
            server.System<HTNSystem>().SetHTNEnabled((target, htn), false);
        if (server.EntMan.HasComponent<HumanoidProfileComponent>(target))
            server.System<SharedGodmodeSystem>().EnableGodmode(target);
        if (server.EntMan.TryGetComponent<BatteryComponent>(target, out var battery))
            server.System<SharedBatterySystem>().SetCharge((target, battery), battery.MaxCharge);
        if (server.EntMan.TryGetComponent<AnomalyComponent>(target, out var anomaly))
        {
            server.System<SharedAnomalySystem>().ChangeAnomalySeverity(target, -anomaly.Severity);
            server.System<SharedAnomalySystem>().ChangeAnomalyStability(target, 0.3f - anomaly.Stability);
        }
        return target;
    }

    private static void UnlockArtifactNodes(ExperimentContext context, EntityUid artifact, int count)
    {
        var server = context.Pair.Server;
        var component = server.EntMan.GetComponent<XenoArtifactComponent>(artifact);
        var system = server.System<SharedXenoArtifactSystem>();
        foreach (var node in system.GetAllNodes((artifact, component)).Take(count))
            system.SetNodeUnlocked((artifact, component), node);
    }

    private static bool StartScan(ExperimentContext context, EntityUid target)
    {
        var server = context.Pair.Server;
        return server.System<FishResearchScannerSystem>().TryScan(
            (context.Scanner, server.EntMan.GetComponent<FishResearchScannerComponent>(context.Scanner)),
            context.User, target, quiet: true);
    }

    private static async Task Scan(ExperimentContext context, EntityUid target, bool requireStart = true)
    {
        var started = false;
        await context.Pair.Server.WaitPost(() => started = StartScan(context, target));
        if (requireStart)
            await context.Pair.Server.WaitAssertion(() => Assert.That(started, Is.True));
        // Fish-edit: проходим настоящий двухсекундный DoAfter с небольшим запасом на завершающий тик.
        await context.Pair.RunSeconds(2.3f);
    }

    private static Task AssertProgress(ExperimentContext context, string experiment, int expected) =>
        context.Pair.Server.WaitAssertion(() => Assert.That(
            context.Pair.Server.EntMan.GetComponent<TechnologyDatabaseComponent>(context.ResearchServer)
                .FishExperimentProgress.GetValueOrDefault(experiment), Is.EqualTo(expected)));

    private static async Task WithExperimentLab(Func<ExperimentContext, Task> scenario)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid researchServer = default;
        EntityUid user = default;
        EntityUid scanner = default;
        var equipped = false;
        var completed = false;
        try
        {
            await server.WaitPost(() =>
            {
                var mapSystem = server.System<SharedMapSystem>();
                for (var x = 0; x < 5; x++)
                for (var y = 0; y < 4; y++)
                    mapSystem.SetTile(map.Grid, new EntityCoordinates(map.GridCoords.EntityId, x, y), map.Tile.Tile);
                researchServer = server.EntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
                server.System<PowerReceiverSystem>().SetNeedsPower(researchServer, false);
                var userCoordinates = new EntityCoordinates(map.GridCoords.EntityId, 1.5f, 1.5f);
                user = server.EntMan.SpawnEntity("MobHuman", userCoordinates);
                server.System<SharedGodmodeSystem>().EnableGodmode(user);
                scanner = server.EntMan.SpawnEntity("FishResearchScanner", userCoordinates);
                server.System<ServerResearchSystem>().RegisterClient(scanner, researchServer);
                equipped = server.System<SharedHandsSystem>().TryPickupAnyHand(user, scanner);
            });
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(equipped, Is.True);
                Assert.That(server.System<ServerResearchSystem>().CanCollectFishExperimentSamples(researchServer), Is.True);
            });
            await scenario(new ExperimentContext(pair, researchServer, user, scanner,
                new EntityCoordinates(map.GridCoords.EntityId, 2.75f, 1.5f)));
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

    private sealed class ExperimentContext(TestPair pair, EntityUid researchServer, EntityUid user,
        EntityUid scanner, EntityCoordinates coordinates)
    {
        public TestPair Pair { get; } = pair;
        public EntityUid ResearchServer { get; } = researchServer;
        public EntityUid User { get; } = user;
        public EntityUid Scanner { get; set; } = scanner;
        public EntityCoordinates Coordinates { get; } = coordinates;
    }
}
