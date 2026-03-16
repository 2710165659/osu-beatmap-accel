using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online;
using osu.Game.Rulesets.ProxyAccel.Features.Injection.Utils;

namespace osu.Game.Rulesets.ProxyAccel.Features.Download.UI;

public partial class ProxyDownloadProgressBar : DownloadProgressBar
{
    private readonly IBeatmapSetInfo beatmapSetInfo;

    public ProxyDownloadProgressBar(IBeatmapSetInfo beatmapSetInfo)
        : base(beatmapSetInfo)
    {
        this.beatmapSetInfo = beatmapSetInfo;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var tracker = new ProxyBeatmapDownloadTracker(beatmapSetInfo);
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
