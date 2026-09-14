using Robust.Client.Graphics;

namespace Content.Client._Fish.Dissolve;

/// <summary>Временное клиентское состояние dissolve и сохранённая видимость спрайта.</summary>
[RegisterComponent]
public sealed partial class DissolveComponent : Component
{
    public ShaderInstance? Shader;
    public bool PreviousVisible;
    public bool PreviousRaiseShaderEvent;
    public bool PreviousGetScreenTexture;
    public bool Hidden;
    public float Amount;
    public float From;
    public float To;
    public float Elapsed;
    public float Duration;
}
