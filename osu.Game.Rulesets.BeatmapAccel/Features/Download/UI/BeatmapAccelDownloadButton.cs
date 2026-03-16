using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Online;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.BeatmapSet.Buttons;
using osu.Game.Rulesets.BeatmapAccel.Features.Injection.Utils;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download.UI;

public partial class BeatmapAccelDownloadButton : HeaderDownloadButton
{
    private readonly APIBeatmapSet beatmapSet;
    private readonly bool noVideo;

    private DownloadTracker<IBeatmapSetInfo>? tracker;

    public BeatmapAccelDownloadButton(APIBeatmapSet beatmapSet, bool noVideo = false)
        : base(beatmapSet, noVideo)
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        this.beatmapSet = beatmapSet;
        this.noVideo = noVideo;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        try
        {
            HeaderButton? baseButton = this.FindInstanceAssignable(typeof(HeaderButton)) as HeaderButton;
            BeatmapDownloadTracker? downloadTracker = this.FindInstanceAssignable(typeof(BeatmapDownloadTracker)) as BeatmapDownloadTracker;
            ShakeContainer? shakeContainer = this.FindInstanceAssignable(typeof(ShakeContainer)) as ShakeContainer;

            if (baseButton == null || downloadTracker == null || shakeContainer == null)
            {
                BeatmapAccelLogging.Log("Unable to wire BeatmapAccel download button internals.");
                this.FadeOut();
                return;
            }

            baseButton.BackgroundColour = Color4Extensions.FromHex("#1580a6");

            if (BeatmapAccelDownloadRuntime.Downloader != null)
            {
                baseButton.Action = () =>
                {
                    try
                    {
                        if (downloadTracker.State.Value != DownloadState.NotDownloaded)
                        {
                            shakeContainer.Shake();
                            return;
                        }

                        BeatmapAccelDownloadRuntime.Downloader.Download(beatmapSet, noVideo);
                    }
                    catch (Exception e)
                    {
                        BeatmapAccelLogging.LogError(e, "Unable to start BeatmapAccel preferred-IP download");
                    }
                };
            }

            tracker = new BeatmapAccelBeatmapDownloadTracker(beatmapSet);
            tracker.State.BindValueChanged(state =>
            {
                ((Bindable<DownloadState>)downloadTracker.State).Value = state.NewValue;
            }, true);

            shakeContainer.Add(new BeatmapAccelDownloadProgressBar(beatmapSet)
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft
            });

            AddInternal((Drawable)tracker);
        }
        catch (Exception e)
        {
            BeatmapAccelLogging.LogError(e, "Failed to configure BeatmapAccel download button");
        }
    }
}
