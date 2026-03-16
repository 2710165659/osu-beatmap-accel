using System.ComponentModel;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BeatmapAccel;

public partial class BeatmapAccelInputManager : RulesetInputManager<BeatmapAccelAction>
{
    public BeatmapAccelInputManager(RulesetInfo ruleset)
        : base(ruleset, 0, SimultaneousBindingMode.Unique)
    {
    }
}

public enum BeatmapAccelAction
{
    [Description("Idle")]
    Idle
}
