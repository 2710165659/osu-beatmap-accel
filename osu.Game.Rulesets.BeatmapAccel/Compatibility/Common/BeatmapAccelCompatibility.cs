using System;
using osu.Game.Rulesets.BeatmapAccel.Compatibility.Android;
using osu.Game.Rulesets.BeatmapAccel.Compatibility.Windows;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal static class BeatmapAccelCompatibility
{
    private static readonly IBeatmapAccelPlatformRuntime platformRuntime = createPlatformRuntime();

    public static IBeatmapAccelPlatformRuntime Current => platformRuntime;

    private static IBeatmapAccelPlatformRuntime createPlatformRuntime()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            return new AndroidBeatmapAccelPlatformRuntime();

        return new WindowsBeatmapAccelPlatformRuntime();
    }
}
