using System;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download;

public static class BeatmapAccelDownloadRuntime
{
    private static readonly object sync = new();

    public static BeatmapAccelBeatmapModelDownloader? Downloader { get; private set; }

    public static DisabledBeatmapModelDownloader? DisabledDownloader { get; private set; }

    public static void EnsureInitialized(IModelImporter<BeatmapSetInfo> beatmapImporter, IAPIProvider api, Action<Notification>? postNotification = null)
    {
        lock (sync)
        {
            Downloader ??= new BeatmapAccelBeatmapModelDownloader(beatmapImporter, api);
            DisabledDownloader ??= new DisabledBeatmapModelDownloader(beatmapImporter);
            Downloader.SetPostNotification(postNotification);
        }
    }

    public static void UpdateNotificationPoster(Action<Notification>? postNotification)
    {
        lock (sync)
            Downloader?.SetPostNotification(postNotification);
    }

    public static void Shutdown()
    {
        lock (sync)
            Downloader?.CancelAllDownloads();
    }
}

public sealed class DisabledBeatmapModelDownloader : BeatmapModelDownloader
{
    public DisabledBeatmapModelDownloader(IModelImporter<BeatmapSetInfo> beatmapImporter)
        : base(beatmapImporter, null!)
    {
    }

    protected override ArchiveDownloadRequest<IBeatmapSetInfo> CreateDownloadRequest(IBeatmapSetInfo set, bool minimiseDownloadSize)
        => throw new NotSupportedException("DisabledBeatmapModelDownloader should never create requests.");

    public override ArchiveDownloadRequest<IBeatmapSetInfo>? GetExistingDownload(IBeatmapSetInfo model)
        => null;
}
