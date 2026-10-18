using System.Linq;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Shared._Sunrise.GhostTheme;
using Content.Shared.Body;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Gibbing;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Fish.Ghost;

[TestFixture, NonParallelizable]
public sealed class GhostHeadAppearanceTest
{
    [TestCase("ClothingUniformJumpsuitBartender", null, "#ffffff", false)]
    [TestCase("ClothingUniformJumpsuitColorRed", null, "#eb0c07", false)]
    [TestCase("ClothingUniformJumpsuitBartender", "folded", "#ffffff", false)]
    [TestCase("ClothingUniformJumpsuitBartender", null, "#ffffff", true)]
    [TestCase("ClothingUniformJumpsuitChameleon", null, "#ffffff", false)]
    public async Task ClassicGhostKeepsWornOutfitWithSingleOpacity(string uniformPrototype, string prefix, string color, bool gibbed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        EntityUid uniform = default;
        EntityUid ghost = default;
        NetEntity ghostNet = default;
        var equipped = false;
        var uniformState = prefix == null ? "equipped-INNERCLOTHING" : "folded-equipped-INNERCLOTHING";
        var uniformColor = Color.FromHex(color);
        var uniformRsi = uniformPrototype == "ClothingUniformJumpsuitColorRed"
            ? "Clothing/Uniforms/Jumpsuit/color.rsi"
            : "Clothing/Uniforms/Jumpsuit/bartender.rsi";

        await server.WaitPost(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            uniform = entities.SpawnEntity(uniformPrototype, map.GridCoords);
            if (uniformPrototype == "ClothingUniformJumpsuitChameleon")
                entities.System<SharedChameleonClothingSystem>().SetSelectedPrototype(uniform, "ClothingUniformJumpsuitBartender");
            entities.System<ClothingSystem>().SetEquippedPrefix(uniform, prefix);
            var hat = entities.SpawnEntity("ClothingHeadHatTophat", map.GridCoords);
            var inventory = entities.System<InventorySystem>();
            equipped = inventory.TryEquip(body, uniform, "jumpsuit", true, true) &&
                       inventory.TryEquip(body, hat, "head", true, true);

            // Проверяем зависимые черты головы, как у видов с дредами или покрытием морды.
            var organs = entities.GetComponent<BodyComponent>(body).Organs!.ContainedEntities;
            var head = organs.First(organ => entities.TryGetComponent<VisualOrganComponent>(organ, out var visual) &&
                                           visual.Layer is HumanoidVisualLayers.Head);
            var headMarkings = entities.GetComponent<VisualOrganMarkingsComponent>(head);
            headMarkings.Markings[HumanoidVisualLayers.Hair] = [new Marking("HumanHairAfro", [Color.White])];
            headMarkings.Markings[HumanoidVisualLayers.HeadTop] = [new Marking("CatEars", [Color.White, Color.White])];
            headMarkings.DependentHidingLayers[HumanoidVisualLayers.Hair] = [HumanoidVisualLayers.HeadTop];
            entities.Dirty(head, headMarkings);
            entities.System<SharedHideableHumanoidLayersSystem>().SetLayerOcclusion((body, null),
                HumanoidVisualLayers.Hair, true, SlotFlags.HEAD);

            var minds = entities.System<MindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Ghost outfit test");
            minds.TransferTo(mind, body, mind: mind.Comp);
            if (gibbed)
                entities.System<GibbingSystem>().Gib(body);
            ghost = entities.System<GhostSystem>().SpawnGhost(mind.AsNullable(), map.GridCoords)!.Value;
            var theme = entities.EnsureComponent<GhostThemeComponent>(ghost);
            theme.GhostTheme = "FishClassic";
            entities.Dirty(ghost, theme);
            ghostNet = entities.GetNetEntity(ghost);
        });
        await server.WaitAssertion(() => Assert.That(equipped, Is.True));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => AssertOutfit(client.EntMan, ghostNet, uniformState, uniformColor, uniformRsi));

        await server.WaitPost(() =>
        {
            entities.System<ClothingSystem>().SetEquippedPrefix(uniform, prefix == null ? "folded" : null);
            if (entities.EntityExists(body))
                entities.DeleteEntity(body);
            if (entities.EntityExists(uniform))
                entities.DeleteEntity(uniform);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => AssertOutfit(client.EntMan, ghostNet, uniformState, uniformColor, uniformRsi));
        await server.WaitPost(() =>
        {
            var theme = entities.GetComponent<GhostThemeComponent>(ghost);
            theme.GhostTheme = "Default";
            entities.Dirty(ghost, theme);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var sprite = client.EntMan.GetComponent<SpriteComponent>(client.EntMan.GetEntity(ghostNet));
            Assert.That(sprite.AllLayers.Any(layer => layer.RsiState.ToString() == uniformState), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertOutfit(IEntityManager entities, NetEntity ghost, string uniformState, Color uniformColor, string uniformRsi)
    {
        var sprite = entities.GetComponent<SpriteComponent>(entities.GetEntity(ghost));
        var layers = sprite.AllLayers.ToArray();
        var uniform = layers.SingleOrDefault(layer => layer.Visible && layer.RsiState.ToString() == uniformState);
        var hat = layers.SingleOrDefault(layer => layer.Visible && layer.RsiState.ToString() == "equipped-HELMET");
        Assert.That(uniform, Is.Not.Null, "Worn uniform must be part of the ghost snapshot.");
        Assert.That(hat, Is.Not.Null, "Worn headgear must be part of the ghost snapshot.");
        Assert.That(layers.Any(layer => layer.Visible && layer.RsiState.ToString() is "afro" or "ears_cat_outer"), Is.False,
            "Headgear occlusion must include dependent head features.");
        Assert.That(Array.IndexOf(layers, hat), Is.GreaterThan(Array.IndexOf(layers, uniform)));
        Assert.That(uniform!.Color, Is.EqualTo(uniformColor));
        Assert.That(uniform.ActualRsi?.Path.ToString(), Does.EndWith(uniformRsi));
        Assert.That(sprite.Color.A * uniform!.Color.A, Is.EqualTo(8f / 15f).Within(0.001f));
        Assert.That(sprite.Color.A * hat!.Color.A, Is.EqualTo(8f / 15f).Within(0.001f));
    }

    public enum GhostCreation
    {
        Permanent,
        Returnable,
        MindTransfer,
        Gibbed,
    }

    [TestCase(GhostCreation.Permanent)]
    [TestCase(GhostCreation.Returnable)]
    [TestCase(GhostCreation.MindTransfer)]
    [TestCase(GhostCreation.Gibbed)]
    public async Task ClassicGhostRetainsHeadTraitsAfterBodyDeletion(GhostCreation creation)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        EntityUid ghost = default;
        NetEntity ghostNet = default;
        var hairColor = Color.FromHex("#b05020");

        await server.WaitPost(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var head = entities.GetComponent<BodyComponent>(body).Organs!.ContainedEntities.First(organ =>
                entities.TryGetComponent<VisualOrganComponent>(organ, out var visual) &&
                visual.Layer is HumanoidVisualLayers.Head);
            var markings = entities.GetComponent<VisualOrganMarkingsComponent>(head);
            markings.Markings = new()
            {
                [HumanoidVisualLayers.Hair] = [new Marking("HumanHairAfro", [hairColor])],
                [HumanoidVisualLayers.FacialHair] = [new Marking("HumanFacialHairAbe", [Color.Brown])],
                // Проверяем фильтрацию даже для некорректной маркировки тела в наборе головы.
                [HumanoidVisualLayers.Tail] = [new Marking("CatTail", [Color.White])],
            };
            entities.Dirty(head, markings);
            // Старые кошачьи уши хранятся в Special у торса, хотя визуально относятся к голове.
            var torso = entities.GetComponent<BodyComponent>(body).Organs!.ContainedEntities.First(organ =>
                entities.TryGetComponent<VisualOrganComponent>(organ, out var visual) &&
                visual.Layer is HumanoidVisualLayers.Chest);
            var torsoMarkings = entities.GetComponent<VisualOrganMarkingsComponent>(torso);
            torsoMarkings.Markings[HumanoidVisualLayers.Special] = [new Marking("CatEars", [Color.Orange, Color.White])];
            entities.Dirty(torso, torsoMarkings);

            var minds = entities.System<MindSystem>();
            var mind = minds.CreateMind(pair.Player!.UserId, "Ghost appearance test");
            minds.TransferTo(mind, body, mind: mind.Comp);
            if (creation == GhostCreation.MindTransfer)
            {
                minds.TransferTo(mind, null, mind: mind.Comp);
                ghost = mind.Comp.OwnedEntity!.Value;
            }
            else
            {
                if (creation == GhostCreation.Gibbed)
                    entities.System<GibbingSystem>().Gib(body);

                ghost = entities.System<GhostSystem>().SpawnGhost(mind.AsNullable(), map.GridCoords,
                    creation == GhostCreation.Returnable)!.Value;
            }
            server.PlayerMan.SetAttachedEntity(pair.Player!, ghost);
            var theme = entities.EnsureComponent<GhostThemeComponent>(ghost);
            theme.GhostTheme = "FishClassic";
            entities.Dirty(ghost, theme);
            ghostNet = entities.GetNetEntity(ghost);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() => AssertHeadTraits(client.EntMan, ghostNet, hairColor));

        await server.WaitPost(() =>
        {
            if (entities.EntityExists(body))
                entities.DeleteEntity(body);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => AssertHeadTraits(client.EntMan, ghostNet, hairColor));

        await server.WaitPost(() =>
        {
            var theme = entities.GetComponent<GhostThemeComponent>(ghost);
            theme.GhostTheme = "Default";
            entities.Dirty(ghost, theme);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var sprite = client.EntMan.GetComponent<SpriteComponent>(client.EntMan.GetEntity(ghostNet));
            Assert.That(sprite.AllLayers.Count(layer => layer.RsiState.ToString() == "afro"), Is.Zero);
        });

        await server.WaitPost(() =>
        {
            var theme = entities.GetComponent<GhostThemeComponent>(ghost);
            theme.GhostTheme = "FishClassic";
            entities.Dirty(ghost, theme);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => AssertHeadTraits(client.EntMan, ghostNet, hairColor));
        await pair.CleanReturnAsync();
    }

    private static void AssertHeadTraits(IEntityManager entities, NetEntity ghost, Color hairColor)
    {
        var sprite = entities.GetComponent<SpriteComponent>(entities.GetEntity(ghost));
        var layers = sprite.AllLayers.ToArray();
        var hair = layers.SingleOrDefault(layer => layer.Visible && layer.RsiState.ToString() == "afro");
        Assert.That(hair, Is.Not.Null, "Classic ghost must display the deceased character's hair.");
        ProtoId<GhostThemePrototype> themeId = "FishClassic";
        var theme = IoCManager.Resolve<IPrototypeManager>().Index(themeId);
        Assert.That(hair!.Color, Is.EqualTo(hairColor * theme.SpriteColor));
        Assert.That(sprite.Color.A * theme.SpriteColor.A, Is.EqualTo(8f / 15f).Within(0.001f));
        Assert.That(layers.Count(layer => layer.Visible && layer.RsiState.ToString() == "abe"), Is.EqualTo(1));
        Assert.That(layers.Count(layer => layer.Visible && layer.RsiState.ToString() == "ears_cat_outer"), Is.EqualTo(1));
        Assert.That(layers.Count(layer => layer.Visible && layer.RsiState.ToString() == "tail_cat_wag"), Is.Zero);
    }
}
