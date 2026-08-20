using System;
using osu.Framework.Bindables;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BeatmapAccel.Compatibility;
using osu.Game.Rulesets.BeatmapAccel.Configuration;
using osu.Game.Rulesets.BeatmapAccel.Features.Download;

namespace osu.Game.Rulesets.BeatmapAccel.Settings;

public partial class BeatmapAccelSettingsSubsection : RulesetSettingsSubsection
{
    private SettingsButtonV2 switchButton = null!;
    private SettingsNote proxyNote = null!;

    [Resolved(canBeNull: true)]
    private INotificationOverlay? notifications { get; set; }

    public BeatmapAccelSettingsSubsection(BeatmapAccelRuleset ruleset)
        : base(ruleset)
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var config = (BeatmapAccelRulesetConfigManager)Config;

        CloudflareSpeedTestManager.ScheduleToMainThread = action => Schedule(action);
        CloudflareSpeedTestManager.NotificationPoster = notifications == null ? null : new Action<Notification>(notifications.Post);

        Children = new Drawable[]
        {
            wrapWithSettingsPadding(new GridContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                ColumnDimensions = new[]
                {
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 180),
                },
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.AutoSize),
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new FormTextBox
                        {
                            Caption = "当前优选 IP",
                            HintText = "支持手动填写 IPv4 或 IPv6 地址。留空时将退回默认域名解析，测速完成后也会自动覆盖这里的值。",
                            PlaceholderText = "例如 172.67.65.31 或 2606:4700::6810:1234",
                            Current = config.GetBindable<string>(BeatmapAccelSetting.PreferredIp)
                        },
                        switchButton = new SettingsButtonV2
                        {
                            Text = "测速并切换",
                            Action = runSpeedTestAndSwitch,
                            Padding = default,
                            Margin = new MarginPadding { Left = 10 }
                        }
                    }
                }
            }),
            wrapWithSettingsPadding(new FormCheckBox
            {
                Caption = "谱面预览显示右上角弹窗",
                HintText = "在播放谱面预览音频后，于右上角显示 BeatmapAccel 下载弹窗。关闭后不会弹窗，但其他下载接管功能仍可单独启用。",
                Current = config.GetBindable<bool>(BeatmapAccelSetting.ShowPreviewDownloadOverlay)
            }),
            wrapWithSettingsPadding(new FormCheckBox
            {
                Caption = "接管所有谱面下载",
                HintText = "高风险兼容模式。BeatmapAccel 会尝试接管当前界面的谱面下载按钮、自动缺谱面下载与下载状态追踪，让它们尽量走优选 IP。osu! 更新后可能失效，或导致少数页面下载状态显示异常。",
                Current = config.GetBindable<bool>(BeatmapAccelSetting.InterceptAllBeatmapDownloads)
            }),
            wrapWithSettingsPadding(proxyNote = new SettingsNote
            {
                RelativeSizeAxes = Axes.X,
            }),
            wrapWithSettingsPadding(new FormCheckBox
            {
                Caption = "启动自动测速切换",
                HintText = "游戏启动后后台测试内置 Cloudflare IP 段，并将后续下载切换到当前优选 IP。",
                Current = config.GetBindable<bool>(BeatmapAccelSetting.AutoSwitchOnStartup)
            }),
            wrapWithSettingsPadding(new FormCheckBox
            {
                Caption = "下载失败后自动测速切换",
                HintText = "下载请求失败时，后台重新测速并切换到新的优选 IP。手动取消下载不会触发。",
                Current = config.GetBindable<bool>(BeatmapAccelSetting.AutoSwitchOnDownloadFailure)
            }),
            wrapWithSettingsPadding(new FormCheckBox
            {
                Caption = "启用 IPv6 候选测速",
                HintText = "将 Cloudflare IPv6 段也加入测速候选。你的网络需要具备可用 IPv6，否则建议关闭。",
                Current = config.GetBindable<bool>(BeatmapAccelSetting.EnableIpv6Candidates)
            })
        };

        // 检测到系统代理（如 VPN 代理模式）时，接管下载会绕过代理直连优选 IP 而失败，
        // 因此提示用户；不改动用户已保存的开关状态，关闭代理并重启后会自动恢复接管能力。
        if (BeatmapAccelCompatibility.Current.HasSystemProxy)
        {
            proxyNote.Current.Value = new SettingsNote.Data(
                "检测到代理，接管所有谱面下载将不会生效。关闭代理并重启 osu! 后自动恢复。",
                SettingsNote.Type.Informational);
        }
    }

    private static Drawable wrapWithSettingsPadding(Drawable drawable) => new Container
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Padding = SettingsPanel.CONTENT_PADDING,
        Child = drawable
    };

    private void runSpeedTestAndSwitch()
    {
        switchButton.Enabled.Value = false;

        Task.Run(async () =>
        {
            try
            {
                await CloudflareSpeedTestManager.SwitchToFastestIpAsync(SpeedTestTrigger.Manual, notifications).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                BeatmapAccelLogging.LogError(e, "Manual speed test failed");

                Schedule(() => notifications?.Post(new SimpleNotification
                {
                    Text = $"BeatmapAccel: 测速失败。\n{e.Message}"
                }));
            }
            finally
            {
                Schedule(() => switchButton.Enabled.Value = true);
            }
        });
    }
}
