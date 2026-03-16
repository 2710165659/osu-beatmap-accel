using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Replays;
using osuTK;

namespace osu.Game.Rulesets.ProxyAccel.Replays;

public class ProxyAccelReplayFrame : ReplayFrame
{
    public List<ProxyAccelAction> Actions { get; } = new();

    public Vector2 Position { get; set; }

    public override bool IsEquivalentTo(ReplayFrame other)
        => other is ProxyAccelReplayFrame frame
           && Time == frame.Time
           && Position == frame.Position
           && Actions.SequenceEqual(frame.Actions);
}
