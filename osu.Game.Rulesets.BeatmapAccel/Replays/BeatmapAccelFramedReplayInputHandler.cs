using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Framework.Utils;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BeatmapAccel.Replays;

public class BeatmapAccelFramedReplayInputHandler : FramedReplayInputHandler<BeatmapAccelReplayFrame>
{
    public BeatmapAccelFramedReplayInputHandler(Replay replay)
        : base(replay)
    {
    }

    protected override bool IsImportant(BeatmapAccelReplayFrame frame) => frame.Actions.Count > 0;

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        var position = Interpolation.ValueAt(CurrentTime, StartFrame.Position, EndFrame.Position, StartFrame.Time, EndFrame.Time);

        inputs.Add(new MousePositionAbsoluteInput
        {
            Position = GamefieldToScreenSpace(position),
        });
        inputs.Add(new ReplayState<BeatmapAccelAction>
        {
            PressedActions = CurrentFrame?.Actions ?? new List<BeatmapAccelAction>(),
        });
    }
}
