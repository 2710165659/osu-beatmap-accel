using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal interface IBeatmapAccelRuntimeStrategy
{
    string Name { get; }

    long NextInt64(long minInclusive, long maxExclusive);

    void NextBytes(byte[] buffer);

    Task ForEachAsync<T>(IEnumerable<T> source, int maxDegreeOfParallelism, CancellationToken cancellationToken, Func<T, CancellationToken, Task> body);

    SocketsHttpHandler CreatePreferredIpHttpHandler(PreferredIpHttpHandlerOptions options);

    Task<Stream> ReadHttpStreamAsync(HttpContent content, CancellationToken cancellationToken);

    Task<string> ReadHttpStringAsync(HttpContent content, CancellationToken cancellationToken);

    Task<int> ReadStreamAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken);

    Task WriteStreamAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken);

    Task ConnectSocketAsync(Socket socket, IPAddress address, int port, CancellationToken cancellationToken);

    HttpClient CreateHttpClient(HttpMessageHandler handler);

    void SetRequestHeader(HttpRequestMessage request, string name, string value);

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
