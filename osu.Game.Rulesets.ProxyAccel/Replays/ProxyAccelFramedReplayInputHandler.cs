using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Framework.Utils;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.ProxyAccel.Replays;

public class ProxyAccelFramedReplayInputHandler : FramedReplayInputHandler<ProxyAccelReplayFrame>
{
    public ProxyAccelFramedReplayInputHandler(Replay replay)
        : base(replay)
    {
    }

    protected override bool IsImportant(ProxyAccelReplayFrame frame) => frame.Actions.Count > 0;

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        var position = Interpolation.ValueAt(CurrentTime, StartFrame.Position, EndFrame.Position, StartFrame.Time, EndFrame.Time);

        inputs.Add(new MousePositionAbsoluteInput
        {
            Position = GamefieldToScreenSpace(position),
        });
        inputs.Add(new ReplayState<ProxyAccelAction>
        {
            PressedActions = CurrentFrame?.Actions ?? new List<ProxyAccelAction>(),
        });
    }
}
