using System;
using osu.Framework.Threading;

namespace osu.Game.Rulesets.ProxyAccel.Features.Injection;

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
                game.Add(new Download.PreviewTrackHandler());
                ProxyLogging.Log("Injected proxy preview handler.");
            }
            catch (Exception e)
            {
                currentSessionHash = int.MinValue;
                ProxyLogging.LogError(e, "Failed to inject proxy preview handler");
            }
        }, 1);

        return true;
    }
}
