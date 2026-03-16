using System;
using System.Net.Http;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.ProxyAccel.Configuration;

namespace osu.Game.Rulesets.ProxyAccel.Settings;

public partial class ProxyAccelSettingsSubsection : RulesetSettingsSubsection
{
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private SettingsTextBox workerBaseUrlTextBox = null!;
    private SettingsButton testButton = null!;
    private SpriteText testStatusText = null!;

    protected override LocalisableString Header => "ProxyAccel";

    public ProxyAccelSettingsSubsection(ProxyAccelRuleset ruleset)
        : base(ruleset)
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var config = (ProxyAccelRulesetConfigManager)Config;

        Children = new Drawable[]
        {
            workerBaseUrlTextBox = new SettingsTextBox
            {
                LabelText = "加速 URL",
                TooltipText = "填写 worker 基地址",
                Current = config.GetBindable<string>(ProxyAccelSetting.WorkerBaseUrl)
            },
            testButton = new SettingsButton
            {
                Text = "Test",
                TooltipText = "请求 /healthz 检查当前地址是否可用",
                Action = runHealthCheck
            },
            testStatusText = new SpriteText
            {
                Margin = new MarginPadding
                {
                    Left = SettingsPanel.CONTENT_MARGINS,
                    Right = SettingsPanel.CONTENT_MARGINS,
                    Top = 4,
                    Bottom = 8
                },
                Font = OsuFont.GetFont(size: 14),
                Text = "等待测试"
            }
        };
    }

    private void runHealthCheck()
    {
        string baseUrl = workerBaseUrlTextBox.Current.Value.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            testStatusText.Text = "测试失败：URL 不能为空";
            return;
        }

        testButton.Enabled.Value = false;
        testStatusText.Text = "测试中...";

        Task.Run(async () =>
        {
            string result;

            try
            {
                using var response = await httpClient.GetAsync($"{baseUrl}/healthz").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                result = response.IsSuccessStatusCode
                    ? $"测试成功：{(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"测试失败：{(int)response.StatusCode} {response.ReasonPhrase} {body}";
            }
            catch (Exception e)
            {
                result = $"测试失败：{e.Message}";
            }

            Schedule(() =>
            {
                testStatusText.Text = result;
                testButton.Enabled.Value = true;
            });
        });
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        httpClient.Dispose();
    }
}
