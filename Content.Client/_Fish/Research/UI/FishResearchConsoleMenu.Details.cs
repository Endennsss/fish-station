using Content.Client.Stylesheets.Palette;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Fish.Research.UI;

public sealed partial class FishResearchConsoleMenu
{
    // Fish-edit: детали перестраиваются при выборе или изменении исследований, баланс обновляет только действие.
    private void RefreshDetails()
    {
        if (_detailsDirty)
        {
            _detailsDirty = false;
            DetailsContent.RemoveAllChildren();
            if (SelectedTechnology is not { } id || _catalog == null || !_catalog.Technologies.TryGetValue(id, out var technology))
            {
                DetailsContent.AddChild(Text(_loc.GetString("fish-research-tree-select-hint")));
            }
            else
            {
                var prototype = technology.Prototype;
                var discipline = _catalog.Disciplines[prototype.Discipline];
                var heading = new BoxContainer { SeparationOverride = 10, HorizontalExpand = true };
                heading.AddChild(new TextureRect
                {
                    Texture = technology.Icon,
                    SetSize = new(48, 48),
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    VerticalAlignment = VAlignment.Top,
                });
                heading.AddChild(Text(technology.Name, true));
                DetailsContent.AddChild(heading);
                DetailsContent.AddChild(Text(_loc.GetString("fish-research-tree-discipline-tier",
                    ("discipline", _loc.GetString(discipline.Name)), ("tier", prototype.Tier)), color: discipline.Color));
                DetailsContent.AddChild(Text(technology.Description));
                var cost = _technologyCosts.GetValueOrDefault(id, prototype.Cost);
                if (cost < prototype.Cost)
                {
                    var baseCost = Text(_loc.GetString("fish-research-tree-base-cost", ("cost", prototype.Cost)));
                    baseCost.Name = "TechnologyBaseCost";
                    DetailsContent.AddChild(baseCost);
                }
                var costLabel = Text(_loc.GetString("fish-research-tree-cost", ("cost", cost)), true, Palettes.Gold.Base);
                costLabel.Name = "TechnologyCost";
                DetailsContent.AddChild(costLabel);
                if (cost < prototype.Cost)
                    DetailsContent.AddChild(Text(_loc.GetString("fish-research-tree-discount-applied")));
                DetailsContent.AddChild(Text(_loc.GetString("fish-research-tree-requirements"), true));
                AddTechnologyRequirements(prototype);
                DetailsContent.AddChild(Text(_loc.GetString("fish-research-tree-unlocks", ("count", technology.Recipes.Count)), true));
                foreach (var recipe in technology.Recipes)
                    AddRecipe(recipe, technology);
                foreach (var unlock in prototype.GenericUnlocks)
                    DetailsContent.AddChild(Text(_loc.GetString(unlock.UnlockDescription)));
                if (technology.Recipes.Count == 0 && prototype.GenericUnlocks.Count == 0)
                    DetailsContent.AddChild(Text(_loc.GetString("fish-research-tree-no-unlocks")));
            }
        }

        UpdatePurchaseStatus();
    }

    private RichTextLabel Text(string text, bool heading = false, Color? color = null)
    {
        var label = new RichTextLabel { HorizontalExpand = true, ToolTip = text };
        var message = new FormattedMessage();
        if (heading)
            message.PushTag(new MarkupNode("bold", null, null));
        message.AddText(text);
        if (heading)
            message.Pop();
        label.SetMessage(message, color);
        return label;
    }

    private void AddTechnologyRequirements(TechnologyPrototype technology)
    {
        if (technology.TechnologyPrerequisites.Count == 0)
        {
            DetailsContent.AddChild(Text(_loc.GetString("fish-research-tree-no-prerequisites")));
            return;
        }

        foreach (var prerequisite in technology.TechnologyPrerequisites)
        {
            if (!_prototype.TryIndex(prerequisite, out var prototype))
                continue;
            var name = _loc.GetString(prototype.Name);
            var complete = _unlocked.Contains(prerequisite);
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
            row.AddChild(Text(_loc.GetString(complete ? "fish-research-tree-prerequisite-done" : "fish-research-tree-prerequisite-missing", ("name", name)),
                color: complete ? Palettes.Green.Base : Palettes.Red.Base));
            var navigate = new Button
            {
                Text = _loc.GetString("fish-research-tree-show-prerequisite"),
                ToolTip = name,
                ClipText = true,
                Disabled = _catalog == null || !_catalog.Technologies.ContainsKey(prerequisite),
            };
            navigate.OnPressed += _ => NavigateToTechnology(prerequisite);
            row.AddChild(navigate);
            DetailsContent.AddChild(row);
        }
    }

    private void AddRecipe(FishResearchRecipe recipe, FishResearchTechnology technology)
    {
        var row = new BoxContainer { SeparationOverride = 8, HorizontalExpand = true };
        row.AddChild(new TextureRect
        {
            Texture = recipe.Icon ?? technology.Icon,
            SetSize = new(32, 32),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = VAlignment.Top,
            ToolTip = recipe.Description,
        });
        var caption = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 3 };
        caption.AddChild(Text(recipe.Name));
        if (!string.IsNullOrWhiteSpace(recipe.Description))
            caption.AddChild(Text(recipe.Description));
        row.AddChild(caption);
        DetailsContent.AddChild(row);
    }

    private void UpdatePurchaseStatus()
    {
        ResearchButton.Disabled = true;
        if (!_entity.TryGetComponent(_console, out TechnologyDatabaseComponent? database))
        {
            PurchaseStatusLabel.SetMessage(_loc.GetString("fish-research-tree-no-database"));
            return;
        }
        if (SelectedTechnology is not { } id || _catalog == null || !_catalog.Technologies.TryGetValue(id, out var technology))
        {
            PurchaseStatusLabel.SetMessage(_loc.GetString("fish-research-tree-select-hint"));
            return;
        }

        var cost = _research.GetTechnologyCost(database, technology.Prototype);
        string message;
        Color color;
        if (_unlocked.Contains(id))
        {
            message = _loc.GetString("fish-research-tree-already-researched");
            color = Palettes.Green.Base;
        }
        else if (!_nodeStates.TryGetValue(id, out var state) || state == FishResearchNodeState.Locked)
        {
            message = _loc.GetString("fish-research-tree-locked-reason");
            color = Palettes.Red.Base;
        }
        else if (!_hasAccess)
        {
            message = _loc.GetString("research-console-no-access-popup");
            color = Palettes.Red.Base;
        }
        else if (_points < cost)
        {
            message = _loc.GetString("fish-research-tree-points-missing", ("points", cost - _points));
            color = Palettes.Gold.Base;
        }
        else
        {
            message = _loc.GetString("fish-research-tree-ready");
            color = Palettes.Green.Base;
            ResearchButton.Disabled = false;
        }
        PurchaseStatusLabel.SetMessage(message, color);
        ResearchButton.ToolTip = message;
    }
}
