using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.BeatmapAccel.Configuration;

public class BeatmapAccelRulesetConfigManager : RulesetConfigManager<BeatmapAccelSetting>
{
    public static BeatmapAccelRulesetConfigManager? Instance { get; private set; }

    public BeatmapAccelRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset)
        : base(settings, ruleset)
    {
        Instance = this;
    }

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();
        SetDefault(BeatmapAccelSetting.AutoSwitchOnStartup, true);
        SetDefault(BeatmapAccelSetting.AutoSwitchOnDownloadFailure, true);
        SetDefault(BeatmapAccelSetting.EnableIpv6Candidates, false);
        SetDefault(BeatmapAccelSetting.PreferredIp, string.Empty);
        SetDefault(BeatmapAccelSetting.LastSpeedTestSummary, "尚未测速");
    }

    public bool GetAutoSwitchOnStartup()
        => Get<bool>(BeatmapAccelSetting.AutoSwitchOnStartup);

    public bool GetAutoSwitchOnDownloadFailure()
        => Get<bool>(BeatmapAccelSetting.AutoSwitchOnDownloadFailure);

    public bool GetEnableIpv6Candidates()
        => Get<bool>(BeatmapAccelSetting.EnableIpv6Candidates);

    public string GetPreferredIp()
        => Get<string>(BeatmapAccelSetting.PreferredIp).Trim();

    public string GetLastSpeedTestSummary()
        => Get<string>(BeatmapAccelSetting.LastSpeedTestSummary).Trim();

    public void SetPreferredIp(string value)
        => SetValue(BeatmapAccelSetting.PreferredIp, value.Trim());

    public void SetLastSpeedTestSummary(string value)
        => SetValue(BeatmapAccelSetting.LastSpeedTestSummary, value.Trim());
}

public enum BeatmapAccelSetting
{
    AutoSwitchOnStartup,
    AutoSwitchOnDownloadFailure,
    EnableIpv6Candidates,
    PreferredIp,
    LastSpeedTestSummary,
}
