using Content.Client._Fish.Shuttles.UI;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._Fish.Shuttles;

public sealed class MassScannerDisplayTest : InteractionTest
{
    [Test]
    public async Task ShuttleNavigationUsesScannerPanel()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new ShuttleConsoleWindow();
            var nav = window.FindControl<NavScreen>("NavContainer");
            Assert.That(nav.FindControl<FishRadarPanel>("Scanner").RadarScreen.MassScannerDisplay, Is.True);
        });
    }

    [Test]
    public async Task FiltersSurviveRadarUpdates()
    {
        await SpawnTarget("ComputerRadar");
        await Server.WaitAssertion(() => Assert.That(
            SUiSys.TryOpenUi(STarget!.Value, RadarConsoleUiKey.Key, SPlayer), Is.True));
        await RunTicks(5);
        Assert.That(IsUiOpen(RadarConsoleUiKey.Key), Is.True);

        var window = GetWindow<RadarConsoleWindow>();
        var panel = window.FindControl<FishRadarPanel>("Scanner");
        var radar = panel.RadarScreen;
        Assert.That(radar.MassScannerDisplay, Is.True);

        Assert.That(radar.ShowIFF, Is.True);
        await ClickControl(panel.FindControl<Button>("Labels"));

        await Client.WaitAssertion(() =>
        {
            window.UpdateState(new NavInterfaceState(64, null, null, new()));
            Assert.That(radar.ShowIFF, Is.False);
        });
    }
}
