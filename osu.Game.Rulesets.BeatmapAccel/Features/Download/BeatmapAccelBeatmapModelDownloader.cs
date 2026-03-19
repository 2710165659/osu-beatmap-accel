using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BeatmapAccel.Compatibility;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download;

public partial class BeatmapAccelBeatmapModelDownloader
{
    private static readonly IBeatmapAccelPlatformRuntime runtime = BeatmapAccelCompatibility.Current;

    public Action<Notification>? PostNotification { private get; set; }

    public event Action<ArchiveDownloadRequest<IBeatmapSetInfo>>? DownloadBegan;

    public event Action<ArchiveDownloadRequest<IBeatmapSetInfo>>? DownloadFailed;

    private readonly object downloadsLock = new();

    private readonly List<PreferredIpDownloadBeatmapSetRequest> currentDownloads = new();

    private readonly IModelImporter<BeatmapSetInfo> beatmapImporter;
    private readonly BeatmapManager? beatmapManager;
    private readonly IAPIProvider api;

    public BeatmapAccelBeatmapModelDownloader(IModelImporter<BeatmapSetInfo> beatmapImporter, IAPIProvider api)
    {
        this.beatmapImporter = beatmapImporter;
        beatmapManager = beatmapImporter as BeatmapManager;
        this.api = api;
    }

    public void SetPostNotification(Action<Notification>? postNotification)
        => PostNotification = postNotification;

    public ArchiveDownloadRequest<IBeatmapSetInfo>? GetExistingDownload(IBeatmapSetInfo model)
    {
        lock (downloadsLock)
            return currentDownloads.Find(request => request.Model.OnlineID == model.OnlineID);
    }

    public bool Download(IBeatmapSetInfo model, bool minimiseDownloadSize = false)
        => startDownload(model, minimiseDownloadSize, null);

    public bool DownloadAsUpdate(BeatmapSetInfo originalModel, bool minimiseDownloadSize = false)
        => startDownload(originalModel, minimiseDownloadSize, originalModel);

    private bool startDownload(IBeatmapSetInfo model, bool minimiseDownloadSize, BeatmapSetInfo? originalModel)
    {
        if (GetExistingDownload(model) != null)
            return false;

        var request = new PreferredIpDownloadBeatmapSetRequest(model, minimiseDownloadSize, api, CloudflareSpeedTestManager.GetPreferredIp());
        var notification = new DownloadNotification
        {
            Text = $"Downloading {request.Model.GetDisplayString()}",
        };

        request.DownloadProgressed += progress =>
        {
            notification.State = ProgressNotificationState.Active;
            notification.Progress = progress;
        };

        request.Success += filename =>
        {
            _ = Task.Run(async () =>
            {
                bool importSuccessful = false;

                try
                {
                    if (originalModel != null)
                    {
                        importSuccessful = await beatmapImporter.ImportAsUpdate(notification, new ImportTask(filename), originalModel).ConfigureAwait(false) != null;
                        await waitForLocalAvailability(request.Model.OnlineID, request.CancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var imported = (await beatmapImporter.Import(notification, new[] { new ImportTask(filename) }).ConfigureAwait(false)).ToList();
                        importSuccessful = imported.Any();

                        if (importSuccessful)
                        {
                            repairImportedSetOnlineIds(imported, request.Model.OnlineID);
                            await waitForLocalAvailability(request.Model.OnlineID, request.CancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    if (!importSuccessful)
                        DownloadFailed?.Invoke(request);

                    removeCurrentDownload(request);
                }
            });
        };

        request.Failure += error =>
        {
            removeCurrentDownload(request);
            DownloadFailed?.Invoke(request);
            notification.State = ProgressNotificationState.Cancelled;

            if (error is not OperationCanceledException)
            {
                BeatmapAccelLogging.LogError(error, $"Beatmap download failed for set {request.Model.OnlineID}");
                CloudflareSpeedTestManager.BeginFailureRecoverySpeedTest(PostNotification);
            }
        };

        notification.CancelRequested += () =>
        {
            request.CancelDownload();
            return true;
        };

        lock (downloadsLock)
            currentDownloads.Add(request);

        PostNotification?.Invoke(notification);
        DownloadBegan?.Invoke(request);
        request.Start();
        return true;
    }

    private void repairImportedSetOnlineIds(IReadOnlyList<Live<BeatmapSetInfo>> importedBeatmaps, int expectedOnlineId)
    {
        if (expectedOnlineId <= 0)
            return;

        foreach (Live<BeatmapSetInfo> imported in importedBeatmaps)
        {
            imported.PerformWrite(set =>
            {
                if (set.OnlineID > 0)
                    return;

                set.OnlineID = expectedOnlineId;
                BeatmapAccelLogging.Log($"Repaired imported beatmap set online ID to {expectedOnlineId} for {set.GetDisplayString()}.");
            });
        }
    }

    private async Task waitForLocalAvailability(int onlineId, CancellationToken cancellationToken)
    {
        if (beatmapManager == null || onlineId <= 0)
            return;

        var beatmapSet = new BeatmapSetInfo { OnlineID = onlineId };

        for (int i = 0; i < 40; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (beatmapManager.IsAvailableLocally(beatmapSet))
                return;

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        if (!beatmapManager.IsAvailableLocally(beatmapSet))
            BeatmapAccelLogging.Log($"Imported beatmap set {onlineId} did not become locally visible before the wait timeout expired.");
    }

    public void CancelAllDownloads()
    {
        PreferredIpDownloadBeatmapSetRequest[] activeDownloads;

        lock (downloadsLock)
            activeDownloads = currentDownloads.ToArray();

        foreach (PreferredIpDownloadBeatmapSetRequest request in activeDownloads)
            request.CancelDownload();
    }

    private void removeCurrentDownload(PreferredIpDownloadBeatmapSetRequest request)
    {
        lock (downloadsLock)
            currentDownloads.Remove(request);
    }

    private partial class DownloadNotification : ProgressNotification
    {
        protected override Notification CreateCompletionNotification() => new ProgressCompletionNotification
        {
            Activated = CompletionClickAction,
            IsImportant = false,
            Text = CompletionText
        };
    }

    private sealed class PreferredIpDownloadBeatmapSetRequest : ArchiveDownloadRequest<IBeatmapSetInfo>
    {
        private const string download_host = "osu.ppy.sh";
        private static readonly TimeSpan download_timeout = TimeSpan.FromSeconds(60);

        private readonly bool noVideo;
        private readonly IAPIProvider api;
        private readonly string preferredIp;
        private readonly CancellationTokenSource cancellationSource = new();
        private readonly string targetFilePath;
        private readonly Stopwatch stopwatch = new();

        private int firstProgressLogged;

        public CancellationToken CancellationToken => cancellationSource.Token;

        public PreferredIpDownloadBeatmapSetRequest(IBeatmapSetInfo set, bool noVideo, IAPIProvider api, string preferredIp)
            : base(set)
        {
            this.noVideo = noVideo;
            this.api = api;
            this.preferredIp = preferredIp;

            AttachAPI(api);

            string tempFile = Path.GetTempFileName();
            targetFilePath = Path.ChangeExtension(tempFile, ".osz");
            runtime.MoveFileOverwrite(tempFile, targetFilePath);
        }

        public void Start()
        {
            stopwatch.Start();
            BeatmapAccelLogging.Log($"Starting preferred-IP download for beatmap set {Model.OnlineID} via {(string.IsNullOrWhiteSpace(preferredIp) ? download_host : preferredIp)}.");
            _ = Task.Run(downloadAsync);
        }

        public void CancelDownload()
        {
            cancellationSource.Cancel();
            Fail(new OperationCanceledException("Request cancelled"));
            cleanupTargetFile();
        }

        protected override string Target => $@"beatmapsets/{Model.OnlineID}/download{(noVideo ? "?noVideo=1" : string.Empty)}";

        protected override string Uri => noVideo
            ? $"https://{download_host}/api/v2/beatmapsets/{Model.OnlineID}/download?noVideo=1"
            : $"https://{download_host}/api/v2/beatmapsets/{Model.OnlineID}/download";

        private async Task downloadAsync()
        {
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
                requestTimeout.CancelAfter(download_timeout);

                await runtime.DownloadFileAsync(new PreferredIpFileDownloadRequest(
                    new System.Uri(this.Uri),
                    parsePreferredIp(),
                    targetFilePath,
                    TimeSpan.FromSeconds(15),
                    createHeaders(),
                    (currentBytes, totalBytes) =>
                    {
                        if (Interlocked.Exchange(ref firstProgressLogged, 1) == 0)
                            BeatmapAccelLogging.Log($"Preferred-IP download received first progress for beatmap set {Model.OnlineID} after {stopwatch.ElapsedMilliseconds} ms.");

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                            SetProgress((float)currentBytes / totalBytes.Value);
                    }), requestTimeout.Token).ConfigureAwait(false);

                SetProgress(1);
                BeatmapAccelLogging.Log($"Preferred-IP download finished transfer for beatmap set {Model.OnlineID} in {stopwatch.ElapsedMilliseconds} ms.");
                TriggerSuccess(targetFilePath);
            }
            catch (Exception e)
            {
                cleanupTargetFile();
                Fail(e);
            }
        }

        private IPAddress? parsePreferredIp()
        {
            if (string.IsNullOrWhiteSpace(preferredIp))
                return null;

            return IPAddress.TryParse(preferredIp, out IPAddress? ipAddress) ? ipAddress : null;
        }

        private BeatmapAccelHttpHeader[] createHeaders()
        {
            return new[]
            {
                new BeatmapAccelHttpHeader("Authorization", $"Bearer {api.AccessToken}"),
                new BeatmapAccelHttpHeader("User-Agent", "osu!"),
                new BeatmapAccelHttpHeader("Accept-Language", api.Language.ToCultureCode()),
                new BeatmapAccelHttpHeader("x-api-version", api.APIVersion.ToString())
            };
        }

        private void cleanupTargetFile()
        {
            try
            {
                if (File.Exists(targetFilePath))
                    File.Delete(targetFilePath);
            }
            catch
            {
            }
        }
    }
}
