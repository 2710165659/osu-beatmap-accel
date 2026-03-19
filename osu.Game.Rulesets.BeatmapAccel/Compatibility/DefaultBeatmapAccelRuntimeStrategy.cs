using System;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal sealed class DefaultBeatmapAccelRuntimeStrategy : BaseBeatmapAccelRuntimeStrategy
{
    public override string Name => "default";

    public override long NextInt64(long minInclusive, long maxExclusive)
        => Random.Shared.NextInt64(minInclusive, maxExclusive);

    public override void NextBytes(byte[] buffer)
        => Random.Shared.NextBytes(buffer);
}
