using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Fish.LightShafts;

/// <summary>Направленный декоративный объём света. Не заменяет освещение PointLight.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LightShaftComponent : Component
{
    /// <summary>Включение эффекта независимо от лампы.</summary>
    [DataField, AutoNetworkedField] public bool Enabled = true;
    /// <summary>Цвет при отключённом UseLightColor.</summary>
    [DataField, AutoNetworkedField] public Color Color = Color.White;
    /// <summary>Брать цвет из PointLight, если он существует.</summary>
    [DataField, AutoNetworkedField] public bool UseLightColor = true;
    /// <summary>Следовать включению, перекрытию контейнером и энергии PointLight.</summary>
    [DataField, AutoNetworkedField] public bool FollowLight = true;
    /// <summary>Аддитивная интенсивность, ограниченная клиентом диапазоном 0–1.</summary>
    [DataField, AutoNetworkedField] public float Intensity = 0.12f;
    /// <summary>Дальность в метрах, максимум 16.</summary>
    [DataField, AutoNetworkedField] public float Length = 6f;
    /// <summary>Полный угол конуса в градусах, 5–150.</summary>
    [DataField, AutoNetworkedField] public float ConeAngle = 35f;
    /// <summary>Поворот относительно сущности в градусах: 0 — локальный +X.</summary>
    [DataField, AutoNetworkedField] public float Direction;
    /// <summary>Локальное смещение точки излучения; не следует автоматически за PointLight.Offset.</summary>
    [DataField, AutoNetworkedField] public Vector2 Offset;
    /// <summary>Степень затухания с расстоянием.</summary>
    [DataField, AutoNetworkedField] public float Falloff = 1.8f;
    /// <summary>Доля ширины, отведённая мягкому краю.</summary>
    [DataField, AutoNetworkedField] public float Softness = 0.5f;
    /// <summary>Амплитуда слабого экранного шума; 0 отключает шум.</summary>
    [DataField, AutoNetworkedField] public float NoiseAmount = 0.015f;
    /// <summary>Скорость шума; 0 даёт неподвижный рисунок.</summary>
    [DataField, AutoNetworkedField] public float NoiseSpeed;
    /// <summary>Число ступеней яркости; 0 отключает квантование.</summary>
    [DataField, AutoNetworkedField] public int Quantization;
    /// <summary>Обрезать луч по геометрии Occluder. Отключать только для отладки.</summary>
    [DataField, AutoNetworkedField] public bool OcclusionEnabled = true;
    /// <summary>Усиливать эффект по присутствию сетевых SmokeComponent около трёх точек луча.</summary>
    [DataField, AutoNetworkedField] public bool SmokeReactive;
    /// <summary>Множитель в чистом воздухе для SmokeReactive.</summary>
    [DataField, AutoNetworkedField] public float CleanAirFactor = 0.12f;
    /// <summary>Множитель при полной условной плотности дыма.</summary>
    [DataField, AutoNetworkedField] public float SmokeMultiplier = 1.5f;
    /// <summary>Дополнительная условная плотность 0–1 для карт и будущей интеграции тумана.</summary>
    [DataField, AutoNetworkedField] public float FogDensity;
}
