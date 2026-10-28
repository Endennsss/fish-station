using System.Linq;
using Content.Client.Stylesheets.Palette;
using Content.Shared._Fish.Research;
using Content.Shared._Fish.Research.Prototypes;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Fish.Research.UI;

public sealed partial class FishResearchConsoleMenu
{
    private readonly List<ResearchExperimentEntry> _experiments = new();

    private void SetOrdersVisible(bool visible)
    {
        OrdersButton.Pressed = visible;
        OrdersPanel.Visible = visible;
        TreeContent.Visible = !visible;
        FooterPanel.Visible = !visible;
        SearchEdit.Visible = !visible;
    }

    private void BuildOrders()
    {
        OrdersContent.RemoveAllChildren();
        _experiments.Clear();
        foreach (var experiment in _prototype.EnumeratePrototypes<FishResearchExperimentPrototype>()
                     .OrderBy(experiment => experiment.ID, StringComparer.Ordinal))
        {
            _prototype.TryIndex(experiment.RewardTechnology, out var reward);
            var panel = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = PanelStyle(Palettes.Navy.BackgroundDark),
            };
            var content = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 7,
                Margin = new Thickness(10),
            };
            panel.AddChild(content);
            content.AddChild(Text(_loc.GetString(experiment.Name), true));
            content.AddChild(Text(_loc.GetString(experiment.Description,
                ("required", experiment.RequiredSamples),
                ("severity", MathF.Round(experiment.MinimumSeverity * 100)),
                ("minStability", MathF.Round(experiment.MinimumStability * 100)),
                ("maxStability", MathF.Round(experiment.MaximumStability * 100)),
                ("health", MathF.Round(experiment.MinimumHealth * 100)),
                ("nodes", experiment.MinimumUnlockedNodes),
                ("charge", MathF.Round(experiment.MinimumChargeFraction * 100)))));

            if (experiment.Kind == FishResearchSampleKind.Manufacture)
            {
                var recipes = new List<string>();
                foreach (var recipeId in experiment.AcceptedRecipes.OrderBy(id => id.ToString(), StringComparer.Ordinal))
                {
                    if (_prototype.TryIndex(recipeId, out var recipe))
                        recipes.Add(_lathe.GetRecipeName(recipe));
                }
                content.AddChild(Text(_loc.GetString("fish-research-experiments-recipes", ("recipes", string.Join(", ", recipes)))));
            }

            var progress = Text(string.Empty);
            progress.Name = $"ExperimentProgress{experiment.ID}";
            content.AddChild(progress);
            var bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = Math.Max(1, experiment.RequiredSamples),
                HorizontalExpand = true,
                MinHeight = 8,
            };
            content.AddChild(bar);

            var rewardName = reward == null ? experiment.RewardTechnology.ToString() : _loc.GetString(reward.Name);
            var discount = float.IsFinite(experiment.DiscountFraction)
                ? Math.Clamp(experiment.DiscountFraction, 0f, 1f)
                : 0f;
            content.AddChild(Text(_loc.GetString("fish-research-experiments-reward",
                ("technology", rewardName), ("percent", MathF.Round(discount * 100)))));
            var status = Text(string.Empty);
            status.Name = $"ExperimentStatus{experiment.ID}";
            content.AddChild(status);
            var rewardButton = new Button
            {
                Name = $"ExperimentReward{experiment.ID}",
                Text = _loc.GetString("fish-research-experiments-show-reward"),
                ToolTip = rewardName,
                ClipText = true,
                HorizontalExpand = true,
            };
            rewardButton.OnPressed += _ => NavigateToTechnology(experiment.RewardTechnology);
            content.AddChild(rewardButton);
            OrdersContent.AddChild(panel);
            _experiments.Add(new ResearchExperimentEntry(experiment, reward, progress, bar, status, rewardButton));
        }

        if (_experiments.Count == 0)
            OrdersContent.AddChild(Text(_loc.GetString("fish-research-experiments-empty")));
    }

    private void UpdateOrders(TechnologyDatabaseComponent? database)
    {
        foreach (var entry in _experiments)
        {
            var experiment = entry.Experiment;
            var count = Math.Clamp(database?.FishExperimentProgress.GetValueOrDefault(experiment.ID) ?? 0,
                0, Math.Max(0, experiment.RequiredSamples));
            var supported = database != null && _catalog != null && entry.Reward != null &&
                            _catalog.Technologies.ContainsKey(entry.Reward.ID);
            entry.RewardButton.Disabled = !supported;

            if (entry.Count != count)
            {
                entry.Count = count;
                entry.Progress.SetMessage(_loc.GetString("fish-research-experiments-progress",
                    ("current", count), ("required", experiment.RequiredSamples)));
                entry.Bar.Value = count;
            }

            var status = "fish-research-experiments-status-progress";
            var color = FishResearchTreeControl.StateColor(FishResearchNodeState.Available);
            var refunded = database?.FishResearchRefunds.GetValueOrDefault(experiment.RewardTechnology) ?? 0;
            if (database == null)
            {
                status = "fish-research-tree-no-database";
                color = Palettes.Red.Base;
            }
            else if (!supported)
            {
                status = "fish-research-experiments-status-unavailable";
                color = FishResearchTreeControl.StateColor(FishResearchNodeState.Locked);
            }
            else if (count >= experiment.RequiredSamples)
            {
                if (_unlocked.Contains(experiment.RewardTechnology))
                {
                    status = refunded > 0
                        ? "fish-research-experiments-status-refunded"
                        : "fish-research-experiments-status-reward-used";
                    color = FishResearchTreeControl.StateColor(FishResearchNodeState.Researched);
                }
                else
                {
                    status = "fish-research-experiments-status-completed";
                    color = FishResearchTreeControl.StateColor(FishResearchNodeState.Discounted);
                }
            }

            var message = _loc.GetString(status, ("points", refunded));
            if (entry.Status == message)
                continue;
            entry.Status = message;
            entry.Label.SetMessage(message, color);
        }
    }

    private sealed class ResearchExperimentEntry(FishResearchExperimentPrototype experiment,
        TechnologyPrototype? reward, RichTextLabel progress, ProgressBar bar, RichTextLabel label, Button rewardButton)
    {
        public readonly FishResearchExperimentPrototype Experiment = experiment;
        public readonly TechnologyPrototype? Reward = reward;
        public readonly RichTextLabel Progress = progress;
        public readonly ProgressBar Bar = bar;
        public readonly RichTextLabel Label = label;
        public readonly Button RewardButton = rewardButton;
        public int Count = -1;
        public string? Status;
    }
}
