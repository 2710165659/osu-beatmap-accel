using System.ComponentModel;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.ProxyAccel;

public partial class ProxyAccelInputManager : RulesetInputManager<ProxyAccelAction>
{
    public ProxyAccelInputManager(RulesetInfo ruleset)
        : base(ruleset, 0, SimultaneousBindingMode.Unique)
    {
    }
}

public enum ProxyAccelAction
{
    [Description("Idle")]
    Idle
}
