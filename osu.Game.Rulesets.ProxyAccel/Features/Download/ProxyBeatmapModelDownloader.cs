using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays;
using osu.Game.Rulesets.ProxyAccel.Configuration;
using System;

namespace osu.Game.Rulesets.ProxyAccel.Features.Download;

public class ProxyBeatmapModelDownloader : BeatmapModelDownloader
{
    public ProxyBeatmapModelDownloader(IModelImporter<BeatmapSetInfo> beatmapImporter, IAPIProvider api)
        : base(beatmapImporter, api)
    {
    }

    protected override ArchiveDownloadRequest<IBeatmapSetInfo> CreateDownloadRequest(IBeatmapSetInfo set, bool minimiseDownloadSize)
        => new ProxyDownloadBeatmapSetRequest(set, minimiseDownloadSize);

    public override ArchiveDownloadRequest<IBeatmapSetInfo>? GetExistingDownload(IBeatmapSetInfo model)
        => CurrentDownloads.Find(request => request.Model.OnlineID == model.OnlineID);

    public void AttachNotificationOverlay(INotificationOverlay notificationOverlay)
        => PostNotification += notificationOverlay.Post;

    private class ProxyDownloadBeatmapSetRequest : DownloadBeatmapSetRequest
    {
        private readonly bool noVideo;

        public ProxyDownloadBeatmapSetRequest(IBeatmapSetInfo set, bool noVideo)
            : base(set, noVideo)
        {
            this.noVideo = noVideo;
        }

        protected override string Target => $@"proxy-beatmapsets/{Model.OnlineID}/download{(noVideo ? "?noVideo=1" : string.Empty)}";

        protected override string Uri
        {
            get
            {
                string workerBaseUrl = ProxyAccelRulesetConfigManager.Instance?.GetWorkerBaseUrl()
                                       ?? ProxyAccelRulesetConfigManager.DefaultWorkerBaseUrl;

                if (string.IsNullOrWhiteSpace(workerBaseUrl))
                    throw new InvalidOperationException("ProxyAccel worker URL is not configured.");

                return noVideo
                    ? $"{workerBaseUrl}/beatmapsets/{Model.OnlineID}/download?noVideo=1"
                    : $"{workerBaseUrl}/beatmapsets/{Model.OnlineID}/download";
            }
        }
    }
}
