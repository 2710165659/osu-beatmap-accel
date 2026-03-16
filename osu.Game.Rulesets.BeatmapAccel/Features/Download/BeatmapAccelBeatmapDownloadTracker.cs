using System;
using System.Reflection;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download;

public partial class BeatmapAccelBeatmapDownloadTracker : BeatmapDownloadTracker
{
    private static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly MethodInfo? attachDownloadMethod = typeof(BeatmapDownloadTracker).GetMethod("attachDownload", flags);

    public BeatmapAccelBeatmapDownloadTracker(IBeatmapSetInfo trackedItem)
        : base(trackedItem)
    {
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (PreviewTrackHandler.Downloader == null)
        {
            BeatmapAccelLogging.Log("BeatmapAccel downloader is not ready yet.");
            return;
        }

        PreviewTrackHandler.Downloader.DownloadBegan += onDownloadBegan;
        PreviewTrackHandler.Downloader.DownloadFailed += onDownloadFailed;
        attachCurrentDownload();
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

        attachCurrentDownload();
    }

    private void attachCurrentDownload()
    {
        var beatmapSetInfo = new BeatmapSetInfo { OnlineID = TrackedItem.OnlineID };
        ArchiveDownloadRequest<IBeatmapSetInfo>? request = PreviewTrackHandler.Downloader?.GetExistingDownload(beatmapSetInfo);

        attachDownloadMethod?.Invoke(this, new object?[] { request });
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
