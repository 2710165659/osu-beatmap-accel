using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osuTK;

namespace osu.Game.Rulesets.BeatmapAccel.Objects;

public class BeatmapAccelHitObject : HitObject, IHasPosition
{
    public override Judgement CreateJudgement() => new Judgement();

    public Vector2 Position { get; set; }

    public float X
    {
        get => Position.X;
        set => Position = new Vector2(value, Y);
    }

    public float Y
    {
        get => Position.Y;
        set => Position = new Vector2(X, value);
    }
}
