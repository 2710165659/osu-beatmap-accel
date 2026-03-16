using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Overlays.Notifications;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download;

public partial class BeatmapAccelBeatmapModelDownloader
{
    public Action<Notification>? PostNotification { private get; set; }

    public event Action<ArchiveDownloadRequest<IBeatmapSetInfo>>? DownloadBegan;

    public event Action<ArchiveDownloadRequest<IBeatmapSetInfo>>? DownloadFailed;

    private readonly object downloadsLock = new();

    private readonly List<PreferredIpDownloadBeatmapSetRequest> currentDownloads = new();

    private readonly IModelImporter<BeatmapSetInfo> beatmapImporter;
    private readonly IAPIProvider api;

    public BeatmapAccelBeatmapModelDownloader(IModelImporter<BeatmapSetInfo> beatmapImporter, IAPIProvider api)
    {
        this.beatmapImporter = beatmapImporter;
        this.api = api;
    }

    public ArchiveDownloadRequest<IBeatmapSetInfo>? GetExistingDownload(IBeatmapSetInfo model)
    {
        lock (downloadsLock)
            return currentDownloads.Find(request => request.Model.OnlineID == model.OnlineID);
    }

    public bool Download(IBeatmapSetInfo model, bool minimiseDownloadSize = false)
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
            Task.Factory.StartNew(async () =>
            {
                bool importSuccessful = false;

                try
                {
                    importSuccessful = (await beatmapImporter.Import(notification, new[] { new ImportTask(filename) }).ConfigureAwait(false)).Any();
                }
                finally
                {
                    if (!importSuccessful)
                        DownloadFailed?.Invoke(request);

                    removeCurrentDownload(request);
                }
            }, TaskCreationOptions.LongRunning);
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

        private readonly bool noVideo;
        private readonly IAPIProvider api;
        private readonly string preferredIp;
        private readonly CancellationTokenSource cancellationSource = new();
        private readonly string targetFilePath;
        private readonly Stopwatch stopwatch = new();

        private int firstProgressLogged;

        public PreferredIpDownloadBeatmapSetRequest(IBeatmapSetInfo set, bool noVideo, IAPIProvider api, string preferredIp)
            : base(set)
        {
            this.noVideo = noVideo;
            this.api = api;
            this.preferredIp = preferredIp;

            AttachAPI(api);

            string tempFile = Path.GetTempFileName();
            targetFilePath = Path.ChangeExtension(tempFile, ".osz");
            File.Move(tempFile, targetFilePath, overwrite: true);
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
                using var handler = createHandler();
                using var client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                using var request = new HttpRequestMessage(HttpMethod.Get, Uri);

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.AccessToken);
                request.Headers.UserAgent.ParseAdd("osu!");
                request.Headers.AcceptLanguage.ParseAdd(api.Language.ToCultureCode());
                request.Headers.TryAddWithoutValidation("x-api-version", api.APIVersion.ToString());

                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationSource.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync(cancellationSource.Token).ConfigureAwait(false);
                    throw new InvalidOperationException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} {body}");
                }

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                long currentBytes = 0;

                using Stream input = await response.Content.ReadAsStreamAsync(cancellationSource.Token).ConfigureAwait(false);
                await using FileStream output = new(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                byte[] buffer = new byte[81920];

                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellationSource.Token).ConfigureAwait(false);

                    if (read <= 0)
                        break;

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationSource.Token).ConfigureAwait(false);
                    currentBytes += read;

                    if (Interlocked.Exchange(ref firstProgressLogged, 1) == 0)
                        BeatmapAccelLogging.Log($"Preferred-IP download received first progress for beatmap set {Model.OnlineID} after {stopwatch.ElapsedMilliseconds} ms.");

                    if (totalBytes > 0)
                        SetProgress((float)currentBytes / totalBytes);
                }

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

        private SocketsHttpHandler createHandler()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(15),
            };

            if (!string.IsNullOrWhiteSpace(preferredIp) && IPAddress.TryParse(preferredIp, out IPAddress? ipAddress))
            {
                handler.ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(ipAddress, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                };
            }

            return handler;
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
