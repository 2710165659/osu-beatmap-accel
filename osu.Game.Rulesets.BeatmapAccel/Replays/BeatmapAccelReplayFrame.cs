using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Replays;
using osuTK;

namespace osu.Game.Rulesets.BeatmapAccel.Replays;

public class BeatmapAccelReplayFrame : ReplayFrame
{
    public List<BeatmapAccelAction> Actions { get; } = new();

    public Vector2 Position { get; set; }

    public override bool IsEquivalentTo(ReplayFrame other)
        => other is BeatmapAccelReplayFrame frame
           && Time == frame.Time
           && Position == frame.Position
           && Actions.SequenceEqual(frame.Actions);
}
