using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal interface IBeatmapAccelPlatformRuntime
{
    string Name { get; }

    bool PreferConservativeNetworking { get; }

    /// <summary>
    /// Whether the OS currently has a system proxy configured (e.g. a VPN client in proxy mode).
    /// When true, downloads should go through the proxy instead of connecting directly to the preferred IP.
    /// </summary>
    bool HasSystemProxy { get; }

    long NextInt64(long minInclusive, long maxExclusive);

    void NextBytes(byte[] buffer);

    Task ForEachAsync<T>(IEnumerable<T> source, int maxDegreeOfParallelism, CancellationToken cancellationToken, Func<T, CancellationToken, Task> body);

    Task ConnectSocketAsync(Socket socket, IPAddress address, int port, CancellationToken cancellationToken);

    Task<PreferredIpHttpProbeResponse?> ProbePreferredIpHttpAsync(PreferredIpHttpProbeRequest request, CancellationToken cancellationToken);

    Task DownloadFileAsync(PreferredIpFileDownloadRequest request, CancellationToken cancellationToken);

    void MoveFileOverwrite(string sourcePath, string destinationPath);

    string FormatIpv4(uint value);

    string FormatIpv6(BigInteger value);
}

internal sealed record PreferredIpHttpHandlerOptions(
    IPAddress? PreferredIp,
    bool AllowAutoRedirect,
    DecompressionMethods AutomaticDecompression,
    TimeSpan ConnectTimeout,
    TimeSpan? PooledConnectionLifetime = null,
    TimeSpan? PooledConnectionIdleTimeout = null);
