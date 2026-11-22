using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Shuttles.BUIStates;

namespace Content.IntegrationTests.Tests._Fish.Shuttles;

public sealed class MassScannerDisplayTest : InteractionTest
{
    [Test]
    public async Task FiltersSurviveRadarUpdates()
    {
        await SpawnTarget("ComputerRadar");
        await Server.WaitAssertion(() => Assert.That(
            SUiSys.TryOpenUi(STarget!.Value, RadarConsoleUiKey.Key, SPlayer), Is.True));
        await RunTicks(5);
        Assert.That(IsUiOpen(RadarConsoleUiKey.Key), Is.True);

        var window = GetWindow<RadarConsoleWindow>();
        var radar = window.FindControl<ShuttleNavControl>("RadarScreen");
        Assert.That(radar.MassScannerDisplay, Is.True);

        await ClickControl<RadarConsoleWindow>("Sweep");
        Assert.That(radar.ShowSweep, Is.False);
        Assert.That(radar.ShowIFF, Is.True);
        await ClickControl<RadarConsoleWindow>("Labels");

        await Client.WaitAssertion(() =>
        {
            window.UpdateState(new NavInterfaceState(64, null, null, new()));
            Assert.That(radar.ShowIFF, Is.False);
            Assert.That(radar.ShowSweep, Is.False);
        });
    }
}
