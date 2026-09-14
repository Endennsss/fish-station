namespace Content.Client._Fish.Dissolve;

/// <summary>Направление исчезновения в экранной плоскости спрайта.</summary>
public enum DissolveDirection : byte
{
    Noise,
    BottomToTop,
    TopToBottom,
}

/// <summary>Параметры локального визуального эффекта; не изменяют игровое состояние.</summary>
public sealed record DissolveSettings
{
    /// <summary>Ширина яркой кромки в диапазоне noise 0..1.</summary>
    public float EdgeWidth { get; init; } = 0.06f;
    /// <summary>Цвет внешней горячей области.</summary>
    public Color EdgeColor { get; init; } = Color.FromHex("#FF5008");
    /// <summary>Цвет узкой внутренней кромки.</summary>
    public Color CoreColor { get; init; } = Color.FromHex("#FFF1A0");
    /// <summary>Интенсивность свечения, от 0 до 4.</summary>
    public float EdgeIntensity { get; init; } = 1.4f;
    /// <summary>Число крупных noise-ячеек на метр спрайта.</summary>
    public float NoiseScale { get; init; } = 5f;
    /// <summary>Ширина обугленной зоны перед горячей кромкой.</summary>
    public float CharWidth { get; init; } = 0.08f;
    /// <summary>Доля затемнения обугленной зоны.</summary>
    public float CharStrength { get; init; } = 0.8f;
    /// <summary>Неизменное смещение noise; одинаковое значение сохраняет рисунок при reverse.</summary>
    public float Seed { get; init; } = 1f;
    /// <summary>Порядок исчезновения.</summary>
    public DissolveDirection Direction { get; init; }

    public static readonly DissolveSettings Burn = new();
    public static readonly DissolveSettings Teleport = new()
    {
        EdgeColor = Color.FromHex("#009DFF"), CoreColor = Color.FromHex("#A0FFFF"),
        CharStrength = 0f, Direction = DissolveDirection.BottomToTop,
    };
    public static readonly DissolveSettings Anomaly = new()
    {
        EdgeColor = Color.FromHex("#A000FF"), CoreColor = Color.FromHex("#FF80FF"),
        NoiseScale = 8f, EdgeWidth = 0.09f, CharStrength = 0.35f,
    };
    public static readonly DissolveSettings Energy = new()
    {
        EdgeColor = Color.FromHex("#20DFFF"), CoreColor = Color.White,
        EdgeWidth = 0.025f, EdgeIntensity = 2f, CharStrength = 0f,
    };
}
