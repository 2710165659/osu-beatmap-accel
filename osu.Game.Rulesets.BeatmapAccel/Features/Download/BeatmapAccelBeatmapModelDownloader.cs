using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
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

        int[] onlineBeatmapIds = model.Beatmaps.Select(beatmap => beatmap.OnlineID).Where(id => id > 0).Distinct().ToArray();
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
            // 下载传输成功说明当前优选 IP 可用，重置失败恢复的连续跳过计数。
            CloudflareSpeedTestManager.OnDownloadSucceeded();

            _ = Task.Run(async () =>
            {
                bool importSuccessful = false;

                try
                {
                    if (originalModel != null)
                    {
                        importSuccessful = await beatmapImporter.ImportAsUpdate(notification, new ImportTask(filename), originalModel).ConfigureAwait(false) != null;
                        await waitForLocalAvailability(request.Model.OnlineID, onlineBeatmapIds, request.CancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var imported = (await beatmapImporter.Import(notification, new[] { new ImportTask(filename) }).ConfigureAwait(false)).ToList();
                        importSuccessful = imported.Any();

                        if (importSuccessful)
                        {
                            repairImportedSetOnlineIds(imported, request.Model.OnlineID);
                            await waitForLocalAvailability(request.Model.OnlineID, onlineBeatmapIds, request.CancellationToken).ConfigureAwait(false);
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

            if (error is OperationCanceledException)
                return;

            BeatmapAccelLogging.LogError(error, $"Beatmap download failed for set {request.Model.OnlineID}");

            if (isNonRecoverableDownloadFailure(error))
            {
                BeatmapAccelLogging.Log($"Skipping preferred-IP recovery for non-recoverable beatmap download failure on set {request.Model.OnlineID}: {error.Message}");
                return;
            }

            CloudflareSpeedTestManager.BeginFailureRecoverySpeedTest(PostNotification);
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

    private async Task waitForLocalAvailability(int beatmapSetId, IReadOnlyList<int> onlineBeatmapIds, CancellationToken cancellationToken)
    {
        if (beatmapManager == null || onlineBeatmapIds.Count == 0)
            return;

        bool isAvailableLocally()
            => onlineBeatmapIds.Any(id => beatmapManager.IsAvailableLocally(new APIBeatmap { OnlineID = id }));

        for (int i = 0; i < 40; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isAvailableLocally())
                return;

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        if (!isAvailableLocally())
            BeatmapAccelLogging.Log($"Imported beatmap set {beatmapSetId} did not become locally visible before the wait timeout expired.");
    }

    private static bool isNonRecoverableDownloadFailure(Exception error)
    {
        if (error is not PreferredIpDownloadHttpException httpError)
            return false;

        return httpError.StatusCode is HttpStatusCode.Unauthorized
                                   or HttpStatusCode.NotFound
                                   or HttpStatusCode.TooManyRequests;
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
        // [临时诊断] 跟踪最后一次进度回调的已写入字节数与 Content-Length，用于诊断移动端"传到一半被终止"。
        private long lastProgressBytes;
        private long? lastProgressTotal;

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

                        // [临时诊断] 记录最后进度，用于失败时判断"传到一半"的确切字节位置。
                        lastProgressBytes = currentBytes;
                        lastProgressTotal = totalBytes;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                            SetProgress((float)currentBytes / totalBytes.Value);
                    }), requestTimeout.Token).ConfigureAwait(false);

                SetProgress(1);
                BeatmapAccelLogging.Log($"Preferred-IP download finished transfer for beatmap set {Model.OnlineID} in {stopwatch.ElapsedMilliseconds} ms.");
                TriggerSuccess(targetFilePath);
            }
            catch (Exception e)
            {
                // [临时诊断] 仅对"非取消"的传输中断输出诊断（用户取消/总超时取消属预期行为，不刷屏）。
                // 取消可能是 download_timeout(60s) 总超时或用户取消；非取消异常才是真正的传输中断。
                bool tokenCancelled = cancellationSource.IsCancellationRequested;

                if (!tokenCancelled && e is not OperationCanceledException)
                {
                    string typeChain = BeatmapAccelLogging.BuildExceptionTypeChain(e);

                    BeatmapAccelLogging.Log($"[DIAG] Download aborted for set {Model.OnlineID} after {stopwatch.ElapsedMilliseconds} ms. " +
                                            $"Type={typeChain}, Message={e.Message}, " +
                                            $"lastBytes={lastProgressBytes}, lastTotal={(lastProgressTotal?.ToString() ?? "<null>")}, " +
                                            $"preferredIp={(string.IsNullOrWhiteSpace(preferredIp) ? "<empty>" : preferredIp)}.", LogLevel.Important);
                }

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
