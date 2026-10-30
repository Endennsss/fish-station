using System.Collections.Generic;
using System.Linq;
using Content.Client._Fish.Research.Bluespace;
using Content.IntegrationTests.Pair;
using Content.Shared._Fish.Research.Bluespace;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._Fish.Research;

/// <summary>Loads the device XAML and checks server snapshot presentation on the headless client thread.</summary>
[TestFixture, NonParallelizable]
public sealed class FishBluespaceDeviceUiTest
{
    /// <summary>Each device exposes its relevant localized controls without turning snapshots into operating requests.</summary>
    [TestCase(FishBluespaceDeviceKind.Harvester)]
    [TestCase(FishBluespaceDeviceKind.Prober)]
    [TestCase(FishBluespaceDeviceKind.Suppressor)]
    public async Task DeviceSnapshotsUpdateLocalizedControlsWithoutSendingRequests(FishBluespaceDeviceKind kind)
    {
        await WithWindow(async (pair, window) =>
        {
            var client = pair.Client;
            var loc = client.ResolveDependency<ILocalizationManager>();
            var requests = 0;
            await client.WaitPost(() =>
            {
                window.EnabledRequested += _ => requests++;
                window.PowerModeRequested += _ => requests++;
                window.LootCategoryRequested += _ => requests++;
                window.UpdateState(State(kind, enabled: false, mode: FishBluespacePowerMode.Normal,
                    category: FishBluespaceLootCategory.Materials));
                window.UpdateState(State(kind, mode: FishBluespacePowerMode.Overdrive,
                    category: FishBluespaceLootCategory.Research));
            });
            await client.WaitAssertion(() =>
            {
                var nameKey = $"fish-bluespace-device-{kind.ToString().ToLowerInvariant()}";
                var instructionsKey = $"fish-bluespace-instructions-{kind.ToString().ToLowerInvariant()}";
                Assert.That(loc.HasString(nameKey) && loc.HasString(instructionsKey), Is.True);
                Assert.That(window.Title, Is.EqualTo(loc.GetString(nameKey)));
                Assert.That(window.FindControl<Label>("DeviceName").Text, Is.EqualTo(loc.GetString(nameKey)));
                Assert.That(window.FindControl<RichTextLabel>("Instructions").Text, Is.EqualTo(loc.GetString(instructionsKey)));
                Assert.That(window.FindControl<Label>("Status").Text, Is.EqualTo(loc.GetString("fish-bluespace-status-working")));
                Assert.That(window.FindControl<BoxContainer>("HarvesterPanel").Visible,
                    Is.EqualTo(kind == FishBluespaceDeviceKind.Harvester));
                Assert.That(window.FindControl<Label>("ResearchRate").Visible,
                    Is.EqualTo(kind == FishBluespaceDeviceKind.Prober));
                Assert.That(window.FindControl<Label>("ResearchRate").Text,
                    Is.EqualTo(loc.GetString("fish-bluespace-research-rate", ("rate", 120f))));
                Assert.That(window.FindControl<ProgressBar>("Background").Value, Is.EqualTo(42.5f));
                Assert.That(window.FindControl<ProgressBar>("Progress").Value, Is.EqualTo(0.625f));
                Assert.That(window.FindControl<Label>("PowerText").Text,
                    Is.EqualTo(loc.GetString("fish-bluespace-power-value", ("received", 1000f), ("requested", 1000f))));
                var enable = window.FindControl<Button>("EnableButton");
                Assert.That(enable.Text, Is.EqualTo(loc.GetString("fish-bluespace-disable")));
                Assert.That(enable.Disabled, Is.False);
                var mode = window.FindControl<OptionButton>("PowerMode");
                var category = window.FindControl<OptionButton>("LootCategory");
                Assert.That(mode.SelectedId, Is.EqualTo((int) FishBluespacePowerMode.Overdrive));
                Assert.That(category.SelectedId, Is.EqualTo((int) FishBluespaceLootCategory.Research));
                Assert.That(Descendants(mode).OfType<Label>().Any(label =>
                    label.Text?.Trim() == loc.GetString("fish-bluespace-mode-overdrive")), Is.True);
                Assert.That(Descendants(category).OfType<Label>().Any(label =>
                    label.Text?.Trim() == loc.GetString("fish-bluespace-category-research")), Is.True);
                Assert.That(requests, Is.Zero, "Applying authoritative snapshots must never request machine changes.");
            });
        });
    }

    /// <summary>A prober without a linked server explains that prerequisite before its secondary lack of working power.</summary>
    [Test]
    public async Task EnabledProberWithoutServerShowsMissingServerInsteadOfMissingPower()
    {
        await WithWindow(async (pair, window) =>
        {
            var client = pair.Client;
            var loc = client.ResolveDependency<ILocalizationManager>();
            await client.WaitPost(() => window.UpdateState(State(FishBluespaceDeviceKind.Prober,
                powered: false, hasResearchServer: false)));
            await client.WaitAssertion(() =>
            {
                var status = window.FindControl<Label>("Status").Text;
                Assert.That(status, Is.EqualTo(loc.GetString("fish-bluespace-status-no-server")));
                Assert.That(status, Is.Not.EqualTo(loc.GetString("fish-bluespace-status-no-power")));
                var enable = window.FindControl<Button>("EnableButton");
                Assert.That(enable.Text, Is.EqualTo(loc.GetString("fish-bluespace-disable")));
                Assert.That(enable.Disabled, Is.False, "The operator must still be able to switch off the waiting prober.");
            });
        });
    }

    private static FishBluespaceDeviceBoundUserInterfaceState State(FishBluespaceDeviceKind kind,
        bool enabled = true, bool powered = true, bool hasResearchServer = true,
        FishBluespacePowerMode mode = FishBluespacePowerMode.Normal,
        FishBluespaceLootCategory category = FishBluespaceLootCategory.Materials)
    {
        return new FishBluespaceDeviceBoundUserInterfaceState(kind, enabled, powered, anchored: true,
            hasStation: true, hasResearchServer: hasResearchServer, critical: false, background: 42.5f,
            receivedPower: 1_000_000f, requestedPower: 1_000_000f, progress: 0.625f, pointsPerMinute: 120f,
            powerMode: mode, lootCategory: category);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static async Task WithWindow(Func<TestPair, FishBluespaceDeviceWindow, Task> scenario)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        FishBluespaceDeviceWindow window = null!;
        try
        {
            // Fish-edit: окно проверяется без GUI и сетевого сеанса, все операции выполняются на клиентском потоке.
            await pair.Client.WaitPost(() => window = new FishBluespaceDeviceWindow());
            await scenario(pair, window);
        }
        finally
        {
            await pair.Client.WaitPost(() => window?.Orphan());
        }
        await pair.CleanReturnAsync();
    }
}
