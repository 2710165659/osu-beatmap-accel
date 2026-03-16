using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.ProxyAccel.UI;

[Cached]
public partial class ProxyAccelPlayfield : Playfield
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
