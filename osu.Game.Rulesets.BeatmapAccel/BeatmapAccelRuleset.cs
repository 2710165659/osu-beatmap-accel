using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.StateChanges;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.BeatmapAccel.Configuration;
using osu.Game.Rulesets.BeatmapAccel.Features.Injection;
using osu.Game.Rulesets.BeatmapAccel.Settings;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Input.Handlers;
using osu.Game.Replays;
using osuTK;

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

    private class BeatmapAccelBeatmapConverter : BeatmapConverter<BeatmapAccelHitObject>
    {
        public BeatmapAccelBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        public override bool CanConvert() => true;

        protected override IEnumerable<BeatmapAccelHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            yield return new BeatmapAccelHitObject
            {
                Samples = original.Samples,
                StartTime = original.StartTime,
                Position = (original as IHasPosition)?.Position ?? Vector2.Zero,
            };
        }
    }

    private class BeatmapAccelDifficultyCalculator : DifficultyCalculator
    {
        public BeatmapAccelDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
            => new(mods, 0);

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
            => Enumerable.Empty<DifficultyHitObject>();

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate)
            => Array.Empty<Skill>();
    }

    private class BeatmapAccelHitObject : HitObject, IHasPosition
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

    [Cached]
    private partial class DrawableBeatmapAccelRuleset : DrawableRuleset<BeatmapAccelHitObject>
    {
        public DrawableBeatmapAccelRuleset(BeatmapAccelRuleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods)
            : base(ruleset, beatmap, mods)
        {
        }

        protected override Playfield CreatePlayfield() => new BeatmapAccelPlayfield();

        protected override ReplayInputHandler CreateReplayInputHandler(Replay replay) => new BeatmapAccelFramedReplayInputHandler(replay);

        public override DrawableHitObject<BeatmapAccelHitObject> CreateDrawableRepresentation(BeatmapAccelHitObject hitObject)
            => new DrawableBeatmapAccelHitObject(hitObject);

        protected override PassThroughInputManager CreateInputManager() => new BeatmapAccelInputManager(Ruleset!.RulesetInfo);
    }

    [Cached]
    private partial class BeatmapAccelPlayfield : Playfield
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(HitObjectContainer);
        }
    }

    private partial class DrawableBeatmapAccelHitObject : DrawableHitObject<BeatmapAccelHitObject>
    {
        public DrawableBeatmapAccelHitObject(BeatmapAccelHitObject hitObject)
            : base(hitObject)
        {
            Alpha = 0;
            Size = Vector2.Zero;
            AlwaysPresent = false;
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (timeOffset >= 0)
                ApplyResult(HitResult.Perfect);
        }
    }

    private class BeatmapAccelFramedReplayInputHandler : FramedReplayInputHandler<BeatmapAccelReplayFrame>
    {
        public BeatmapAccelFramedReplayInputHandler(Replay replay)
            : base(replay)
        {
        }

        protected override bool IsImportant(BeatmapAccelReplayFrame frame) => false;

        protected override void CollectReplayInputs(List<IInput> inputs)
        {
        }
    }

    private class BeatmapAccelReplayFrame : ReplayFrame
    {
        public override bool IsEquivalentTo(ReplayFrame other) => other.Time == Time;
    }

    private partial class BeatmapAccelInputManager : RulesetInputManager<BeatmapAccelAction>
    {
        public BeatmapAccelInputManager(RulesetInfo ruleset)
            : base(ruleset, 0, SimultaneousBindingMode.Unique)
        {
        }
    }

    private enum BeatmapAccelAction
    {
        [Description("Idle")]
        Idle
    }
}
