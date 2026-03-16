using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.ProxyAccel.Objects;
using osu.Game.Rulesets.Scoring;
using osuTK;

namespace osu.Game.Rulesets.ProxyAccel.Objects.Drawables;

public partial class DrawableProxyAccelHitObject : DrawableHitObject<ProxyAccelHitObject>
{
    public DrawableProxyAccelHitObject(ProxyAccelHitObject hitObject)
        : base(hitObject)
    {
        Size = new Vector2(40);
        Origin = Anchor.Centre;
        Position = hitObject.Position;
        Alpha = 0;
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (timeOffset >= 0)
            ApplyResult(HitResult.Perfect);
    }
}
