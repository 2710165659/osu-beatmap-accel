using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.BeatmapAccel.Objects;
using osu.Game.Rulesets.BeatmapAccel.Objects.Drawables;
using osu.Game.Rulesets.BeatmapAccel.Replays;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BeatmapAccel.UI;

[Cached]
public partial class DrawableBeatmapAccelRuleset : DrawableRuleset<BeatmapAccelHitObject>
{
    public DrawableBeatmapAccelRuleset(BeatmapAccelRuleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods)
        : base(ruleset, beatmap, mods)
    {
    }

    protected override Playfield CreatePlayfield() => new BeatmapAccelPlayfield();

    protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BeatmapAccelFramedReplayInputHandler(replay);

    public override DrawableHitObject<BeatmapAccelHitObject> CreateDrawableRepresentation(BeatmapAccelHitObject hitObject)
        => new DrawableBeatmapAccelHitObject(hitObject);

    protected override PassThroughInputManager CreateInputManager() => new BeatmapAccelInputManager(Ruleset!.RulesetInfo);
}
