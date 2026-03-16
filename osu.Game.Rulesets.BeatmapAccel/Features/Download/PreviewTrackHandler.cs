using System;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BeatmapAccel.Features.Download.UI;
using osu.Game.Rulesets.BeatmapAccel.Features.Injection;
using osu.Game.Rulesets.BeatmapAccel.Features.Injection.Utils;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download;

public partial class PreviewTrackHandler : AbstractHandler
{
    private readonly Bindable<PreviewTrackManager.TrackManagerPreviewTrack?> previewTrack = new();
    private readonly object injectLock = new();

    private FieldInfo? previewTrackFieldInfo;
    private int? currentBeatmapSetId;
    private BeatmapAccelDownloadOverlay? currentOverlay;

    [Resolved]
    private PreviewTrackManager previewTrackManager { get; set; } = null!;

    [Resolved]
    private OsuGame game { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private BeatmapManager? beatmapManager { get; set; }

    [Resolved(canBeNull: true)]
    private IAPIProvider? apiProvider { get; set; }

    public static BeatmapAccelBeatmapModelDownloader? Downloader { get; private set; }

    [BackgroundDependencyLoader]
    private void load(INotificationOverlay notificationOverlay)
    {
        previewTrack.BindValueChanged(onPreviewTrackChanged);

        if (!tryLocatePreviewTrackField())
        {
            notificationOverlay.Post(new SimpleNotification
            {
                Text = "BeatmapAccel: unable to locate preview track manager."
            });
            return;
        }

        if (beatmapManager == null || apiProvider == null)
        {
            notificationOverlay.Post(new SimpleNotification
            {
                Text = "BeatmapAccel: required beatmap download dependencies are missing."
            });
            return;
        }

        setupDownloader(beatmapManager, apiProvider);
        if (Downloader != null)
            Downloader.PostNotification = notificationOverlay.Post;

        CloudflareSpeedTestManager.ScheduleToMainThread ??= action => Schedule(action);
        CloudflareSpeedTestManager.BeginStartupSpeedTest();
    }

    protected override void Update()
    {
        base.Update();

        if (previewTrackFieldInfo == null || DebugUtils.IsDebugBuild)
            return;

        try
        {
            previewTrack.Value = (PreviewTrackManager.TrackManagerPreviewTrack?)previewTrackFieldInfo.GetValue(previewTrackManager);
        }
        catch (Exception e)
        {
            BeatmapAccelLogging.LogError(e, "Failed to poll preview track");
            previewTrackFieldInfo = null;
        }
    }

    private bool tryLocatePreviewTrackField()
    {
        lock (injectLock)
        {
            previewTrackFieldInfo ??= previewTrackManager.FindFieldInstanceAssignable(typeof(PreviewTrackManager.TrackManagerPreviewTrack));

            if (previewTrackFieldInfo == null)
                return false;

            previewTrack.Value = (PreviewTrackManager.TrackManagerPreviewTrack?)previewTrackFieldInfo.GetValue(previewTrackManager);
            return true;
        }
    }

    private APIBeatmapSet? getApiBeatmapSet(PreviewTrackManager.TrackManagerPreviewTrack preview)
        => preview.FindInstanceAssignable(typeof(IBeatmapSetInfo)) as APIBeatmapSet;

    private void onPreviewTrackChanged(ValueChangedEvent<PreviewTrackManager.TrackManagerPreviewTrack?> change)
    {
        currentOverlay?.Hide();
        currentOverlay?.Expire();
        currentOverlay = null;

        if (change.NewValue == null)
        {
            currentBeatmapSetId = null;
            return;
        }

        APIBeatmapSet? apiSet = getApiBeatmapSet(change.NewValue);

        if (apiSet == null)
            return;

        if (currentBeatmapSetId == apiSet.OnlineID)
            return;

        currentBeatmapSetId = apiSet.OnlineID;
        currentOverlay = new BeatmapAccelDownloadOverlay(apiSet);
        game.Add(currentOverlay);
    }

    private static void setupDownloader(IModelImporter<BeatmapSetInfo> beatmapImporter, IAPIProvider api)
        => Downloader = new BeatmapAccelBeatmapModelDownloader(beatmapImporter, api);
}
