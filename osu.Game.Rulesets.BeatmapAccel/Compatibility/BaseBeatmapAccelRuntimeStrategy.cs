using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal abstract class BaseBeatmapAccelRuntimeStrategy : IBeatmapAccelRuntimeStrategy
{
    public abstract string Name { get; }

    public abstract long NextInt64(long minInclusive, long maxExclusive);

    public abstract void NextBytes(byte[] buffer);

    public async Task ForEachAsync<T>(IEnumerable<T> source, int maxDegreeOfParallelism, CancellationToken cancellationToken, Func<T, CancellationToken, Task> body)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        if (body == null)
            throw new ArgumentNullException(nameof(body));

        if (maxDegreeOfParallelism <= 1)
        {
            foreach (T item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await body(item, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        using var throttler = new SemaphoreSlim(maxDegreeOfParallelism);
        var tasks = new List<Task>();

        foreach (T item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            tasks.Add(runBodyAsync(item, throttler, cancellationToken, body));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public virtual SocketsHttpHandler CreatePreferredIpHttpHandler(PreferredIpHttpHandlerOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = options.AllowAutoRedirect,
            AutomaticDecompression = options.AutomaticDecompression,
            ConnectTimeout = options.ConnectTimeout,
        };

        if (options.PooledConnectionLifetime.HasValue)
            handler.PooledConnectionLifetime = options.PooledConnectionLifetime.Value;

        if (options.PooledConnectionIdleTimeout.HasValue)
            handler.PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout.Value;

        if (options.PreferredIp != null)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(options.PreferredIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };

                await ConnectSocketAsync(socket, options.PreferredIp, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            };
        }

        return handler;
    }

    public virtual Task<Stream> ReadHttpStreamAsync(HttpContent content, CancellationToken cancellationToken)
        => content.ReadAsStreamAsync();

    public virtual async Task<string> ReadHttpStringAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using Stream stream = await ReadHttpStreamAsync(content, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    public virtual Task<int> ReadStreamAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => stream.ReadAsync(buffer, offset, count, cancellationToken);

    public virtual Task WriteStreamAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => stream.WriteAsync(buffer, offset, count, cancellationToken);

    public virtual Task ConnectSocketAsync(Socket socket, IPAddress address, int port, CancellationToken cancellationToken)
        => socket.ConnectAsync(address, port, cancellationToken).AsTask();

    public virtual HttpClient CreateHttpClient(HttpMessageHandler handler)
        => new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    public virtual void SetRequestHeader(HttpRequestMessage request, string name, string value)
        => request.Headers.TryAddWithoutValidation(name, value);

    public virtual void MoveFileOverwrite(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        File.Move(sourcePath, destinationPath);
    }

    public virtual string FormatIpv4(uint value)
        => $"{(byte)(value >> 24)}.{(byte)(value >> 16)}.{(byte)(value >> 8)}.{(byte)value}";

    public virtual string FormatIpv6(BigInteger value)
    {
        byte[] littleEndian = value.ToByteArray();

        if (littleEndian.Length > 16)
        {
            if (littleEndian.Length == 17 && littleEndian[^1] == 0)
                Array.Resize(ref littleEndian, 16);
            else
                throw new ArgumentOutOfRangeException(nameof(value), "IPv6 value exceeds 128 bits.");
        }

        Array.Resize(ref littleEndian, 16);
        Array.Reverse(littleEndian);

        var groups = new ushort[8];

        for (int i = 0; i < groups.Length; i++)
            groups[i] = (ushort)((littleEndian[i * 2] << 8) | littleEndian[i * 2 + 1]);

        return string.Create(39, groups, static (span, values) =>
        {
            int index = 0;

            for (int i = 0; i < values.Length; i++)
            {
                values[i].TryFormat(span.Slice(index, 4), out int charsWritten, "x4");
                index += charsWritten;

                if (i < values.Length - 1)
                    span[index++] = ':';
            }
        });
    }

    private static async Task runBodyAsync<T>(T item, SemaphoreSlim throttler, CancellationToken cancellationToken, Func<T, CancellationToken, Task> body)
    {
        try
        {
            await body(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            throttler.Release();
        }
    }
}
