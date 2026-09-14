using Robust.Client.Graphics;

namespace Content.Client._Fish.LightShafts;

/// <summary>Владеет клиентским overlay и его графическими ресурсами.</summary>
public sealed class LightShaftSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    private LightShaftOverlay? _shafts;

    public override void Initialize()
    {
        base.Initialize();
        _shafts = new LightShaftOverlay();
        _overlay.AddOverlay(_shafts);
    }

    public override void Shutdown()
    {
        if (_shafts != null)
        {
            _overlay.RemoveOverlay(_shafts);
            _shafts.Dispose();
            _shafts = null;
        }
        base.Shutdown();
    }
}
