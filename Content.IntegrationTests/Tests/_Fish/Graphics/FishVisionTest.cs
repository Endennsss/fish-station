using Content.Client._Fish.Graphics.Vision;
using Content.Server.Temperature.Systems;
using Content.Shared._Fish.Vision;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Content.Shared.Temperature.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Fish.Graphics;

[TestFixture]
public sealed class FishVisionTest
{
    [TestCase(true, "eyes")]
    [TestCase(false, "head")]
    public async Task TwoActiveNightDevicesKeepRemainingSource(bool headFirst, string removedSlot)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid wearer = default;
        EntityUid eyes = default;
        EntityUid head = default;
        await server.WaitPost(() =>
        {
            wearer = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            eyes = server.EntMan.SpawnEntity("ClothingEyesNVDSyndicate", map.GridCoords);
            head = server.EntMan.SpawnEntity("ClothingEyesNVDHandcrafted", map.GridCoords);
            server.System<InventorySystem>().TryEquip(wearer, eyes, "eyes", force: true);
            server.System<InventorySystem>().TryEquip(wearer, head, "head", force: true);
            server.System<ItemToggleSystem>().TryActivate(headFirst ? head : eyes, wearer);
            server.System<ItemToggleSystem>().TryActivate(headFirst ? eyes : head, wearer);
        });
        await server.WaitAssertion(() => Assert.That(
            server.EntMan.GetComponent<FishNightVisionComponent>(wearer).Color, Is.EqualTo(Color.FromHex("#FF1020"))));
        await server.WaitPost(() => server.System<InventorySystem>().TryUnequip(wearer, removedSlot, force: true));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.TryGetComponent<FishNightVisionComponent>(wearer, out var remaining), Is.True);
            Assert.That(remaining.Color, Is.EqualTo(Color.FromHex(removedSlot == "eyes" ? "#FFDD33" : "#FF1020")));
        });
        await server.WaitPost(() => server.EntMan.DeleteEntity(removedSlot == "eyes" ? head : eyes));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(server.EntMan.HasComponent<FishNightVisionComponent>(wearer), Is.False));
        await pair.CleanReturnAsync();
    }

    [TestCase("ClothingEyesNVD", "eyes", null, false)]
    [TestCase("ClothingEyesNVDSec", "eyes", null, false)]
    [TestCase("ClothingEyesNVDMedSec", "eyes", null, false)]
    [TestCase("ClothingEyesNVDSyndicate", "eyes", "#FF1020", false)]
    [TestCase("ClothingEyesGlassesNVG", "eyes", "#FF1020", false)]
    [TestCase("ClothingEyesNVDHandcrafted", "head", "#FFDD33", false)]
    [TestCase("SunriseClothingEyesGlassesThermal", "eyes", null, true)]
    [TestCase("ClothingEyesGlassesThermalChameleon", "eyes", null, true)]
    public async Task ExistingGogglesKeepTogglePowerAndEquipmentLifecycle(string prototype, string slot, string? expectedColor, bool thermal)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);
        EntityUid wearer = default;
        EntityUid goggles = default;
        var equipped = false;
        var activated = false;
        await server.WaitPost(() =>
        {
            wearer = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            goggles = server.EntMan.SpawnEntity(prototype, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(session, wearer);
            equipped = server.System<InventorySystem>().TryEquip(wearer, goggles, slot, force: true);
        });
        Assert.That(equipped, Is.True);
        await Check(false);
        await server.WaitPost(() => activated = server.System<ItemToggleSystem>().TryActivate(goggles, wearer));
        Assert.That(activated, Is.True);
        await Check(true);

        await server.WaitPost(() => server.System<InventorySystem>().TryUnequip(wearer, slot, force: true));
        await Check(false);
        await server.WaitPost(() =>
        {
            server.System<InventorySystem>().TryEquip(wearer, goggles, slot, force: true);
            activated = server.System<ItemToggleSystem>().TryActivate(goggles, wearer);
        });
        Assert.That(activated, Is.True);
        await Check(true);
        await server.WaitPost(() =>
        {
            if (server.System<PowerCellSystem>().TryGetBatteryFromSlot(goggles, out var battery))
                server.System<SharedBatterySystem>().SetCharge(battery.Value.AsNullable(), 0f);
        });
        await Check(false);
        await server.WaitPost(() => activated = server.System<ItemToggleSystem>().TryActivate(goggles, wearer));
        Assert.That(activated, Is.False);
        await pair.CleanReturnAsync();

        async Task Check(bool expected)
        {
            await pair.RunTicksSync(20);
            await server.WaitAssertion(() =>
            {
                Assert.That(thermal ? server.EntMan.HasComponent<FishThermalVisionComponent>(wearer) :
                    server.EntMan.HasComponent<FishNightVisionComponent>(wearer), Is.EqualTo(expected));
            });
            await client.WaitAssertion(() =>
            {
                var vision = client.System<FishVisionSystem>();
                Assert.That(thermal ? vision.ThermalViewer != null : vision.NightPresentation.Source != null,
                    Is.EqualTo(expected));
                if (expected && !thermal)
                {
                    var color = vision.NightPresentation.Color;
                    if (expectedColor != null)
                        Assert.That(color, Is.EqualTo(Color.FromHex(expectedColor)));
                    else
                        Assert.That(color.G, Is.GreaterThan(color.R));
                }
            });
        }
    }

    [Test]
    public async Task TemperatureSurvivesDeathChangesWithCoolingAndDoesNotLoadDistantTargets()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);
        EntityUid target = default;
        EntityUid far = default;
        NetEntity targetNet = default;
        NetEntity farNet = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Robust.Shared.CVars.NetPVS, true);
            var wearer = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            target = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 2, 0));
            far = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1000, 0));
            targetNet = server.EntMan.GetNetEntity(target);
            farNet = server.EntMan.GetNetEntity(far);
            server.PlayerMan.SetAttachedEntity(session, wearer);
            server.EntMan.AddComponent<FishThermalVisionComponent>(wearer);
            server.System<MobStateSystem>().ChangeMobState(target, MobState.Dead);
            // Изолируем передачу температуры от охлаждения тела атмосферой тестовой карты.
            server.EntMan.GetComponent<TemperatureComponent>(target).AtmosTemperatureTransferEfficiency = 0f;
            server.System<TemperatureSystem>().ForceChangeTemperature(target, 310.15f);
        });
        await CheckTemperature(310f);
        await server.WaitPost(() => server.System<TemperatureSystem>().ForceChangeTemperature(target, 273.15f));
        await CheckTemperature(273f);
        await server.WaitPost(() => server.EntMan.RemoveComponent<TemperatureComponent>(target));
        await pair.RunTicksSync(30);
        await client.WaitAssertion(() => Assert.That(
            client.EntMan.HasComponent<FishThermalSignatureComponent>(client.EntMan.GetEntity(targetNet)), Is.False));
        await pair.CleanReturnAsync();

        async Task CheckTemperature(float expected)
        {
            await pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.GetComponent<FishThermalSignatureComponent>(target).Temperature,
                    Is.EqualTo(expected).Within(2f));
                Assert.That(server.EntMan.HasComponent<FishThermalSignatureComponent>(far), Is.False);
            });
            await client.WaitAssertion(() =>
            {
                Assert.That(client.EntMan.GetComponent<FishThermalSignatureComponent>(client.EntMan.GetEntity(targetNet)).Temperature,
                    Is.EqualTo(expected).Within(2f));
                Assert.That(client.EntMan.TryGetEntity(farNet, out _), Is.False);
            });
        }
    }
}
