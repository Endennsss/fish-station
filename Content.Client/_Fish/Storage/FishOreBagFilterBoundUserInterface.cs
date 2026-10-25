using Content.Shared._Fish.Storage;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Fish.Storage;

[UsedImplicitly]
public sealed partial class FishOreBagFilterBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private FishOreBagFilterSystem _filter = default!;

    private FishOreBagFilterWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<FishOreBagFilterWindow>();
        _window.LoadStackTypes(_filter.GetFilterableStackTypes());
        _window.StackToggled += (stackType, enabled) =>
            SendPredictedMessage(new FishOreBagFilterSetStackMessage(stackType, enabled));
        _window.AllToggled += enabled => SendPredictedMessage(new FishOreBagFilterSetAllMessage(enabled));
        _window.OtherToggled += enabled => SendPredictedMessage(new FishOreBagFilterSetOtherMessage(enabled));

        Update();
    }

    public override void Update()
    {
        if (!EntMan.TryGetComponent<FishOreBagFilterComponent>(Owner, out var filter))
            return;

        _window?.UpdateState(filter);
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);

        if (_window == null)
            return;

        _window.LoadStackTypes(_filter.GetFilterableStackTypes());
        Update();
    }
}
