using Robust.Shared.Prototypes;

namespace Content.Shared.Mech.Components;

public sealed partial class MechComponent
{
    /// <summary>
    /// Действие сканера массы, которое выдаётся пилоту при посадке в мех.
    /// </summary>
    [DataField]
    public EntProtoId? MechMassScannerAction;

    /// <summary>
    /// Сущность действия сканера массы, выданного текущему пилоту.
    /// </summary>
    public EntityUid? MechMassScannerActionEntity;
}
