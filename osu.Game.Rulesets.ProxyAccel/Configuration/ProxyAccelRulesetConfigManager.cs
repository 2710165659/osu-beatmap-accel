using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.ProxyAccel.Configuration;

public class ProxyAccelRulesetConfigManager : RulesetConfigManager<ProxyAccelSetting>
{
    public const string DefaultWorkerBaseUrl = "";

    public static ProxyAccelRulesetConfigManager? Instance { get; private set; }

    public ProxyAccelRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset)
        : base(settings, ruleset)
    {
        Instance = this;
    }

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();
        SetDefault(ProxyAccelSetting.WorkerBaseUrl, DefaultWorkerBaseUrl);
    }

    public string GetWorkerBaseUrl()
        => Get<string>(ProxyAccelSetting.WorkerBaseUrl).Trim().TrimEnd('/');
}

public enum ProxyAccelSetting
{
    WorkerBaseUrl,
}
