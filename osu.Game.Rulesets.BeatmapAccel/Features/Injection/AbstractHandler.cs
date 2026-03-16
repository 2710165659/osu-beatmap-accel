using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Injection;

public abstract partial class AbstractHandler : CompositeDrawable
{
    [Resolved]
    private OsuGame game { get; set; } = null!;

    protected OsuGame Game => game;
}
