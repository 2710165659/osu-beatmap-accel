using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.ProxyAccel.Objects;
using osu.Game.Rulesets.ProxyAccel.Objects.Drawables;
using osu.Game.Rulesets.ProxyAccel.Replays;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.ProxyAccel.UI;

[Cached]
public partial class DrawableProxyAccelRuleset : DrawableRuleset<ProxyAccelHitObject>
{
    public DrawableProxyAccelRuleset(ProxyAccelRuleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods)
        : base(ruleset, beatmap, mods)
    {
    }

    protected override Playfield CreatePlayfield() => new ProxyAccelPlayfield();

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new ProxyAccelFramedReplayInputHandler(replay);

    public override DrawableHitObject<ProxyAccelHitObject> CreateDrawableRepresentation(ProxyAccelHitObject hitObject)
        => new DrawableProxyAccelHitObject(hitObject);

    protected override PassThroughInputManager CreateInputManager() => new ProxyAccelInputManager(Ruleset!.RulesetInfo);
}
