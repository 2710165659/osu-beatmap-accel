using System;
using System.Reflection;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online;

namespace osu.Game.Rulesets.ProxyAccel.Features.Download;

public partial class ProxyBeatmapDownloadTracker : BeatmapDownloadTracker
{
    private static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly MethodInfo? attachDownloadMethod = typeof(BeatmapDownloadTracker).GetMethod("attachDownload", flags);

    public ProxyBeatmapDownloadTracker(IBeatmapSetInfo trackedItem)
        : base(trackedItem)
    {
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (PreviewTrackHandler.Downloader == null)
        {
            ProxyLogging.Log("Custom proxy downloader is not ready yet.");
            return;
        }

        PreviewTrackHandler.Downloader.DownloadBegan += onDownloadBegan;
        PreviewTrackHandler.Downloader.DownloadFailed += onDownloadFailed;
    }

    private void onDownloadBegan(ArchiveDownloadRequest<IBeatmapSetInfo> request)
    {
        if (request.Model.OnlineID != TrackedItem.OnlineID)
            return;

        attachDownloadMethod?.Invoke(this, new object?[] { request });
    }

    private void onDownloadFailed(ArchiveDownloadRequest<IBeatmapSetInfo> request)
    {
        if (request.Model.OnlineID != TrackedItem.OnlineID)
            return;

        attachDownloadMethod?.Invoke(this, new object?[] { null });
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (PreviewTrackHandler.Downloader == null)
            return;

        PreviewTrackHandler.Downloader.DownloadBegan -= onDownloadBegan;
        PreviewTrackHandler.Downloader.DownloadFailed -= onDownloadFailed;
    }
}
