using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.BeatmapAccel.Beatmaps;
using osu.Game.Rulesets.BeatmapAccel.Configuration;
using osu.Game.Rulesets.BeatmapAccel.Features.Injection;
using osu.Game.Rulesets.BeatmapAccel.Settings;
using osu.Game.Rulesets.BeatmapAccel.UI;
using osu.Game.Rulesets.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BeatmapAccel;

public partial class BeatmapAccelRuleset : Ruleset
{
    public override string Description => "BeatmapAccel";

    public override string ShortName => "beatmapaccel";

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods)
        => new DrawableBeatmapAccelRuleset(this, beatmap, mods);

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        => new BeatmapAccelBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        => new BeatmapAccelDifficultyCalculator(RulesetInfo, beatmap);

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
        => new BeatmapAccelRulesetConfigManager(settings, RulesetInfo);

    public override RulesetSettingsSubsection CreateSettings()
        => new BeatmapAccelSettingsSubsection(this);

    public override IEnumerable<Mod> GetModsFor(ModType type) => Array.Empty<Mod>();

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) => Array.Empty<KeyBinding>();

    public override Drawable CreateIcon() => new Icon(this);

    public partial class Icon : Sprite
    {
        private readonly Ruleset ruleset;

        public Icon(Ruleset ruleset)
        {
            this.ruleset = ruleset;
        }

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load(IRenderer renderer, OsuGame? game)
        {
            Texture = new TextureStore(renderer, new TextureLoaderStore(ruleset.CreateResourceStore()), false).Get("Textures/logo");

            if (game == null)
                return;

            InjectorBootstrapper.BeginInject(game, Scheduler);
        }
    }

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;
}
