using Robust.Shared.Configuration;

namespace Content.Shared._Sunrise.SunriseCCVars;

public sealed partial class SunriseCCVars
{
    /// <summary>
    /// Включает bloom совместимых источников света и emissive-слоёв спрайтов.
    /// </summary>
    public static readonly CVarDef<bool> LightBloomEnabled =
        CVarDef.Create("sunrise.light_bloom_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Управляет прямой интенсивностью bloom в диапазоне от нуля до единицы.
    /// </summary>
    public static readonly CVarDef<float> LightBloomStrength =
        CVarDef.Create("sunrise.light_bloom_strength", 0.45f, CVar.CLIENTONLY | CVar.ARCHIVE); // FIsh edit
}
