using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility.Android;

internal sealed class AndroidBeatmapAccelPlatformRuntime : BeatmapAccelPlatformRuntimeBase
{
    private static int randomSeed = Environment.TickCount;

    private readonly ThreadLocal<Random> random = new(() => new Random(Interlocked.Increment(ref randomSeed)));

    public override string Name => "android";

    public override bool PreferConservativeNetworking => true;

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

    public override Task ConnectSocketAsync(Socket socket, IPAddress address, int port, CancellationToken cancellationToken)
        => connectSocketInternalAsync(socket, address, port, cancellationToken);

    public override Task<PreferredIpHttpProbeResponse?> ProbePreferredIpHttpAsync(PreferredIpHttpProbeRequest request, CancellationToken cancellationToken)
        => AndroidPreferredIpHttpsTunnel.ProbeAsync(request, cancellationToken);

    public override Task DownloadFileAsync(PreferredIpFileDownloadRequest request, CancellationToken cancellationToken)
        => AndroidPreferredIpHttpsTunnel.DownloadFileAsync(request, cancellationToken);

    private static async Task connectSocketInternalAsync(Socket socket, IPAddress address, int port, CancellationToken cancellationToken)
    {
        Task connectTask = Task.Run(() =>
        {
            try
            {
                socket.Connect(new IPEndPoint(address, port));
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }, CancellationToken.None);

        _ = connectTask.ContinueWith(task =>
        {
            if (task.Exception == null)
                return;

            foreach (Exception inner in task.Exception.Flatten().InnerExceptions)
            {
                if (inner is OperationCanceledException)
                    continue;

                if (inner is SocketException socketException && socketException.SocketErrorCode == SocketError.NotSocket)
                    continue;

                BeatmapAccelLogging.Log($"Android socket connect background failure: {inner.GetType().Name}: {inner.Message}");
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        if (!cancellationToken.CanBeCanceled)
        {
            await connectTask.ConfigureAwait(false);
            return;
        }

        var cancellationTaskSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                socket.Dispose();
            }
            catch
            {
            }

            cancellationTaskSource.TrySetCanceled(cancellationToken);
        });

        Task completedTask = await Task.WhenAny(connectTask, cancellationTaskSource.Task).ConfigureAwait(false);
        await completedTask.ConfigureAwait(false);
    }

    private ulong nextUInt64()
    {
        byte[] bytes = new byte[sizeof(ulong)];
        NextBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }
}
