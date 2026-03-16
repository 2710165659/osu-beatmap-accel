using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BeatmapAccel.UI;

[Cached]
public partial class BeatmapAccelPlayfield : Playfield
{
    [BackgroundDependencyLoader]
    private void load()
    {
        AddRangeInternal(new Drawable[]
        {
            HitObjectContainer,
        });
    }
}
