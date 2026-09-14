using Robust.Shared.Configuration;

namespace Content.Shared._Fish.LightShafts;

/// <summary>Локальные переключатели и жёсткие бюджеты декоративных лучей.</summary>
[CVarDefs]
public sealed class LightShaftCVars
{
    /// <summary>Общий клиентский переключатель эффекта.</summary>
    public static readonly CVarDef<bool> Enabled = CVarDef.Create("fish.light_shafts", true, CVar.CLIENTONLY | CVar.ARCHIVE);
    /// <summary>Показывать геометрию вместо градиента.</summary>
    public static readonly CVarDef<bool> Debug = CVarDef.Create("fish.light_shafts_debug", false, CVar.CLIENTONLY);
    /// <summary>Максимальное число источников на viewport; клиент ограничивает значением 100.</summary>
    public static readonly CVarDef<int> MaxVisible = CVarDef.Create("fish.light_shafts_max", 20, CVar.CLIENTONLY | CVar.ARCHIVE);
    /// <summary>Суммарный бюджет лучей пересечения на viewport за кадр, максимум 16384.</summary>
    public static readonly CVarDef<int> RayBudget = CVarDef.Create("fish.light_shafts_rays", 4096, CVar.CLIENTONLY | CVar.ARCHIVE);
}
