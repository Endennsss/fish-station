using System.Linq;
using Content.Client._Fish.Storage;
using Content.Shared._Fish.Storage;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Fish.Storage;

[TestFixture, NonParallelizable]
public sealed class FishOreBagFilterTest
{
    private static readonly EntProtoId OreBagId = "OreBag";
    private static readonly EntProtoId HumanId = "MobHuman";
    private static readonly EntProtoId SteelOreId = "SteelOre1";
    private static readonly EntProtoId GoldOreId = "GoldOre1";
    private static readonly EntProtoId ArtifactFragmentId = "ArtifactFragment1";
    private static readonly ProtoId<StackPrototype> SteelOreStack = "SteelOre";
    private static readonly ProtoId<StackPrototype> NonOreStack = "Steel";

    [Test]
    public async Task OreBagHasMagnetPickupFilter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototype = server.ProtoMan.Index(OreBagId);
            Assert.That(prototype.Components.ContainsKey("FishOreBagFilter"), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("OreBagOfHolding", true)]
    [TestCase("BorgOreBag", true)]
    [TestCase("BorgOreBagOfHolding", true)]
    [TestCase("MagazBagWM", false)]
    public async Task DescendantsInheritFilterAndInterface(string prototypeId, bool enabled)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var ui = entityManager.System<SharedUserInterfaceSystem>();
        EntityUid bag = default;

        await server.WaitPost(() => bag = entityManager.SpawnEntity(prototypeId, MapCoordinates.Nullspace));

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.GetComponent<FishOreBagFilterComponent>(bag).Enabled, Is.EqualTo(enabled));
            Assert.That(ui.HasUi(bag, FishOreBagFilterUiKey.Key), Is.True);
            Assert.That(ui.HasUi(bag, StorageComponent.StorageUiKey.Key), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("OreBag")]
    [TestCase("OreBagOfHolding")]
    public async Task FilterBlocksMagnetStackMergingButAllowsManualInsertion(string prototypeId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var inventory = entityManager.System<InventorySystem>();
        var storage = entityManager.System<SharedStorageSystem>();
        var filter = entityManager.System<FishOreBagFilterSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();
        EntityUid bag = default;
        EntityUid storedOre = default;
        EntityUid defaultOre = default;
        EntityUid blockedOre = default;
        FishOreBagFilterComponent preferences = null!;
        var equipped = false;
        var inserted = false;
        var changed = false;

        await server.WaitPost(() =>
        {
            var user = entityManager.SpawnEntity(HumanId, map.GridCoords);
            bag = entityManager.SpawnEntity(prototypeId, map.GridCoords);
            preferences = entityManager.GetComponent<FishOreBagFilterComponent>(bag);
            storedOre = SpawnOnGround(entityManager, SteelOreId, map.GridCoords);
            inserted = storage.Insert(bag, storedOre, out _, playSound: false);
            equipped = inventory.TryEquip(user, bag, "belt", silent: true);
            defaultOre = SpawnOnGround(entityManager, GoldOreId, map.GridCoords);
            Scan(entityManager, timing, bag);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(equipped, Is.True);
            Assert.That(inserted, Is.True);
            Assert.That(preferences.DisabledStackTypes, Is.Empty);
            Assert.That(preferences.CollectOther, Is.True);
            Assert.That(entityManager.GetComponent<StorageComponent>(bag).Container.ContainedEntities,
                Does.Contain(defaultOre));
        });

        await server.WaitPost(() =>
        {
            changed = filter.TrySetStackEnabled((bag, preferences), SteelOreStack, false);
            blockedOre = SpawnOnGround(entityManager, SteelOreId, map.GridCoords);
            Scan(entityManager, timing, bag);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(entityManager.GetComponent<StackComponent>(storedOre).Count, Is.EqualTo(1));
            Assert.That(entityManager.GetComponent<StackComponent>(blockedOre).Count, Is.EqualTo(1));
            Assert.That(entityManager.GetComponent<StorageComponent>(bag).Container.ContainedEntities,
                Does.Not.Contain(blockedOre));
        });

        await server.WaitPost(() => inserted = storage.Insert(bag, blockedOre, out _, playSound: false));

        await server.WaitAssertion(() =>
        {
            Assert.That(inserted, Is.True);
            Assert.That(entityManager.GetComponent<StackComponent>(storedOre).Count, Is.EqualTo(2));
            Assert.That(preferences.DisabledStackTypes, Does.Contain(SteelOreStack));
        });

        await server.WaitPost(() =>
        {
            changed = filter.TrySetStackEnabled((bag, preferences), SteelOreStack, true);
            SpawnOnGround(entityManager, SteelOreId, map.GridCoords);
            Scan(entityManager, timing, bag);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(entityManager.GetComponent<StackComponent>(storedOre).Count, Is.EqualTo(3));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllAndOtherSettingsControlMagnetPickup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var inventory = entityManager.System<InventorySystem>();
        var filter = entityManager.System<FishOreBagFilterSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();
        EntityUid bag = default;
        EntityUid ore = default;
        EntityUid fragment = default;
        EntityUid other = default;
        FishOreBagFilterComponent preferences = null!;
        var equipped = false;
        var changed = false;

        await server.WaitPost(() =>
        {
            var user = entityManager.SpawnEntity(HumanId, map.GridCoords);
            bag = entityManager.SpawnEntity(OreBagId, map.GridCoords);
            preferences = entityManager.GetComponent<FishOreBagFilterComponent>(bag);
            equipped = inventory.TryEquip(user, bag, "belt", silent: true);
            ore = SpawnOnGround(entityManager, SteelOreId, map.GridCoords);
            fragment = SpawnOnGround(entityManager, ArtifactFragmentId, map.GridCoords);
            other = SpawnOnGround(entityManager, GoldOreId, map.GridCoords);
            entityManager.RemoveComponent<StackComponent>(other);
            changed = filter.TrySetAllEnabled((bag, preferences), false);
            Scan(entityManager, timing, bag);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(equipped, Is.True);
            Assert.That(changed, Is.True);
            Assert.That(filter.GetFilterableStackTypes().Select(stack => stack.ID),
                Does.Contain("SteelOre").And.Contain("ArtifactFragment"));
            Assert.That(entityManager.GetComponent<StorageComponent>(bag).Container.ContainedEntities, Is.Empty);
        });

        await server.WaitPost(() =>
        {
            changed = filter.TrySetOtherEnabled((bag, preferences), true);
            Scan(entityManager, timing, bag);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(entityManager.GetComponent<StorageComponent>(bag).Container.ContainedEntities,
                Is.EquivalentTo(new[] { other }));
        });

        await server.WaitPost(() =>
        {
            changed = filter.TrySetAllEnabled((bag, preferences), true);
            Scan(entityManager, timing, bag);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(preferences.DisabledStackTypes, Is.Empty);
            Assert.That(preferences.CollectOther, Is.True);
            Assert.That(entityManager.GetComponent<StorageComponent>(bag).Container.ContainedEntities,
                Is.EquivalentTo(new[] { ore, fragment, other }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SettingsArePerBagAndRejectUnknownOrNonOreCategories()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var filter = entityManager.System<FishOreBagFilterSystem>();
        ProtoId<StackPrototype> unknownStack = "FishOreBagUnknownStack";
        Entity<FishOreBagFilterComponent> first = default;
        Entity<FishOreBagFilterComponent> second = default;
        EntityUid ore = default;
        var changed = false;
        var unknownAccepted = true;
        var nonOreAccepted = true;

        await server.WaitPost(() =>
        {
            var firstBag = entityManager.SpawnEntity(OreBagId, MapCoordinates.Nullspace);
            var secondBag = entityManager.SpawnEntity(OreBagId, MapCoordinates.Nullspace);
            first = (firstBag, entityManager.GetComponent<FishOreBagFilterComponent>(firstBag));
            second = (secondBag, entityManager.GetComponent<FishOreBagFilterComponent>(secondBag));
            ore = entityManager.SpawnEntity(SteelOreId, MapCoordinates.Nullspace);
            changed = filter.TrySetStackEnabled(first, SteelOreStack, false);
            filter.TrySetOtherEnabled(first, false);
            unknownAccepted = filter.TrySetStackEnabled(first, unknownStack, false);
            nonOreAccepted = filter.TrySetStackEnabled(first, NonOreStack, false);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(unknownAccepted, Is.False);
            Assert.That(nonOreAccepted, Is.False);
            Assert.That(first.Comp.DisabledStackTypes, Is.EquivalentTo(new[] { SteelOreStack }));
            Assert.That(second.Comp.DisabledStackTypes, Is.Empty);
            Assert.That(first.Comp.CollectOther, Is.False);
            Assert.That(second.Comp.CollectOther, Is.True);
            Assert.That(filter.CanCollect(first, ore), Is.False);
            Assert.That(filter.CanCollect(second, ore), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OpenFilterWindowSendsSettingsAndSynchronizesComponentState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entityManager = server.EntMan;
        var ui = entityManager.System<SharedUserInterfaceSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);
        var previousEntity = session.AttachedEntity;
        EntityUid user = default;
        EntityUid bag = default;
        EntityUid clientBag = default;
        NetEntity netBag = default;
        FishOreBagFilterBoundUserInterface bui = null!;
        var equipped = false;
        var opened = false;

        await server.WaitPost(() =>
        {
            user = entityManager.SpawnEntity(HumanId, map.GridCoords);
            bag = entityManager.SpawnEntity(OreBagId, map.GridCoords);
            equipped = entityManager.System<InventorySystem>().TryEquip(user, bag, "belt", silent: true);
            server.PlayerMan.SetAttachedEntity(session, user);
            netBag = entityManager.GetNetEntity(bag);
            opened = ui.TryOpenUi(bag, FishOreBagFilterUiKey.Key, user);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(equipped, Is.True);
            Assert.That(opened, Is.True);
        });

        await pair.RunTicksSync(15);

        await client.WaitAssertion(() =>
        {
            clientBag = client.EntMan.GetEntity(netBag);
            Assert.That(client.System<SharedUserInterfaceSystem>()
                .TryGetOpenUi(clientBag, FishOreBagFilterUiKey.Key, out bui), Is.True);
            var windows = client.ResolveDependency<IUserInterfaceManager>()
                .WindowRoot.Children.OfType<FishOreBagFilterWindow>();
            Assert.That(windows.Single().IsOpen, Is.True);
        });

        await client.WaitPost(() => bui.SendPredictedMessage(new FishOreBagFilterSetStackMessage(SteelOreStack, false)));
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
            Assert.That(entityManager.GetComponent<FishOreBagFilterComponent>(bag).DisabledStackTypes,
                Does.Contain(SteelOreStack)));
        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<FishOreBagFilterComponent>(clientBag).DisabledStackTypes,
                Does.Contain(SteelOreStack)));

        await server.WaitPost(() =>
        {
            ui.CloseUi(bag, FishOreBagFilterUiKey.Key, user);
            server.PlayerMan.SetAttachedEntity(session, previousEntity);
        });
        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    [TestCase("OreBag", 10f)]
    [TestCase("MagazBagWM", 0f)]
    public async Task UnavailableSettingsRejectOpenAndChangeMessages(string prototypeId, float actorOffset)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var filter = entityManager.System<FishOreBagFilterSystem>();
        var map = await pair.CreateTestMap();
        Entity<FishOreBagFilterComponent> bag = default;
        EntityUid user = default;
        BoundUserInterfaceMessageAttempt openAttempt = null!;
        BoundUserInterfaceMessageAttempt changeAttempt = null!;

        await server.WaitPost(() =>
        {
            var bagUid = entityManager.SpawnEntity(prototypeId, map.GridCoords);
            bag = (bagUid, entityManager.GetComponent<FishOreBagFilterComponent>(bagUid));
            user = entityManager.SpawnEntity(HumanId, new EntityCoordinates(map.Grid, actorOffset, 0));
            openAttempt = new BoundUserInterfaceMessageAttempt(user, bag, FishOreBagFilterUiKey.Key,
                new OpenBoundInterfaceMessage());
            changeAttempt = new BoundUserInterfaceMessageAttempt(user, bag, FishOreBagFilterUiKey.Key,
                new FishOreBagFilterSetAllMessage(false));
            entityManager.EventBus.RaiseEvent(EventSource.Local, openAttempt);
            entityManager.EventBus.RaiseEvent(EventSource.Local, changeAttempt);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(filter.CanAccessSettings(bag, user), Is.False);
            Assert.That(openAttempt.Cancelled, Is.True);
            Assert.That(changeAttempt.Cancelled, Is.True);
            Assert.That(bag.Comp.DisabledStackTypes, Is.Empty);
            Assert.That(bag.Comp.CollectOther, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnOnGround(IEntityManager entityManager, EntProtoId prototype, EntityCoordinates coordinates)
    {
        var item = entityManager.SpawnEntity(prototype, coordinates);
        var physics = entityManager.GetComponent<PhysicsComponent>(item);
        entityManager.System<SharedPhysicsSystem>().SetBodyStatus(item, physics, BodyStatus.OnGround);
        return item;
    }

    private static void Scan(IEntityManager entityManager, IGameTiming timing, EntityUid bag)
    {
        entityManager.GetComponent<MagnetPickupComponent>(bag).NextScan = timing.CurTime;
        entityManager.System<MagnetPickupSystem>().Update(0f);
    }
}
