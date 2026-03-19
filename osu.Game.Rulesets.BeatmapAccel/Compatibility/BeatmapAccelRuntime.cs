using System;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal static class BeatmapAccelRuntime
{
    private static readonly IBeatmapAccelRuntimeStrategy strategy = createStrategy();

    public static IBeatmapAccelRuntimeStrategy Current => strategy;

    private static IBeatmapAccelRuntimeStrategy createStrategy()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            return new PortableBeatmapAccelRuntimeStrategy();

        return new DefaultBeatmapAccelRuntimeStrategy();
    }
}
