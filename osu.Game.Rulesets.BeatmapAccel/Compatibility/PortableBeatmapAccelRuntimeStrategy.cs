using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal sealed class PortableBeatmapAccelRuntimeStrategy : BaseBeatmapAccelRuntimeStrategy
{
    private static int randomSeed = Environment.TickCount;

    private readonly ThreadLocal<Random> random = new(() => new Random(Interlocked.Increment(ref randomSeed)));

    public override string Name => "portable";

    public override long NextInt64(long minInclusive, long maxExclusive)
    {
        if (minInclusive >= maxExclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

        ulong range = unchecked((ulong)(maxExclusive - minInclusive));
        ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
        ulong value;

        do
        {
            value = nextUInt64();
        }
        while (value >= limit);

        return unchecked((long)((value % range) + (ulong)minInclusive));
    }

    public override void NextBytes(byte[] buffer)
        => random.Value!.NextBytes(buffer);

    public override async Task ConnectSocketAsync(Socket socket, IPAddress address, int port, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration _ = cancellationToken.Register(() =>
        {
            try
            {
                socket.Dispose();
            }
            catch
            {
            }
        });

        await Task.Factory.FromAsync(
            (callback, state) => socket.BeginConnect(address, port, callback, state),
            socket.EndConnect,
            null).ConfigureAwait(false);
    }

    private ulong nextUInt64()
    {
        byte[] bytes = new byte[sizeof(ulong)];
        NextBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }
}
