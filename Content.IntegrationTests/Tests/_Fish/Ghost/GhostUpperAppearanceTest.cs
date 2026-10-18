using System.Linq;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Shared._Fish.Ghost;
using Content.Shared._Sunrise.GhostTheme;
using Content.Shared.Body;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Fish.Ghost;

[TestFixture, NonParallelizable]
public sealed class GhostUpperAppearanceTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LivingGhostKeepsOnlyUpperAppearance(bool admin)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        EntityUid ghost = default;
        NetEntity ghostNet = default;
        var equipped = false;

        await server.WaitPost(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var head = entities.GetComponent<BodyComponent>(body).Organs!.ContainedEntities.First(organ =>
                entities.TryGetComponent<VisualOrganComponent>(organ, out var visual) && visual.Layer is HumanoidVisualLayers.Head);
            var markings = entities.GetComponent<VisualOrganMarkingsComponent>(head);
            markings.Markings[HumanoidVisualLayers.Hair] = [new Marking("HumanHairAfro", [Color.Brown])];
            entities.Dirty(head, markings);
            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitBartender", map.GridCoords);
            var boots = entities.SpawnEntity("ClothingShoesBootsJack", map.GridCoords);
            var pants = entities.SpawnEntity("ClothingPantsManBoxers", map.GridCoords);
            var inventory = entities.System<InventorySystem>();
            equipped = inventory.TryEquip(body, uniform, "jumpsuit", true, true) &&
                       inventory.TryEquip(body, boots, "shoes", true, true) &&
                       inventory.TryEquip(body, pants, "pants", true, true);
            var minds = entities.System<MindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Living ghost appearance test");
            minds.TransferTo(mind, body, mind: mind.Comp);

            if (admin)
                server.ResolveDependency<IConsoleHost>().ExecuteCommand(null, $"aghost {pair.Player.Name}");
            else
                entities.System<GhostSystem>().OnGhostAttempt(mind, true, viaCommand: true, mind: mind.Comp);

            ghost = pair.Player.AttachedEntity!.Value;
            var theme = entities.EnsureComponent<GhostThemeComponent>(ghost);
            theme.GhostTheme = "FishClassic";
            entities.Dirty(ghost, theme);
            ghostNet = entities.GetNetEntity(ghost);
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(equipped, Is.True);
            Assert.That(entities.HasComponent<GhostComponent>(ghost), Is.True);
            Assert.That(entities.GetComponent<GhostComponent>(ghost).CanGhostInteract, Is.EqualTo(admin));
            Assert.That(entities.System<MobStateSystem>().IsAlive(body), Is.True);
            Assert.That(entities.TryGetComponent<GhostAppearanceComponent>(ghost, out var appearance), Is.True,
                "A living character's ghost and aghost must receive their appearance snapshot.");
            Assert.That(appearance!.Clothing.Any(item => item.Slot is "shoes" or "socks" or "pants"), Is.False);
        });
        await client.WaitAssertion(() =>
        {
            var sprite = client.EntMan.GetComponent<SpriteComponent>(client.EntMan.GetEntity(ghostNet));
            var layers = sprite.AllLayers.ToArray();
            Assert.That(layers.Any(layer => layer.Visible && layer.RsiState.ToString() == "afro"), Is.True);
            Assert.That(layers.Any(layer => layer.Visible && layer.RsiState.ToString() == "equipped-FEET"), Is.False);
            var uniform = layers.Single(layer => layer.RsiState.ToString() == "equipped-INNERCLOTHING");
            Assert.That(((SpriteComponent.Layer) uniform).ShaderPrototype?.Id, Is.EqualTo("FishGhostUpperClothing"),
                "A full uniform must be clipped above the legs.");
        });
        await pair.CleanReturnAsync();
    }
}
