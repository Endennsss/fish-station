using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Fish.Shaders.Bloom;

/// <summary>
/// Добавляет декоративный bloom выбранным слоям спрайта без создания PointLight.
/// </summary>
[RegisterComponent]
public sealed partial class FishEmissiveBloomComponent : Component
{
    /// <summary>
    /// Ключи слоёв спрайта, которые повторно рисуются как emissive mask.
    /// </summary>
    [DataField(required: true)]
    public List<string> Layers = [];

    /// <summary>
    /// Локальный множитель интенсивности поверх пользовательской настройки bloom.
    /// </summary>
    [DataField]
    public float Strength = 0.8f;

    /// <summary>
    /// Множитель радиусов blur. Для небольших экранов обычно достаточно 0.5–0.8.
    /// </summary>
    [DataField]
    public float Radius = 0.65f;

    /// <summary>
    /// Дополнительная модуляция цвета; исходный цвет текстуры при этом сохраняется.
    /// </summary>
    [DataField]
    public Color Color = Color.White;
}
