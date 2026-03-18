using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Beatmaps.Drawables.Cards.Buttons;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Overlays;
using osu.Game.Overlays.BeatmapSet.Buttons;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BeatmapAccel.Configuration;
using osu.Game.Rulesets.BeatmapAccel.Features.Injection;
using osu.Game.Screens.OnlinePlay.DailyChallenge;
using osu.Game.Screens.OnlinePlay.Matchmaking.Match;
using osu.Game.Screens.OnlinePlay.Multiplayer.Match;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using UiDownloadButton = osu.Game.Graphics.UserInterface.DownloadButton;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download;

public partial class GlobalBeatmapDownloadInterceptor : AbstractHandler
{
    private const double scan_interval = 1000;
    private const string ranked_play_screen_type_name = "osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay.RankedPlayScreen";

    private static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly PropertyInfo? internalChildrenProperty = typeof(CompositeDrawable).GetProperty("InternalChildren", flags);
    private static readonly FieldInfo? noVideoField = typeof(DownloadBeatmapSetRequest).GetField("noVideo", BindingFlags.Instance | BindingFlags.NonPublic);

    [Resolved(canBeNull: true)]
    private BeatmapManager? beatmapManager { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapLookupCache? beatmapLookupCache { get; set; }

    [Resolved(canBeNull: true)]
    private IAPIProvider? apiProvider { get; set; }

    [Resolved(canBeNull: true)]
    private INotificationOverlay? notifications { get; set; }

    [Resolved(canBeNull: true)]
    private OsuConfigManager? osuConfig { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapModelDownloader? originalDownloader { get; set; }

    private IBindable<bool>? interceptAllDownloads;
    private double lastScanTime;

    private readonly Dictionary<object, ActionPatch> actionPatches = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, MemberPatch> downloaderMemberPatches = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BeatmapDownloadTracker, TrackerBridge> trackerBridges = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> automaticDownloadAttempts = new(ReferenceEqualityComparer.Instance);

    [BackgroundDependencyLoader]
    private void load()
    {
        if (beatmapManager == null || apiProvider == null)
        {
            BeatmapAccelLogging.Log("BeatmapAccel global interceptor is missing required download dependencies.");
            return;
        }

        BeatmapAccelDownloadRuntime.EnsureInitialized(beatmapManager, apiProvider, notifications == null ? null : notifications.Post);

        if (originalDownloader != null)
            originalDownloader.DownloadBegan += onOriginalDownloadBegan;

        CloudflareSpeedTestManager.ScheduleToMainThread ??= action => Schedule(action);
        CloudflareSpeedTestManager.BeginStartupSpeedTest();

        if (BeatmapAccelRulesetConfigManager.Instance == null)
            return;

        interceptAllDownloads = BeatmapAccelRulesetConfigManager.Instance.GetBindable<bool>(BeatmapAccelSetting.InterceptAllBeatmapDownloads);
        interceptAllDownloads.BindValueChanged(onInterceptionChanged, true);
    }

    protected override void Update()
    {
        base.Update();

        BeatmapAccelDownloadRuntime.UpdateNotificationPoster(notifications == null ? null : notifications.Post);

        if (interceptAllDownloads?.Value != true || BeatmapAccelDownloadRuntime.Downloader == null)
            return;

        if (Time.Current - lastScanTime < scan_interval)
            return;

        lastScanTime = Time.Current;
        scanAndPatch();
    }

    private void onInterceptionChanged(ValueChangedEvent<bool> change)
    {
        if (change.NewValue)
        {
            lastScanTime = 0;
            scanAndPatch();
        }
        else
            restorePatchedState();
    }

    private void scanAndPatch()
    {
        if (Parent is not Drawable root)
            return;

        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Drawable>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            Drawable current = stack.Pop();
            inspectDrawable(current, seen);

            if (current is not CompositeDrawable composite || internalChildrenProperty?.GetValue(composite) is not IEnumerable children)
                continue;

            foreach (object? child in children)
            {
                if (child is Drawable drawable)
                    stack.Push(drawable);
            }
        }

        cleanupStaleState(seen);
    }

    private void inspectDrawable(Drawable drawable, HashSet<object> seen)
    {
        switch (drawable)
        {
            case HeaderDownloadButton headerDownloadButton:
                patchHeaderDownloadButton(headerDownloadButton, seen);
                break;

            case BeatmapDownloadButton beatmapDownloadButton:
                patchBeatmapDownloadButton(beatmapDownloadButton, seen);
                break;

            case DownloadButton downloadButton:
                patchCardDownloadButton(downloadButton, seen);
                break;

            case BeatmapCard beatmapCard:
                patchBeatmapCard(beatmapCard, seen);
                break;

            case BeatmapDownloadTracker tracker when tracker is not BeatmapAccelBeatmapDownloadTracker:
                seen.Add(tracker);
                ensureTrackerBridge(tracker);
                break;

            case SoloSpectatorScreen soloSpectatorScreen:
                seen.Add(soloSpectatorScreen);
                disableDownloaderMember(soloSpectatorScreen, "beatmapDownloader");
                handleSoloSpectatorAutomaticDownload(soloSpectatorScreen);
                break;

            case DailyChallengeIntro dailyChallengeIntro:
                seen.Add(dailyChallengeIntro);
                handleDailyChallengeAutomaticDownload(dailyChallengeIntro);
                break;

            case ScreenMatchmaking matchmakingScreen:
                seen.Add(matchmakingScreen);
                disableDownloaderMember(matchmakingScreen, "beatmapDownloader");
                handleMatchmakingAutomaticDownload(matchmakingScreen);
                break;

            case MultiplayerSpectateButton multiplayerSpectateButton:
                seen.Add(multiplayerSpectateButton);
                disableDownloaderMember(multiplayerSpectateButton, "beatmapDownloader");
                handleMultiplayerSpectateAutomaticDownload(multiplayerSpectateButton);
                break;

            case MissingBeatmapNotification missingBeatmapNotification:
                seen.Add(missingBeatmapNotification);
                disableDownloaderMember(missingBeatmapNotification, "beatmapDownloader");
                handleMissingBeatmapNotification(missingBeatmapNotification);
                break;

            case PanelUpdateBeatmapButton panelUpdateBeatmapButton:
                patchPanelUpdateBeatmapButton(panelUpdateBeatmapButton, seen);
                break;
        }

        if (isTypeOrSubclass(drawable, ranked_play_screen_type_name))
        {
            seen.Add(drawable);
            disableDownloaderMember(drawable, "beatmapDownloader");
            handleRankedPlayAutomaticDownload(drawable);
        }
    }

    private void patchHeaderDownloadButton(HeaderDownloadButton headerDownloadButton, HashSet<object> seen)
    {
        HeaderButton? button = getFieldValue<HeaderButton>(headerDownloadButton, "button");
        BeatmapDownloadTracker? tracker = getFieldValue<BeatmapDownloadTracker>(headerDownloadButton, "downloadTracker");
        APIBeatmapSet? beatmapSet = getFieldValue<APIBeatmapSet>(headerDownloadButton, "beatmapSet");
        bool? noVideo = getFieldValue<bool>(headerDownloadButton, "noVideo");

        if (button == null || tracker == null || beatmapSet == null || noVideo == null)
            return;

        seen.Add(button);

        if (!actionPatches.ContainsKey(button))
            actionPatches[button] = new ActionPatch(button, button.Action);

        button.Action = () =>
        {
            if (tracker.State.Value == DownloadState.NotDownloaded)
            {
                BeatmapAccelDownloadRuntime.Downloader?.Download(beatmapSet, noVideo.Value);
                return;
            }

            actionPatches[button].OriginalAction?.Invoke();
        };
    }

    private void patchBeatmapDownloadButton(BeatmapDownloadButton beatmapDownloadButton, HashSet<object> seen)
    {
        UiDownloadButton? button = getFieldValue<UiDownloadButton>(beatmapDownloadButton, "button");
        BeatmapDownloadTracker? tracker = getFieldValue<BeatmapDownloadTracker>(beatmapDownloadButton, "DownloadTracker");
        IBeatmapSetInfo? beatmapSet = getFieldValue<IBeatmapSetInfo>(beatmapDownloadButton, "beatmapSet");
        Bindable<bool>? noVideoSetting = getFieldValue<Bindable<bool>>(beatmapDownloadButton, "noVideoSetting");

        if (button == null || tracker == null || beatmapSet == null || noVideoSetting == null)
            return;

        seen.Add(button);

        if (!actionPatches.ContainsKey(button))
            actionPatches[button] = new ActionPatch(button, button.Action);

        button.Action = () =>
        {
            if (tracker.State.Value == DownloadState.NotDownloaded)
            {
                BeatmapAccelDownloadRuntime.Downloader?.Download(beatmapSet, noVideoSetting.Value);
                return;
            }

            actionPatches[button].OriginalAction?.Invoke();
        };
    }

    private void patchCardDownloadButton(DownloadButton downloadButton, HashSet<object> seen)
    {
        APIBeatmapSet? beatmapSet = getFieldValue<APIBeatmapSet>(downloadButton, "beatmapSet");
        Bindable<bool>? preferNoVideo = getFieldValue<Bindable<bool>>(downloadButton, "preferNoVideo");

        if (beatmapSet == null || preferNoVideo == null)
            return;

        seen.Add(downloadButton);

        if (!actionPatches.ContainsKey(downloadButton))
            actionPatches[downloadButton] = new ActionPatch(downloadButton, downloadButton.Action);

        downloadButton.Action = () =>
        {
            if (downloadButton.State.Value != DownloadState.NotDownloaded || beatmapSet.Availability.DownloadDisabled)
                return;

            BeatmapAccelDownloadRuntime.Downloader?.Download(beatmapSet, preferNoVideo.Value);
        };
    }

    private void patchBeatmapCard(BeatmapCard beatmapCard, HashSet<object> seen)
    {
        seen.Add(beatmapCard);

        if (!actionPatches.ContainsKey(beatmapCard))
            actionPatches[beatmapCard] = new ActionPatch(beatmapCard, beatmapCard.Action);

        beatmapCard.Action = () =>
        {
            BeatmapDownloadTracker? tracker = getFieldValue<BeatmapDownloadTracker>(beatmapCard, "DownloadTracker");
            bool shiftPressed = getFieldValue<InputManager>(beatmapCard, "containingInputManager")?.CurrentState.Keyboard.ShiftPressed == true;

            if (shiftPressed)
            {
                switch (tracker?.State.Value)
                {
                    case DownloadState.NotDownloaded:
                        if (!beatmapCard.BeatmapSet.Availability.DownloadDisabled)
                        {
                            bool preferNoVideo = getFieldValue<Bindable<bool>>(beatmapCard, "preferNoVideo")?.Value == true;
                            BeatmapAccelDownloadRuntime.Downloader?.Download(beatmapCard.BeatmapSet, preferNoVideo);
                        }

                        break;

                    case DownloadState.LocallyAvailable:
                        getMemberValue<OsuGame>(beatmapCard, "game")?.PresentBeatmap(beatmapCard.BeatmapSet);
                        break;
                }

                return;
            }

            getMemberValue<BeatmapSetOverlay>(beatmapCard, "beatmapSetOverlay")?.FetchAndShowBeatmapSet(beatmapCard.BeatmapSet.OnlineID);
        };
    }

    private void patchPanelUpdateBeatmapButton(PanelUpdateBeatmapButton panelUpdateBeatmapButton, HashSet<object> seen)
    {
        seen.Add(panelUpdateBeatmapButton);

        if (!actionPatches.ContainsKey(panelUpdateBeatmapButton))
            actionPatches[panelUpdateBeatmapButton] = new ActionPatch(panelUpdateBeatmapButton, panelUpdateBeatmapButton.Action);

        panelUpdateBeatmapButton.Action = () =>
        {
            BeatmapSetInfo? beatmapSet = getFieldValue<BeatmapSetInfo>(panelUpdateBeatmapButton, "beatmapSet");
            Bindable<bool>? preferNoVideo = getFieldValue<Bindable<bool>>(panelUpdateBeatmapButton, "preferNoVideo");
            IAPIProvider? api = getMemberValue<IAPIProvider>(panelUpdateBeatmapButton, "api");
            LoginOverlay? loginOverlay = getMemberValue<LoginOverlay>(panelUpdateBeatmapButton, "loginOverlay");
            IDialogOverlay? dialogOverlay = getMemberValue<IDialogOverlay>(panelUpdateBeatmapButton, "dialogOverlay");

            if (beatmapSet == null || api == null || BeatmapAccelDownloadRuntime.Downloader == null)
                return;

            if (!api.IsLoggedIn)
            {
                loginOverlay?.Show();
                return;
            }

            bool updateConfirmed = getFieldValue<bool>(panelUpdateBeatmapButton, "updateConfirmed");

            if (dialogOverlay != null && beatmapSet.Status == BeatmapOnlineStatus.LocallyModified && !updateConfirmed)
            {
                dialogOverlay.Push(new UpdateLocalConfirmationDialog(() =>
                {
                    setMemberValue(getMember(panelUpdateBeatmapButton.GetType(), "updateConfirmed")!, panelUpdateBeatmapButton, true);
                    panelUpdateBeatmapButton.Action?.Invoke();
                }));

                return;
            }

            setMemberValue(getMember(panelUpdateBeatmapButton.GetType(), "updateConfirmed")!, panelUpdateBeatmapButton, false);

            if (BeatmapAccelDownloadRuntime.Downloader.DownloadAsUpdate(beatmapSet, preferNoVideo?.Value == true))
                attachPanelUpdateProgress(panelUpdateBeatmapButton, beatmapSet);
        };
    }

    private void ensureTrackerBridge(BeatmapDownloadTracker tracker)
    {
        if (BeatmapAccelDownloadRuntime.Downloader == null || trackerBridges.ContainsKey(tracker))
            return;

        trackerBridges[tracker] = new TrackerBridge(tracker, BeatmapAccelDownloadRuntime.Downloader, action => Schedule(action));
    }

    private void disableDownloaderMember(object owner, string memberName)
    {
        if (BeatmapAccelDownloadRuntime.DisabledDownloader == null || downloaderMemberPatches.ContainsKey(owner))
            return;

        MemberInfo? member = getMember(owner.GetType(), memberName);

        if (member == null)
            return;

        object? currentValue = getMemberValue(member, owner);

        if (currentValue == null || ReferenceEquals(currentValue, BeatmapAccelDownloadRuntime.DisabledDownloader))
            return;

        if (currentValue is not BeatmapModelDownloader)
            return;

        try
        {
            setMemberValue(member, owner, BeatmapAccelDownloadRuntime.DisabledDownloader);
            downloaderMemberPatches[owner] = new MemberPatch(owner, member, currentValue);
        }
        catch (Exception e)
        {
            BeatmapAccelLogging.LogError(e, $"Failed to replace downloader member {memberName} on {owner.GetType().Name}");
        }
    }

    private void handleSoloSpectatorAutomaticDownload(SoloSpectatorScreen screen)
    {
        var automaticDownload = getFieldValue<SettingsCheckbox>(screen, "automaticDownload");
        APIBeatmapSet? beatmapSet = getFieldValue<APIBeatmapSet>(screen, "beatmapSet");

        if (automaticDownload is not IHasCurrentValue<bool> toggle || beatmapSet == null || !toggle.Current.Value)
            return;

        requestAutomaticDownload(screen, beatmapSet.OnlineID, beatmapSet, false);
    }

    private void handleDailyChallengeAutomaticDownload(DailyChallengeIntro intro)
    {
        if (osuConfig?.Get<bool>(OsuSetting.AutomaticallyDownloadMissingBeatmaps) != true)
            return;

        PlaylistItem? item = getFieldValue<PlaylistItem>(intro, "item");
        IBeatmapSetInfo? beatmapSet = item?.Beatmap.BeatmapSet;

        if (beatmapSet == null)
            return;

        requestAutomaticDownload(intro, beatmapSet.OnlineID, beatmapSet, osuConfig.Get<bool>(OsuSetting.PreferNoVideo));
    }

    private void handleMatchmakingAutomaticDownload(ScreenMatchmaking screen)
    {
        MultiplayerClient? client = getMemberValue<MultiplayerClient>(screen, "client");

        if (client?.Room?.CurrentPlaylistItem == null)
            return;

        queueLookupDownload(screen, client.Room.CurrentPlaylistItem.BeatmapID, osuConfig?.Get<bool>(OsuSetting.PreferNoVideo) == true);
    }

    private void handleRankedPlayAutomaticDownload(object screen)
    {
        MultiplayerClient? client = getMemberValue<MultiplayerClient>(screen, "client");

        if (client?.Room?.CurrentPlaylistItem == null)
            return;

        queueLookupDownload(screen, client.Room.CurrentPlaylistItem.BeatmapID, osuConfig?.Get<bool>(OsuSetting.PreferNoVideo) == true);
    }

    private void handleMultiplayerSpectateAutomaticDownload(MultiplayerSpectateButton button)
    {
        if (osuConfig?.Get<bool>(OsuSetting.AutomaticallyDownloadMissingBeatmaps) != true)
            return;

        MultiplayerClient? client = getMemberValue<MultiplayerClient>(button, "client");

        if (client?.Room?.CurrentPlaylistItem == null || client.LocalUser?.State != MultiplayerUserState.Spectating)
            return;

        queueLookupDownload(button, client.Room.CurrentPlaylistItem.BeatmapID, false);
    }

    private void handleMissingBeatmapNotification(MissingBeatmapNotification notification)
    {
        bool? autoDownload = getMemberValue<Bindable<bool>>(notification, "autoDownloadConfig")?.Value;
        APIBeatmapSet? beatmapSet = getFieldValue<APIBeatmapSet>(notification, "beatmapSetInfo");
        bool preferNoVideo = getMemberValue<Bindable<bool>>(notification, "noVideoSetting")?.Value ?? false;

        if (autoDownload != true || beatmapSet == null)
            return;

        requestAutomaticDownload(notification, beatmapSet.OnlineID, beatmapSet, preferNoVideo);
    }

    private void attachPanelUpdateProgress(PanelUpdateBeatmapButton panelUpdateBeatmapButton, BeatmapSetInfo beatmapSet)
    {
        Box? progressFill = getFieldValue<Box>(panelUpdateBeatmapButton, "progressFill");
        ArchiveDownloadRequest<IBeatmapSetInfo>? download = BeatmapAccelDownloadRuntime.Downloader?.GetExistingDownload(beatmapSet);

        if (progressFill == null || download == null)
            return;

        panelUpdateBeatmapButton.Enabled.Value = false;

        download.DownloadProgressed += progress => Schedule(() => progressFill.ResizeWidthTo(progress, 100, Easing.OutQuint));
        download.Success += _ => Schedule(() =>
        {
            panelUpdateBeatmapButton.Enabled.Value = true;
            progressFill.ResizeWidthTo(0, 100, Easing.OutQuint);
        });
        download.Failure += _ => Schedule(() =>
        {
            panelUpdateBeatmapButton.Enabled.Value = true;
            progressFill.ResizeWidthTo(0, 100, Easing.OutQuint);
        });
    }

    private void onOriginalDownloadBegan(ArchiveDownloadRequest<IBeatmapSetInfo> request)
    {
        if (interceptAllDownloads?.Value != true || BeatmapAccelDownloadRuntime.Downloader == null)
            return;

        bool noVideo = noVideoField?.GetValue(request) as bool? == true;

        bool rerouted = request.Model is BeatmapSetInfo originalModel && originalModel.IsManaged
            ? BeatmapAccelDownloadRuntime.Downloader.DownloadAsUpdate(originalModel, noVideo)
            : BeatmapAccelDownloadRuntime.Downloader.Download(request.Model, noVideo);

        if (!rerouted && BeatmapAccelDownloadRuntime.Downloader.GetExistingDownload(request.Model) == null)
            return;

        request.Cancel();
        Scheduler.AddDelayed(() => reattachTrackedDownload(request.Model.OnlineID), 50);
    }

    private void reattachTrackedDownload(int onlineId)
    {
        foreach (var pair in trackerBridges)
        {
            if (pair.Key.TrackedItem.OnlineID == onlineId)
                pair.Value.ReattachCurrentDownload();
        }
    }

    private void queueLookupDownload(object owner, int beatmapId, bool preferNoVideo)
    {
        if (beatmapLookupCache == null || beatmapManager == null || BeatmapAccelDownloadRuntime.Downloader == null)
            return;

        if (automaticDownloadAttempts.TryGetValue(owner, out int previousAttempt) && previousAttempt == beatmapId)
            return;

        if (beatmapManager.IsAvailableLocally(new APIBeatmap { OnlineID = beatmapId }))
        {
            automaticDownloadAttempts[owner] = beatmapId;
            return;
        }

        automaticDownloadAttempts[owner] = beatmapId;

        beatmapLookupCache.GetBeatmapAsync(beatmapId, CancellationToken.None).ContinueWith(task => Schedule(() =>
        {
            APIBeatmapSet? beatmapSet = task.GetResultSafely()?.BeatmapSet;

            if (beatmapSet == null)
                return;

            requestAutomaticDownload(owner, beatmapSet.OnlineID, beatmapSet, preferNoVideo);
        }));
    }

    private void requestAutomaticDownload(object owner, int beatmapSetId, IBeatmapSetInfo beatmapSet, bool preferNoVideo)
    {
        if (beatmapManager == null || BeatmapAccelDownloadRuntime.Downloader == null)
            return;

        if (automaticDownloadAttempts.TryGetValue(owner, out int previousAttempt) && previousAttempt == beatmapSetId)
            return;

        if (beatmapManager.IsAvailableLocally(new BeatmapSetInfo { OnlineID = beatmapSetId }))
        {
            automaticDownloadAttempts[owner] = beatmapSetId;
            return;
        }

        if (BeatmapAccelDownloadRuntime.Downloader.GetExistingDownload(new BeatmapSetInfo { OnlineID = beatmapSetId }) != null)
        {
            automaticDownloadAttempts[owner] = beatmapSetId;
            return;
        }

        if (BeatmapAccelDownloadRuntime.Downloader.Download(beatmapSet, preferNoVideo))
            automaticDownloadAttempts[owner] = beatmapSetId;
    }

    private void cleanupStaleState(HashSet<object> seen)
    {
        foreach (var pair in actionPatches.ToArray())
        {
            if (seen.Contains(pair.Key))
                continue;

            pair.Value.Restore();
            actionPatches.Remove(pair.Key);
        }

        foreach (var pair in downloaderMemberPatches.ToArray())
        {
            if (seen.Contains(pair.Key))
                continue;

            restoreMemberPatch(pair.Value);
            downloaderMemberPatches.Remove(pair.Key);
        }

        foreach (var pair in trackerBridges.ToArray())
        {
            if (seen.Contains(pair.Key))
                continue;

            pair.Value.Dispose();
            trackerBridges.Remove(pair.Key);
        }

        foreach (object owner in automaticDownloadAttempts.Keys.ToArray())
        {
            if (!seen.Contains(owner))
                automaticDownloadAttempts.Remove(owner);
        }
    }

    private void restorePatchedState()
    {
        foreach (ActionPatch patch in actionPatches.Values)
            patch.Restore();

        actionPatches.Clear();

        foreach (MemberPatch patch in downloaderMemberPatches.Values)
            restoreMemberPatch(patch);

        downloaderMemberPatches.Clear();

        foreach (TrackerBridge bridge in trackerBridges.Values)
            bridge.Dispose();

        trackerBridges.Clear();
        automaticDownloadAttempts.Clear();
    }

    private void restoreMemberPatch(MemberPatch patch)
    {
        try
        {
            setMemberValue(patch.Member, patch.Owner, patch.OriginalValue);
        }
        catch (Exception e)
        {
            BeatmapAccelLogging.LogError(e, $"Failed to restore downloader member {patch.Member.Name} on {patch.Owner.GetType().Name}");
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (originalDownloader != null)
            originalDownloader.DownloadBegan -= onOriginalDownloadBegan;

        BeatmapAccelDownloadRuntime.Shutdown();
        restorePatchedState();
    }

    private static MemberInfo? getMember(Type type, string memberName)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(memberName, flags);

            if (field != null)
                return field;

            PropertyInfo? property = current.GetProperty(memberName, flags);

            if (property != null)
                return property;
        }

        return null;
    }

    private static object? getMemberValue(MemberInfo member, object owner) =>
        member switch
        {
            FieldInfo field => field.GetValue(owner),
            PropertyInfo property => property.GetValue(owner),
            _ => null
        };

    private static void setMemberValue(MemberInfo member, object owner, object value)
    {
        switch (member)
        {
            case FieldInfo field:
                field.SetValue(owner, value);
                break;

            case PropertyInfo property:
                property.SetValue(owner, value);
                break;
        }
    }

    private static T? getFieldValue<T>(object owner, string fieldName)
    {
        for (Type? current = owner.GetType(); current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(fieldName, flags);

            if (field == null)
                continue;

            return (T?)field.GetValue(owner);
        }

        return default;
    }

    private static T? getMemberValue<T>(object owner, string memberName)
    {
        MemberInfo? member = getMember(owner.GetType(), memberName);
        return member == null ? default : (T?)getMemberValue(member, owner);
    }

    private static bool isTypeOrSubclass(object owner, string fullTypeName)
    {
        for (Type? current = owner.GetType(); current != null; current = current.BaseType)
        {
            if (current.FullName == fullTypeName)
                return true;
        }

        return false;
    }

    private sealed record MemberPatch(object Owner, MemberInfo Member, object OriginalValue);

    private sealed class ActionPatch
    {
        private static readonly MethodInfo? cardUpdateStateMethod = typeof(DownloadButton).GetMethod("updateState", flags);

        private readonly object target;

        public Action? OriginalAction { get; }

        public ActionPatch(HeaderButton button, Action? originalAction)
        {
            target = button;
            OriginalAction = originalAction;
        }

        public ActionPatch(UiDownloadButton button, Action? originalAction)
        {
            target = button;
            OriginalAction = originalAction;
        }

        public ActionPatch(DownloadButton button, Action? originalAction)
        {
            target = button;
            OriginalAction = originalAction;
        }

        public ActionPatch(BeatmapCard card, Action? originalAction)
        {
            target = card;
            OriginalAction = originalAction;
        }

        public ActionPatch(PanelUpdateBeatmapButton button, Action? originalAction)
        {
            target = button;
            OriginalAction = originalAction;
        }

        public void Restore()
        {
            switch (target)
            {
                case HeaderButton headerButton:
                    headerButton.Action = OriginalAction;
                    break;

                case UiDownloadButton downloadButton:
                    downloadButton.Action = OriginalAction;
                    break;

                case DownloadButton cardDownloadButton:
                    cardDownloadButton.Action = OriginalAction;
                    cardUpdateStateMethod?.Invoke(cardDownloadButton, null);
                    break;

                case BeatmapCard beatmapCard:
                    beatmapCard.Action = OriginalAction;
                    break;

                case PanelUpdateBeatmapButton panelUpdateBeatmapButton:
                    panelUpdateBeatmapButton.Action = OriginalAction;
                    break;
            }
        }
    }

    private sealed class TrackerBridge : IDisposable
    {
        private readonly BeatmapDownloadTracker tracker;
        private readonly BeatmapAccelBeatmapModelDownloader downloader;
        private readonly Action<Action> schedule;
        private readonly MethodInfo? attachDownloadMethod;

        public TrackerBridge(BeatmapDownloadTracker tracker, BeatmapAccelBeatmapModelDownloader downloader, Action<Action> schedule)
        {
            this.tracker = tracker;
            this.downloader = downloader;
            this.schedule = schedule;

            attachDownloadMethod = typeof(BeatmapDownloadTracker).GetMethod("attachDownload", flags);

            downloader.DownloadBegan += onDownloadBegan;
            attachCurrentDownload();
        }

        private void onDownloadBegan(ArchiveDownloadRequest<IBeatmapSetInfo> request)
        {
            if (request.Model.OnlineID != tracker.TrackedItem.OnlineID)
                return;

            schedule(() => attachDownloadMethod?.Invoke(tracker, new object?[] { request }));
        }

        private void attachCurrentDownload()
        {
            var beatmapSetInfo = new BeatmapSetInfo { OnlineID = tracker.TrackedItem.OnlineID };
            ArchiveDownloadRequest<IBeatmapSetInfo>? request = downloader.GetExistingDownload(beatmapSetInfo);

            if (request != null)
                schedule(() => attachDownloadMethod?.Invoke(tracker, new object?[] { request }));
        }

        public void ReattachCurrentDownload()
        {
            attachCurrentDownload();
        }

        public void Dispose()
        {
            downloader.DownloadBegan -= onDownloadBegan;
        }
    }
}
