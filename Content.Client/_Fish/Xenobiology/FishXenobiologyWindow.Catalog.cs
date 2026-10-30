using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Palette;
using Content.Shared._Fish.Xenobiology;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Fish.Xenobiology;

public sealed partial class FishXenobiologyWindow
{
    /// <summary>Catalog navigation and recipes are independent of the live specimen refresh.</summary>
    private readonly Dictionary<string, List<FishXenobiologyReactionPrototype>> _reactions = new();

    private void BuildCatalog()
    {
        foreach (var reaction in _prototype.EnumeratePrototypes<FishXenobiologyReactionPrototype>())
        {
            if (!_reactions.TryGetValue(reaction.Breed.Id, out var recipes))
                _reactions.Add(reaction.Breed.Id, recipes = new List<FishXenobiologyReactionPrototype>());
            recipes.Add(reaction);
        }
        foreach (var breed in _breeds)
        {
            var button = new Button
            {
                Text = _loc.GetString("fish-xeno-breed-button", ("tier", breed.Tier), ("name", _loc.GetString(breed.Name))),
                HorizontalExpand = true,
                ClipText = true,
                ToggleMode = true,
                ToolTip = _loc.GetString(breed.Description),
            };
            button.OnPressed += _ => SelectBreed(breed.ID);
            BreedList.AddChild(button);
            _breedButtons.Add(breed.ID, button);
        }
    }

    private void ApplyCatalogFilter()
    {
        var search = CatalogSearch.Text.Trim();
        var visible = 0;
        foreach (var breed in _breeds)
        {
            var button = _breedButtons[breed.ID];
            button.Visible = search.Length == 0 || _loc.GetString(breed.Name).Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                _loc.GetString(breed.Description).Contains(search, StringComparison.CurrentCultureIgnoreCase);
            if (button.Visible)
                visible++;
        }
        CatalogEmpty.Visible = visible == 0;
        UpdateSummary();
    }

    /// <summary>Opens a lineage, clearing a search that could otherwise conceal a mutation target.</summary>
    public bool SelectBreed(string id) => SelectBreed(id, true);

    private bool SelectBreed(string id, bool showDetails)
    {
        if (!_prototype.TryIndex<FishXenobiologyBreedPrototype>(id, out var breed) || !_breedButtons.ContainsKey(id))
            return false;
        if (showDetails)
        {
            if (!_breedButtons[id].Visible)
                CatalogSearch.SetText(string.Empty, invokeEvent: true);
            _showCatalogDetails = true;
        }
        foreach (var (breedId, button) in _breedButtons)
            button.Pressed = breedId == id;
        UpdateAdaptiveLayout();
        if (_selectedBreed == id)
            return true;
        _selectedBreed = id;
        CatalogDetailsScroll.SetScrollValue(Vector2.Zero);
        BreedDetails.RemoveAllChildren();
        var heading = new BoxContainer { SeparationOverride = 12 };
        var portrait = new TextureRect { SetSize = new Vector2(64), Stretch = TextureRect.StretchMode.KeepAspectCentered };
        FishXenobiologyPresentation.SetIcon(portrait, _sprite, breed);
        heading.AddChild(portrait);
        var title = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        title.AddChild(new Label { Text = _loc.GetString(breed.Name), StyleClasses = { StyleClass.LabelHeading } });
        title.AddChild(new Label { Text = _loc.GetString("fish-xeno-tier", ("tier", breed.Tier)), StyleClasses = { StyleClass.LabelSubText } });
        heading.AddChild(title);
        BreedDetails.AddChild(heading);
        AddCatalogText(_loc.GetString(breed.Description));
        BreedDetails.AddChild(new Label { Text = _loc.GetString("fish-xeno-care"), StyleClasses = { StyleClass.LabelHeading } });
        AddCatalogText(_loc.GetString("fish-xeno-breed-conditions", ("seconds", breed.GrowthDuration), ("growth", breed.GrowthBiomass),
            ("division", breed.DivisionBiomass), ("minimum", FishXenobiologyPresentation.Number(breed.MinimumTemperature - 273.15f)),
            ("maximum", FishXenobiologyPresentation.Number(breed.MaximumTemperature - 273.15f))));
        AddCatalogText(_loc.GetString("fish-xeno-care-thresholds"));
        BreedDetails.AddChild(new Label { Text = _loc.GetString("fish-xeno-mutation-heading"), StyleClasses = { StyleClass.LabelHeading } });
        if (breed.Mutations.Count == 0)
            AddCatalogText(_loc.GetString("fish-xeno-stable-lineage"));
        else
        {
            var total = breed.Mutations.Values.Sum();
            AddCatalogText(_loc.GetString("fish-xeno-mutation-explanation"));
            foreach (var (target, weight) in breed.Mutations.OrderBy(pair => pair.Key.Id))
            {
                var mutation = _prototype.Index(target);
                var button = new Button
                {
                    Text = _loc.GetString("fish-xeno-mutation-target", ("name", _loc.GetString(mutation.Name)),
                        ("chance", FishXenobiologyPresentation.Number(total > 0 ? weight / total * 100 : 0))),
                    HorizontalExpand = true,
                    ClipText = true,
                };
                button.OnPressed += _ => SelectBreed(target.Id);
                BreedDetails.AddChild(button);
            }
        }
        BreedDetails.AddChild(new Label { Text = _loc.GetString("fish-xeno-reaction-heading"), StyleClasses = { StyleClass.LabelHeading } });
        AddCatalogText(_loc.GetString("fish-xeno-extract-instruction"));
        if (_reactions.TryGetValue(id, out var recipes))
        {
            foreach (var reaction in recipes.OrderBy(recipe => recipe.ID))
                AddRecipe(reaction);
        }
        foreach (var industrial in _prototype.EnumeratePrototypes<FishXenobiologyIndustrialRecipePrototype>().OrderBy(recipe => recipe.ID))
        {
            if (industrial.Breed.Id != id)
                continue;

            BreedDetails.AddChild(new Label { Text = _loc.GetString("fish-xeno-industrial-heading"), StyleClasses = { StyleClass.LabelHeading } });
            AddCatalogText(_loc.GetString("fish-xeno-industrial-catalog", ("required", industrial.RequiredExtracts)));
            AddCatalogText(_loc.GetString("fish-xeno-industrial-cycle", ("plasma", industrial.PlasmaCost),
                ("seconds", industrial.Interval.TotalSeconds)));
            foreach (var (product, count) in industrial.Products)
                AddCatalogText(_loc.GetString("fish-xeno-industrial-product", ("count", count), ("name", _prototype.Index(product).Name)));
        }
        return true;
    }

    private void AddRecipe(FishXenobiologyReactionPrototype reaction)
    {
        var panel = new PanelContainer { PanelOverride = FishXenobiologyPresentation.Panel(Palettes.Slate.BackgroundDark) };
        var recipe = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, Margin = new Thickness(8) };
        recipe.AddChild(new Label { Text = _loc.GetString(reaction.Name), StyleClasses = { StyleClass.LabelHeading } });
        recipe.AddChild(new Label
        {
            Text = _loc.GetString("fish-xeno-catalyst", ("amount", reaction.Amount),
                ("reagent", _prototype.Index(reaction.Reagent).LocalizedName)),
            FontColorOverride = Palettes.Cyan.Text,
        });
        var description = new RichTextLabel { HorizontalExpand = true };
        description.SetMessage(_loc.GetString(reaction.Description));
        recipe.AddChild(description);
        foreach (var (product, count) in reaction.Products)
        {
            var output = new BoxContainer { SeparationOverride = 8 };
            output.AddChild(new TextureRect
            {
                Texture = _sprite.GetPrototypeIcon(product).Default,
                SetSize = new Vector2(32),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
            });
            output.AddChild(new Label
            {
                Text = _loc.GetString("fish-xeno-product", ("count", count), ("name", _prototype.Index(product).Name)),
                VerticalAlignment = VAlignment.Center,
                ClipText = true,
                HorizontalExpand = true,
            });
            recipe.AddChild(output);
        }
        foreach (var (reagent, amount) in reaction.Reagents)
            recipe.AddChild(new Label { Text = _loc.GetString("fish-xeno-reagent-product", ("amount", amount),
                ("name", _prototype.Index(reagent).LocalizedName)) });
        panel.AddChild(recipe);
        BreedDetails.AddChild(panel);
    }

    private void AddCatalogText(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(text);
        BreedDetails.AddChild(label);
    }
}
