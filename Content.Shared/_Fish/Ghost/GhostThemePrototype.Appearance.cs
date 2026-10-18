namespace Content.Shared._Sunrise.GhostTheme;

public sealed partial class GhostThemePrototype
{
    /// <summary>
    /// Displays the deceased character's head markings over this theme's sprite.
    /// </summary>
    [DataField]
    public bool ShowHeadAppearance;

    /// <summary>
    /// Displays the deceased character's equipped clothing over this theme's sprite.
    /// </summary>
    [DataField]
    public bool ShowClothing;
}
