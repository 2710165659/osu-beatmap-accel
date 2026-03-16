using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online;
using osu.Game.Rulesets.BeatmapAccel.Features.Injection.Utils;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download.UI;

public partial class BeatmapAccelDownloadProgressBar : DownloadProgressBar
{
    private readonly IBeatmapSetInfo beatmapSetInfo;

    public BeatmapAccelDownloadProgressBar(IBeatmapSetInfo beatmapSetInfo)
        : base(beatmapSetInfo)
    {
        this.beatmapSetInfo = beatmapSetInfo;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var tracker = new BeatmapAccelBeatmapDownloadTracker(beatmapSetInfo);
        AddInternal(tracker);

        if (this.FindInstanceAssignable(typeof(ProgressBar)) is not ProgressBar progressBar)
            return;

        progressBar.Current.UnbindBindings();
        progressBar.Current.BindTarget = tracker.Progress;

        if (this.FindInstanceAssignable(typeof(BeatmapDownloadTracker)) is not BeatmapDownloadTracker downloadTracker)
            return;

        ((Bindable<DownloadState>)downloadTracker.State).BindTarget = tracker.State;
        ((BindableNumber<double>)downloadTracker.Progress).BindTarget = tracker.Progress;
    }
}
