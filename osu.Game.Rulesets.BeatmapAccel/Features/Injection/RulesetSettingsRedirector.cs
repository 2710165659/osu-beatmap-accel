using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Overlays;
using osu.Game.Rulesets.BeatmapAccel.Settings;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Injection;

public partial class RulesetSettingsRedirector : AbstractHandler
{
    private const string ruleset_short_name = "beatmapaccel";

    [Resolved]
    private Bindable<RulesetInfo> currentRuleset { get; set; } = null!;

    [Resolved]
    private SettingsOverlay settingsOverlay { get; set; } = null!;

    [Resolved]
    private RulesetStore rulesetStore { get; set; } = null!;

    private RulesetInfo? lastNonBeatmapAccelRuleset;
    private bool suppressRulesetChange;

    [BackgroundDependencyLoader]
    private void load()
    {
        if (!isBeatmapAccel(currentRuleset.Value))
            lastNonBeatmapAccelRuleset = currentRuleset.Value;

        currentRuleset.BindValueChanged(onRulesetChanged, true);
    }

    private void onRulesetChanged(ValueChangedEvent<RulesetInfo> change)
    {
        if (suppressRulesetChange)
            return;

        if (!isBeatmapAccel(change.NewValue))
        {
            lastNonBeatmapAccelRuleset = change.NewValue;
            return;
        }

        RulesetInfo? fallbackRuleset = !isBeatmapAccel(change.OldValue)
            ? change.OldValue
            : lastNonBeatmapAccelRuleset ?? rulesetStore.AvailableRulesets.FirstOrDefault(r => !isBeatmapAccel(r));

        Schedule(() =>
        {
            settingsOverlay.ShowAtControl<BeatmapAccelSettingsSubsection>();

            if (fallbackRuleset == null)
                return;

            suppressRulesetChange = true;
            currentRuleset.Value = fallbackRuleset;
            suppressRulesetChange = false;
            lastNonBeatmapAccelRuleset = fallbackRuleset;
        });
    }

    private static bool isBeatmapAccel(IRulesetInfo? rulesetInfo)
        => string.Equals(rulesetInfo?.ShortName, ruleset_short_name, StringComparison.OrdinalIgnoreCase);
}
