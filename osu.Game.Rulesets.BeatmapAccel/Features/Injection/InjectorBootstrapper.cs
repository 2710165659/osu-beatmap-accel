using System;
using osu.Framework.Threading;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Injection;

public static class InjectorBootstrapper
{
    private static int currentSessionHash = int.MinValue;

    public static bool BeginInject(OsuGame game, Scheduler scheduler)
    {
        int sessionHash = game.Toolbar.GetHashCode();

        if (sessionHash == currentSessionHash)
            return true;

        currentSessionHash = sessionHash;

        scheduler.AddDelayed(() =>
        {
            try
            {
                game.Add(new Download.GlobalBeatmapDownloadInterceptor());
                BeatmapAccelLogging.Log("Injected global download interceptor.");

                game.Add(new Download.PreviewTrackHandler());
                BeatmapAccelLogging.Log("Injected proxy preview handler.");

                game.Add(new RulesetSettingsRedirector());
                BeatmapAccelLogging.Log("Injected ruleset settings redirector.");
            }
            catch (Exception e)
            {
                currentSessionHash = int.MinValue;
                BeatmapAccelLogging.LogError(e, "Failed to inject proxy preview handler");
            }
        }, 1);

        return true;
    }
}
