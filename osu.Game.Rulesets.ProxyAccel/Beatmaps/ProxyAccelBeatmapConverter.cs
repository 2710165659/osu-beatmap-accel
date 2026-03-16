using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.ProxyAccel.Objects;
using osuTK;

namespace osu.Game.Rulesets.ProxyAccel.Beatmaps;

public class ProxyAccelBeatmapConverter : BeatmapConverter<ProxyAccelHitObject>
{
    public ProxyAccelBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
        : base(beatmap, ruleset)
    {
    }

    public override bool CanConvert() => true;

    protected override IEnumerable<ProxyAccelHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
    {
        yield return new ProxyAccelHitObject
        {
            Samples = original.Samples,
            StartTime = original.StartTime,
            Position = (original as IHasPosition)?.Position ?? Vector2.Zero,
        };
    }
}
