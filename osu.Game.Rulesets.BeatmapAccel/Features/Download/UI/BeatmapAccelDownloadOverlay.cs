using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online;
using osu.Game.Online.API.Requests.Responses;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download.UI;

public partial class BeatmapAccelDownloadOverlay : Container
{
    private readonly APIBeatmapSet beatmapSet;

    private BeatmapAccelBeatmapDownloadTracker tracker = null!;
    private bool useToolbarHeight = true;

    [Resolved]
    private OsuGame game { get; set; } = null!;

    public BeatmapAccelDownloadOverlay(APIBeatmapSet beatmapSet)
    {
        this.beatmapSet = beatmapSet;

        Anchor = Anchor.TopRight;
        Origin = Anchor.TopRight;
    }

    [BackgroundDependencyLoader]
    private void load(OsuColour colours)
    {
        AutoSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 10;
        Alpha = 0.001f;

        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Colour = Color4.Black.Opacity(0.35f),
            Radius = 6,
            Offset = new Vector2(0, 3)
        };

        tracker = new BeatmapAccelBeatmapDownloadTracker(beatmapSet)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        };

        FillFlowContainer buttonFlow;
        OsuAnimatedButton closeButton;

        var cover = new OnlineBeatmapSetCover(beatmapSet, BeatmapSetCoverType.Card)
        {
            RelativeSizeAxes = Axes.Both,
            Colour = Color4Extensions.FromHex("#32505a"),
            FillMode = FillMode.Fill,
            Alpha = 0.001f,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        };

        cover.OnLoadComplete += drawable => drawable.FadeIn(300, Easing.OutQuint);

        InternalChildren = new Drawable[]
        {
            tracker,
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Black.Opacity(0.62f)
            },
            new DelayedLoadUnloadWrapper(() => cover, 10)
            {
                RelativeSizeAxes = Axes.Both
            },
            closeButton = new OsuAnimatedButton
            {
                Size = new Vector2(18),
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Margin = new MarginPadding(12),
                Action = Hide
            },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(12),
                Margin = new MarginPadding(24),
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(6),
                        Direction = FillDirection.Vertical,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = "Beatmap Accel",
                                Colour = colours.Blue1,
                                Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                            },
                            new OsuSpriteText
                            {
                                Text = getDisplayTitle(),
                                Font = OsuFont.GetFont(size: 20, typeface: Typeface.TorusAlternate),
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Margin = new MarginPadding { Horizontal = 16 }
                            },
                            new OsuSpriteText
                            {
                                Text = getDisplayArtist(),
                                Font = OsuFont.GetFont(size: 14, typeface: Typeface.TorusAlternate, weight: FontWeight.SemiBold),
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Margin = new MarginPadding { Horizontal = 16 }
                            }
                        }
                    },
                    buttonFlow = new FillFlowContainer
                    {
                        Height = 40,
                        AutoSizeAxes = Axes.X,
                        Spacing = new Vector2(5),
                        Margin = new MarginPadding { Top = 6 },
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Child = new BeatmapAccelDownloadButton(beatmapSet)
                    }
                }
            }
        };

        if (beatmapSet.HasVideo)
            buttonFlow.Add(new BeatmapAccelDownloadButton(beatmapSet, true));

        closeButton.Add(new SpriteIcon
        {
            Icon = FontAwesome.Solid.Times,
            RelativeSizeAxes = Axes.Both,
            Scale = new Vector2(0.8f),
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        });

        tracker.State.BindValueChanged(onStateChanged, true);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        Y = 48;
        X = -12;

        if (!useToolbarHeight)
            return;

        try
        {
            var toolbar = game.Toolbar;
            Y = toolbar.Y + toolbar.Height + 8;
        }
        catch (Exception e)
        {
            BeatmapAccelLogging.LogError(e, "Unable to read toolbar position");
            useToolbarHeight = false;
        }
    }

    public override void Hide()
    {
        this.FadeOut(250, Easing.OutQuint)
            .MoveToX(12, 250, Easing.OutQuint);
    }

    public override void Show()
    {
        this.FadeIn(250, Easing.OutQuint)
            .MoveToX(-12, 250, Easing.OutQuint);
    }

    private void onStateChanged(ValueChangedEvent<DownloadState> state)
    {
        switch (state.NewValue)
        {
            case DownloadState.LocallyAvailable:
                Hide();
                Expire();
                break;

            default:
                Show();
                break;
        }
    }

    protected override bool OnClick(ClickEvent e) => true;

    protected override bool OnMouseDown(MouseDownEvent e) => true;

    private string getDisplayTitle()
    {
        var metadata = ((IBeatmapSetInfo)beatmapSet).Metadata;
        return string.IsNullOrWhiteSpace(metadata.TitleUnicode) ? metadata.Title : metadata.TitleUnicode;
    }

    private string getDisplayArtist()
    {
        var metadata = ((IBeatmapSetInfo)beatmapSet).Metadata;
        return string.IsNullOrWhiteSpace(metadata.ArtistUnicode) ? metadata.Artist : metadata.ArtistUnicode;
    }
}
