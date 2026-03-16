using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.BeatmapAccel.Objects;
using osuTK;

namespace osu.Game.Rulesets.BeatmapAccel.Beatmaps;

public class BeatmapAccelBeatmapConverter : BeatmapConverter<BeatmapAccelHitObject>
{
    public BeatmapAccelBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
        : base(beatmap, ruleset)
    {
    }

    public override bool CanConvert() => true;

    protected override IEnumerable<BeatmapAccelHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
    {
        yield return new BeatmapAccelHitObject
        {
            Samples = original.Samples,
            StartTime = original.StartTime,
            Position = (original as IHasPosition)?.Position ?? Vector2.Zero,
        };
    }
}
