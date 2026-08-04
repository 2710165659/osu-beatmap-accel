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
using osu.Game.Rulesets.BeatmapAccel.Compatibility;
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
    private const string ranked_play_availability_tracker_type_name = "osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay.RankedPlayBeatmapAvailabilityTracker";

    private static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly PropertyInfo? internalChildrenProperty = typeof(CompositeDrawable).GetProperty("InternalChildren", flags);
    private static readonly EventInfo? childBecameAliveEvent = typeof(CompositeDrawable).GetEvent("ChildBecameAlive", flags);
    private static readonly EventInfo? childDiedEvent = typeof(CompositeDrawable).GetEvent("ChildDied", flags);
    private static readonly FieldInfo? noVideoField = typeof(DownloadBeatmapSetRequest).GetField("noVideo", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Dictionary<(Type Type, string Name), MemberInfo?> memberCache = new();
    private static readonly Dictionary<(Type Type, string Name), FieldInfo?> fieldCache = new();
    private static readonly Dictionary<Type, bool> rankedPlayTypeCache = new();

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
    private bool interceptionActive;
    private MemberInfo? originalDownloaderNotificationMember;
    private Action<Notification>? originalDownloaderNotificationTarget;

    private readonly Dictionary<object, ActionPatch> actionPatches = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CompositeDrawable, CompositeObserver> compositeObservers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, MemberPatch> downloaderMemberPatches = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BeatmapDownloadTracker, TrackerBridge> trackerBridges = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, IDisposable> automaticDownloadBridges = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> automaticLookupAttempts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> automaticSetDownloadAttempts = new(ReferenceEqualityComparer.Instance);

    [BackgroundDependencyLoader]
    private void load()
    {
        if (beatmapManager == null || apiProvider == null)
        {
            BeatmapAccelLogging.Log("BeatmapAccel global interceptor is missing required download dependencies.");
            return;
        }

        BeatmapAccelDownloadRuntime.EnsureInitialized(beatmapManager, apiProvider, notifications == null ? null : new Action<Notification>(notifications.Post));
        CloudflareSpeedTestManager.NotificationPoster = notifications == null ? null : new Action<Notification>(notifications.Post);

        if (originalDownloader != null)
        {
            originalDownloader.DownloadBegan += onOriginalDownloadBegan;
            hookOriginalDownloaderNotifications();
        }

        CloudflareSpeedTestManager.ScheduleToMainThread ??= action => Schedule(action);
        CloudflareSpeedTestManager.BeginStartupSpeedTest();

        if (BeatmapAccelRulesetConfigManager.Instance == null)
            return;

        interceptAllDownloads = BeatmapAccelRulesetConfigManager.Instance.GetBindable<bool>(BeatmapAccelSetting.InterceptAllBeatmapDownloads);
        interceptAllDownloads.BindValueChanged(onInterceptionChanged);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (interceptAllDownloads != null)
            onInterceptionChanged(new ValueChangedEvent<bool>(false, interceptAllDownloads.Value));
    }

    private void onInterceptionChanged(ValueChangedEvent<bool> change)
    {
        if (change.NewValue)
            beginInterception();
        else
            endInterception();
    }

    /// <summary>
    /// Whether interception should actually run right now.
    /// The user's saved setting is respected, but a system proxy disables interception entirely
    /// (downloads must go through the proxy instead of the preferred-IP direct path), without
    /// touching the saved setting value.
    /// </summary>
    private bool shouldIntercept
        => interceptAllDownloads?.Value == true && !BeatmapAccelCompatibility.Current.HasSystemProxy;

    private void hookOriginalDownloaderNotifications()
    {
        if (originalDownloader == null)
            return;

        originalDownloaderNotificationMember ??= getMember(originalDownloader.GetType(), "PostNotification");

        if (originalDownloaderNotificationMember == null)
            return;

        originalDownloaderNotificationTarget = getMemberValue(originalDownloaderNotificationMember, originalDownloader) as Action<Notification>;

        setMemberValue(originalDownloaderNotificationMember, originalDownloader, (Action<Notification>)filterOriginalDownloaderNotification);
    }

    private void restoreOriginalDownloaderNotifications()
    {
        if (originalDownloader == null || originalDownloaderNotificationMember == null)
            return;

        setMemberValue(originalDownloaderNotificationMember, originalDownloader, originalDownloaderNotificationTarget!);
    }

    private void filterOriginalDownloaderNotification(Notification notification)
    {
        if (shouldIntercept)
            return;

        originalDownloaderNotificationTarget?.Invoke(notification);
    }

    private void beginInterception()
    {
        if (interceptionActive || BeatmapAccelDownloadRuntime.Downloader == null || Parent is not Drawable root)
            return;

        // 系统代理（如 VPN 代理模式）下接管下载会绕过代理直连优选 IP 而失败，
        // 因此不启动拦截，让下载继续走原版链路（即系统代理）。用户保存的开关状态保持不变。
        if (!shouldIntercept)
        {
            BeatmapAccelLogging.Log("System proxy detected; global download interception is disabled so downloads keep using the system proxy.");
            return;
        }

        interceptionActive = true;
        attachSubtree(root);
    }

    private void endInterception()
    {
        interceptionActive = false;

        foreach (CompositeObserver observer in compositeObservers.Values)
            observer.Dispose();

        compositeObservers.Clear();
        restorePatchedState();
    }

    private void attachSubtree(Drawable root)
    {
        var stack = new Stack<Drawable>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            Drawable current = stack.Pop();
            inspectDrawable(current);

            if (current is not CompositeDrawable composite)
                continue;

            ensureCompositeObserver(composite);
            pushChildren(composite, stack);
        }
    }

    private void detachSubtree(Drawable root)
    {
        var stack = new Stack<Drawable>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            Drawable current = stack.Pop();

            if (current is CompositeDrawable composite)
            {
                removeCompositeObserver(composite);
                pushChildren(composite, stack);
            }

            cleanupDrawable(current);
        }
    }

    private void onChildBecameAlive(Drawable child)
    {
        if (!interceptionActive)
            return;

        attachSubtree(child);
    }

    private void onChildDied(Drawable child)
        => detachSubtree(child);

    private void ensureCompositeObserver(CompositeDrawable composite)
    {
        if (compositeObservers.ContainsKey(composite))
            return;

        compositeObservers[composite] = new CompositeObserver(composite, onChildBecameAlive, onChildDied);
    }

    private void removeCompositeObserver(CompositeDrawable composite)
    {
        if (!compositeObservers.Remove(composite, out CompositeObserver? observer))
            return;

        observer.Dispose();
    }

    private static void pushChildren(CompositeDrawable composite, Stack<Drawable> stack)
    {
        if (internalChildrenProperty?.GetValue(composite) is not IEnumerable children)
            return;

        foreach (object? child in children)
        {
            if (child is Drawable drawable)
                stack.Push(drawable);
        }
    }

    private void inspectDrawable(Drawable drawable)
    {
        switch (drawable)
        {
            case HeaderDownloadButton headerDownloadButton:
                patchHeaderDownloadButton(headerDownloadButton);
                break;

            case BeatmapDownloadButton beatmapDownloadButton:
                patchBeatmapDownloadButton(beatmapDownloadButton);
                break;

            case DownloadButton downloadButton:
                patchCardDownloadButton(downloadButton);
                break;

            case BeatmapCard beatmapCard:
                patchBeatmapCard(beatmapCard);
                break;

            case BeatmapDownloadTracker tracker when tracker is not BeatmapAccelBeatmapDownloadTracker:
                ensureTrackerBridge(tracker);
                break;

            case SoloSpectatorScreen soloSpectatorScreen:
                disableDownloaderMember(soloSpectatorScreen, "beatmapDownloader");
                if (!ensureSoloSpectatorAutomaticDownloadBridge(soloSpectatorScreen))
                    handleSoloSpectatorAutomaticDownload(soloSpectatorScreen);
                break;

            case DailyChallengeIntro dailyChallengeIntro:
                handleDailyChallengeAutomaticDownload(dailyChallengeIntro);
                break;

            case ScreenMatchmaking matchmakingScreen:
                disableDownloaderMember(matchmakingScreen, "beatmapDownloader");
                if (!ensureMatchmakingAutomaticDownloadBridge(matchmakingScreen))
                    handleMatchmakingAutomaticDownload(matchmakingScreen);
                break;

            case MultiplayerSpectateButton multiplayerSpectateButton:
                disableDownloaderMember(multiplayerSpectateButton, "beatmapDownloader");
                if (!ensureMultiplayerSpectateAutomaticDownloadBridge(multiplayerSpectateButton))
                    handleMultiplayerSpectateAutomaticDownload(multiplayerSpectateButton);
                break;

            case MissingBeatmapNotification missingBeatmapNotification:
                disableDownloaderMember(missingBeatmapNotification, "beatmapDownloader");
                handleMissingBeatmapNotification(missingBeatmapNotification);
                break;

            case PanelUpdateBeatmapButton panelUpdateBeatmapButton:
                patchPanelUpdateBeatmapButton(panelUpdateBeatmapButton);
                break;
        }

        if (isTypeOrSubclass(drawable, ranked_play_availability_tracker_type_name))
        {
            disableDownloaderMember(drawable, "beatmapDownloader");
            if (!ensureRankedPlayAutomaticDownloadBridge(drawable))
                handleRankedPlayAutomaticDownload(drawable);
        }
    }

    private void cleanupDrawable(Drawable drawable)
    {
        switch (drawable)
        {
            case HeaderDownloadButton headerDownloadButton:
                restoreActionPatch(getFieldValue<HeaderButton>(headerDownloadButton, "button"));
                break;

            case BeatmapDownloadButton beatmapDownloadButton:
                restoreActionPatch(getFieldValue<UiDownloadButton>(beatmapDownloadButton, "button"));
                break;

            case DownloadButton downloadButton:
                restoreActionPatch(downloadButton);
                break;

            case BeatmapCard beatmapCard:
                restoreActionPatch(beatmapCard);
                break;

            case BeatmapDownloadTracker tracker when tracker is not BeatmapAccelBeatmapDownloadTracker:
                if (trackerBridges.Remove(tracker, out TrackerBridge? bridge))
                    bridge.Dispose();

                break;

            case SoloSpectatorScreen soloSpectatorScreen:
                restoreMemberPatch(soloSpectatorScreen);
                removeAutomaticDownloadState(soloSpectatorScreen);
                break;

            case DailyChallengeIntro dailyChallengeIntro:
                removeAutomaticDownloadState(dailyChallengeIntro);
                break;

            case ScreenMatchmaking matchmakingScreen:
                restoreMemberPatch(matchmakingScreen);
                removeAutomaticDownloadState(matchmakingScreen);
                break;

            case MultiplayerSpectateButton multiplayerSpectateButton:
                restoreMemberPatch(multiplayerSpectateButton);
                removeAutomaticDownloadState(multiplayerSpectateButton);
                break;

            case MissingBeatmapNotification missingBeatmapNotification:
                restoreMemberPatch(missingBeatmapNotification);
                removeAutomaticDownloadState(missingBeatmapNotification);
                break;

            case PanelUpdateBeatmapButton panelUpdateBeatmapButton:
                restoreActionPatch(panelUpdateBeatmapButton);
                break;
        }

        if (isTypeOrSubclass(drawable, ranked_play_availability_tracker_type_name))
        {
            restoreMemberPatch(drawable);
            removeAutomaticDownloadState(drawable);
        }
    }

    private void patchHeaderDownloadButton(HeaderDownloadButton headerDownloadButton)
    {
        HeaderButton? button = getFieldValue<HeaderButton>(headerDownloadButton, "button");
        BeatmapDownloadTracker? tracker = getFieldValue<BeatmapDownloadTracker>(headerDownloadButton, "downloadTracker");
        APIBeatmapSet? beatmapSet = getFieldValue<APIBeatmapSet>(headerDownloadButton, "beatmapSet");
        bool? noVideo = getFieldValue<bool>(headerDownloadButton, "noVideo");

        if (button == null || tracker == null || beatmapSet == null || noVideo == null)
            return;

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

    private void patchBeatmapDownloadButton(BeatmapDownloadButton beatmapDownloadButton)
    {
        UiDownloadButton? button = getFieldValue<UiDownloadButton>(beatmapDownloadButton, "button");
        BeatmapDownloadTracker? tracker = getFieldValue<BeatmapDownloadTracker>(beatmapDownloadButton, "DownloadTracker");
        IBeatmapSetInfo? beatmapSet = getFieldValue<IBeatmapSetInfo>(beatmapDownloadButton, "beatmapSet");
        Bindable<bool>? noVideoSetting = getFieldValue<Bindable<bool>>(beatmapDownloadButton, "noVideoSetting");

        if (button == null || tracker == null || beatmapSet == null || noVideoSetting == null)
            return;

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

    private void patchCardDownloadButton(DownloadButton downloadButton)
    {
        APIBeatmapSet? beatmapSet = getFieldValue<APIBeatmapSet>(downloadButton, "beatmapSet");
        Bindable<bool>? preferNoVideo = getFieldValue<Bindable<bool>>(downloadButton, "preferNoVideo");

        if (beatmapSet == null || preferNoVideo == null)
            return;

        if (!actionPatches.ContainsKey(downloadButton))
            actionPatches[downloadButton] = new ActionPatch(downloadButton, downloadButton.Action);

        downloadButton.Action = () =>
        {
            if (downloadButton.State.Value != DownloadState.NotDownloaded || beatmapSet.Availability.DownloadDisabled)
                return;

            BeatmapAccelDownloadRuntime.Downloader?.Download(beatmapSet, preferNoVideo.Value);
        };
    }

    private void patchBeatmapCard(BeatmapCard beatmapCard)
    {
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

    private void patchPanelUpdateBeatmapButton(PanelUpdateBeatmapButton panelUpdateBeatmapButton)
    {
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
        APIBeatmap? beatmap = getFieldValue<APIBeatmap>(screen, "beatmap");

        if (automaticDownload is not IHasCurrentValue<bool> toggle || beatmap?.BeatmapSet == null || !toggle.Current.Value)
            return;

        APIBeatmapSet beatmapSet = beatmap.BeatmapSet;
        requestAutomaticDownload(screen, beatmapSet.OnlineID, beatmapSet, false);
    }

    private bool ensureSoloSpectatorAutomaticDownloadBridge(SoloSpectatorScreen screen)
    {
        if (automaticDownloadBridges.ContainsKey(screen))
            return true;

        SettingsCheckbox? automaticDownload = getFieldValue<SettingsCheckbox>(screen, "automaticDownload");
        Container? beatmapPanelContainer = getFieldValue<Container>(screen, "beatmapPanelContainer");

        if (automaticDownload == null || beatmapPanelContainer == null)
            return false;

        automaticDownloadBridges[screen] = new SoloSpectatorAutomaticDownloadBridge(
            beatmapPanelContainer,
            automaticDownload.Current,
            () => Scheduler.AddOnce(() => handleSoloSpectatorAutomaticDownload(screen)));

        return true;
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

    private bool ensureMatchmakingAutomaticDownloadBridge(ScreenMatchmaking screen)
    {
        if (automaticDownloadBridges.ContainsKey(screen))
            return true;

        MultiplayerClient? client = getMemberValue<MultiplayerClient>(screen, "client");

        if (client == null)
            return false;

        automaticDownloadBridges[screen] = new MatchmakingAutomaticDownloadBridge(
            client,
            () => Scheduler.AddOnce(() => handleMatchmakingAutomaticDownload(screen)));

        return true;
    }

    private void handleRankedPlayAutomaticDownload(object tracker)
    {
        MultiplayerClient? client = getMemberValue<MultiplayerClient>(tracker, "client");

        if (client?.Room?.CurrentPlaylistItem == null)
            return;

        queueLookupDownload(tracker, client.Room.CurrentPlaylistItem.BeatmapID, osuConfig?.Get<bool>(OsuSetting.PreferNoVideo) == true);
    }

    private bool ensureRankedPlayAutomaticDownloadBridge(object tracker)
    {
        if (automaticDownloadBridges.ContainsKey(tracker))
            return true;

        MultiplayerClient? client = getMemberValue<MultiplayerClient>(tracker, "client");

        if (client == null)
            return false;

        automaticDownloadBridges[tracker] = new MatchmakingAutomaticDownloadBridge(
            client,
            () => Scheduler.AddOnce(() => handleRankedPlayAutomaticDownload(tracker)));

        return true;
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

    private bool ensureMultiplayerSpectateAutomaticDownloadBridge(MultiplayerSpectateButton button)
    {
        if (automaticDownloadBridges.ContainsKey(button))
            return true;

        MultiplayerClient? client = getMemberValue<MultiplayerClient>(button, "client");
        Bindable<bool>? automaticallyDownload = getFieldValue<Bindable<bool>>(button, "automaticallyDownload");

        if (client == null || automaticallyDownload == null)
            return false;

        automaticDownloadBridges[button] = new MultiplayerSpectateAutomaticDownloadBridge(
            client,
            automaticallyDownload,
            () => Scheduler.AddOnce(() => handleMultiplayerSpectateAutomaticDownload(button)));

        return true;
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
        if (!shouldIntercept || BeatmapAccelDownloadRuntime.Downloader == null)
            return;

        bool noVideo = noVideoField?.GetValue(request) as bool? == true;

        bool rerouted = request.Model is BeatmapSetInfo originalModel && originalModel.IsManaged
            ? BeatmapAccelDownloadRuntime.Downloader.DownloadAsUpdate(originalModel, noVideo)
            : BeatmapAccelDownloadRuntime.Downloader.Download(request.Model, noVideo);

        if (!rerouted && BeatmapAccelDownloadRuntime.Downloader.GetExistingDownload(request.Model) == null)
            return;

        request.Cancel();

        // Reattach any bridged trackers to the BeatmapAccel download on the next scheduler frame.
        // No fixed delay needed: TrackerBridge.onDownloadBegan (subscribed in its constructor) already
        // schedules the primary attachment synchronously when BeatmapAccel's DownloadBegan fires inside
        // Download() above. This Add acts as an immediate safety net to cover the unlikely case where
        // the event-based path misses the tracker — ensuring the tracker always gets re-bound to the
        // BeatmapAccel request before the next draw frame.
        Scheduler.Add(() => reattachTrackedDownload(request.Model.OnlineID));
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

        if (automaticLookupAttempts.TryGetValue(owner, out int previousAttempt) && previousAttempt == beatmapId)
            return;

        if (beatmapManager.IsAvailableLocally(new APIBeatmap { OnlineID = beatmapId }))
        {
            automaticLookupAttempts[owner] = beatmapId;
            return;
        }

        automaticLookupAttempts[owner] = beatmapId;

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

        if (automaticSetDownloadAttempts.TryGetValue(owner, out int previousAttempt) && previousAttempt == beatmapSetId)
            return;

        if (beatmapManager.IsAvailableLocally(new APIBeatmap { OnlineID = beatmapSetId }))
        {
            automaticSetDownloadAttempts[owner] = beatmapSetId;
            return;
        }

        if (BeatmapAccelDownloadRuntime.Downloader.GetExistingDownload(new BeatmapSetInfo { OnlineID = beatmapSetId }) != null)
        {
            automaticSetDownloadAttempts[owner] = beatmapSetId;
            return;
        }

        if (BeatmapAccelDownloadRuntime.Downloader.Download(beatmapSet, preferNoVideo))
            automaticSetDownloadAttempts[owner] = beatmapSetId;
    }

    private void removeAutomaticDownloadState(object owner)
    {
        if (automaticDownloadBridges.Remove(owner, out IDisposable? bridge))
            bridge.Dispose();

        automaticLookupAttempts.Remove(owner);
        automaticSetDownloadAttempts.Remove(owner);
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

        foreach (IDisposable bridge in automaticDownloadBridges.Values)
            bridge.Dispose();

        automaticDownloadBridges.Clear();
        automaticLookupAttempts.Clear();
        automaticSetDownloadAttempts.Clear();
    }

    private void restoreActionPatch(object? key)
    {
        if (key == null || !actionPatches.Remove(key, out ActionPatch? patch))
            return;

        patch.Restore();
    }

    private void restoreMemberPatch(object owner)
    {
        if (!downloaderMemberPatches.Remove(owner, out MemberPatch? patch))
            return;

        restoreMemberPatch(patch);
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

        restoreOriginalDownloaderNotifications();
        BeatmapAccelDownloadRuntime.Shutdown();
        foreach (CompositeObserver observer in compositeObservers.Values)
            observer.Dispose();

        compositeObservers.Clear();
        restorePatchedState();
    }

    private static MemberInfo? getMember(Type type, string memberName)
    {
        var cacheKey = (type, memberName);

        if (memberCache.TryGetValue(cacheKey, out MemberInfo? cachedMember))
            return cachedMember;

        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(memberName, flags);

            if (field != null)
                return memberCache[cacheKey] = field;

            PropertyInfo? property = current.GetProperty(memberName, flags);

            if (property != null)
                return memberCache[cacheKey] = property;
        }

        memberCache[cacheKey] = null;
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
        var cacheKey = (owner.GetType(), fieldName);

        if (!fieldCache.TryGetValue(cacheKey, out FieldInfo? field))
        {
            for (Type? current = owner.GetType(); current != null; current = current.BaseType)
            {
                field = current.GetField(fieldName, flags);

                if (field != null)
                    break;
            }

            fieldCache[cacheKey] = field;
        }

        return field == null ? default : (T?)field.GetValue(owner);
    }

    private static T? getMemberValue<T>(object owner, string memberName)
    {
        MemberInfo? member = getMember(owner.GetType(), memberName);
        return member == null ? default : (T?)getMemberValue(member, owner);
    }

    private static bool isTypeOrSubclass(object owner, string fullTypeName)
    {
        Type ownerType = owner.GetType();

        if (rankedPlayTypeCache.TryGetValue(ownerType, out bool cachedResult))
            return cachedResult;

        for (Type? current = ownerType; current != null; current = current.BaseType)
        {
            if (current.FullName == fullTypeName)
            {
                rankedPlayTypeCache[ownerType] = true;
                return true;
            }
        }

        rankedPlayTypeCache[ownerType] = false;
        return false;
    }

    private sealed record MemberPatch(object Owner, MemberInfo Member, object OriginalValue);

    private sealed class CompositeObserver : IDisposable
    {
        private readonly CompositeDrawable composite;
        private readonly Delegate? childBecameAliveHandler;
        private readonly Delegate? childDiedHandler;

        public CompositeObserver(CompositeDrawable composite, Action<Drawable> childBecameAlive, Action<Drawable> childDied)
        {
            this.composite = composite;
            childBecameAliveHandler = createHandler(childBecameAliveEvent, childBecameAlive);
            childDiedHandler = createHandler(childDiedEvent, childDied);

            addHandler(childBecameAliveEvent, childBecameAliveHandler);
            addHandler(childDiedEvent, childDiedHandler);
        }

        public void Dispose()
        {
            removeHandler(childBecameAliveEvent, childBecameAliveHandler);
            removeHandler(childDiedEvent, childDiedHandler);
        }

        private Delegate? createHandler(EventInfo? eventInfo, Action<Drawable> callback)
        {
            if (eventInfo?.EventHandlerType == null)
                return null;

            return Delegate.CreateDelegate(eventInfo.EventHandlerType, callback.Target, callback.Method, false);
        }

        private void addHandler(EventInfo? eventInfo, Delegate? handler)
        {
            if (eventInfo?.GetAddMethod(true) == null || handler == null)
                return;

            eventInfo.GetAddMethod(true)!.Invoke(composite, new object?[] { handler });
        }

        private void removeHandler(EventInfo? eventInfo, Delegate? handler)
        {
            if (eventInfo?.GetRemoveMethod(true) == null || handler == null)
                return;

            eventInfo.GetRemoveMethod(true)!.Invoke(composite, new object?[] { handler });
        }
    }

    private sealed class SoloSpectatorAutomaticDownloadBridge : IDisposable
    {
        private readonly Bindable<bool> automaticDownload;
        private readonly CompositeObserver beatmapPanelObserver;
        private readonly Action trigger;

        public SoloSpectatorAutomaticDownloadBridge(CompositeDrawable beatmapPanelContainer, Bindable<bool> automaticDownload, Action trigger)
        {
            this.automaticDownload = automaticDownload;
            this.trigger = trigger;

            beatmapPanelObserver = new CompositeObserver(beatmapPanelContainer, _ => trigger(), _ => { });
            automaticDownload.ValueChanged += onAutomaticDownloadChanged;

            trigger();
        }

        private void onAutomaticDownloadChanged(ValueChangedEvent<bool> _) => trigger();

        public void Dispose()
        {
            automaticDownload.ValueChanged -= onAutomaticDownloadChanged;
            beatmapPanelObserver.Dispose();
        }
    }

    private sealed class MatchmakingAutomaticDownloadBridge : IDisposable
    {
        private readonly MultiplayerClient client;
        private readonly Action trigger;

        public MatchmakingAutomaticDownloadBridge(MultiplayerClient client, Action trigger)
        {
            this.client = client;
            this.trigger = trigger;

            client.SettingsChanged += onSettingsChanged;
            trigger();
        }

        private void onSettingsChanged(MultiplayerRoomSettings _) => trigger();

        public void Dispose()
        {
            client.SettingsChanged -= onSettingsChanged;
        }
    }

    private sealed class MultiplayerSpectateAutomaticDownloadBridge : IDisposable
    {
        private readonly MultiplayerClient client;
        private readonly Bindable<bool> automaticallyDownload;
        private readonly Action trigger;

        public MultiplayerSpectateAutomaticDownloadBridge(MultiplayerClient client, Bindable<bool> automaticallyDownload, Action trigger)
        {
            this.client = client;
            this.automaticallyDownload = automaticallyDownload;
            this.trigger = trigger;

            client.RoomUpdated += onRoomUpdated;
            automaticallyDownload.ValueChanged += onAutomaticallyDownloadChanged;

            trigger();
        }

        private void onRoomUpdated() => trigger();

        private void onAutomaticallyDownloadChanged(ValueChangedEvent<bool> _) => trigger();

        public void Dispose()
        {
            client.RoomUpdated -= onRoomUpdated;
            automaticallyDownload.ValueChanged -= onAutomaticallyDownloadChanged;
        }
    }

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

            // Skip attaching if the download has already completed (Progress == 1).
            // When Progress >= 1, BeatmapDownloadTracker.attachDownload() sets state to Importing
            // but does NOT subscribe to Success/Failure events (see osu.Game BeatmapDownloadTracker).
            // If we attach here, the tracker may get permanently stuck at Importing because:
            //   - No events are subscribed (Progress >= 1 branch skips subscription)
            //   - The realm subscription may have already fired earlier
            // The tracker's own realm subscription will correctly detect import completion.
            if (request != null && request.Progress < 1)
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
