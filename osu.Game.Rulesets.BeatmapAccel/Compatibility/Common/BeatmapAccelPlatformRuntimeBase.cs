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

internal abstract class BeatmapAccelPlatformRuntimeBase : IBeatmapAccelPlatformRuntime
{
    private static readonly System.Reflection.PropertyInfo? connectTimeoutProperty = typeof(SocketsHttpHandler).GetProperty("ConnectTimeout");
    private static readonly System.Reflection.PropertyInfo? pooledConnectionLifetimeProperty = typeof(SocketsHttpHandler).GetProperty("PooledConnectionLifetime");
    private static readonly System.Reflection.PropertyInfo? pooledConnectionIdleTimeoutProperty = typeof(SocketsHttpHandler).GetProperty("PooledConnectionIdleTimeout");

    public abstract string Name { get; }

    public virtual bool PreferConservativeNetworking => false;

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

        var throttler = new SemaphoreSlim(maxDegreeOfParallelism);
        var tasks = new List<Task>();

        try
        {
            foreach (T item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
                tasks.Add(runBodyAsync(item, throttler, cancellationToken, body));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }

            throttler.Dispose();
        }
    }

    public virtual Task ConnectSocketAsync(Socket socket, IPAddress address, int port, CancellationToken cancellationToken)
        => socket.ConnectAsync(address, port, cancellationToken).AsTask();

    public abstract Task<PreferredIpHttpProbeResponse?> ProbePreferredIpHttpAsync(PreferredIpHttpProbeRequest request, CancellationToken cancellationToken);

    public abstract Task DownloadFileAsync(PreferredIpFileDownloadRequest request, CancellationToken cancellationToken);

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

    protected SocketsHttpHandler CreatePreferredIpHttpHandler(PreferredIpHttpHandlerOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = options.AllowAutoRedirect,
            AutomaticDecompression = options.AutomaticDecompression,
        };

        setHandlerPropertyIfAvailable(connectTimeoutProperty, handler, options.ConnectTimeout);
        setHandlerPropertyIfAvailable(pooledConnectionLifetimeProperty, handler, options.PooledConnectionLifetime);
        setHandlerPropertyIfAvailable(pooledConnectionIdleTimeoutProperty, handler, options.PooledConnectionIdleTimeout);
        return handler;
    }

    protected HttpClient CreateHttpClient(HttpMessageHandler handler)
        => new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    protected void SetRequestHeader(HttpRequestMessage request, string name, string value)
        => request.Headers.TryAddWithoutValidation(name, value);

    protected virtual Task<Stream> ReadHttpStreamAsync(HttpContent content, CancellationToken cancellationToken)
        => content.ReadAsStreamAsync();

    protected virtual async Task<string> ReadHttpStringAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using Stream stream = await ReadHttpStreamAsync(content, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    protected virtual Task<int> ReadStreamAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => stream.ReadAsync(buffer, offset, count, cancellationToken);

    protected virtual Task WriteStreamAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => stream.WriteAsync(buffer, offset, count, cancellationToken);

    protected async Task CopyHttpContentToFileAsync(HttpContent content, string destinationPath, CancellationToken cancellationToken, Action<long, long?>? progress)
    {
        long? totalBytes = content.Headers.ContentLength;
        long currentBytes = 0;

        using Stream input = await ReadHttpStreamAsync(content, cancellationToken).ConfigureAwait(false);
        using FileStream output = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] buffer = new byte[81920];

        while (true)
        {
            int read = await ReadStreamAsync(input, buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);

            if (read <= 0)
                break;

            await WriteStreamAsync(output, buffer, 0, read, cancellationToken).ConfigureAwait(false);
            currentBytes += read;
            progress?.Invoke(currentBytes, totalBytes);
        }

        output.Flush();
    }

    protected static bool IsRedirectStatusCode(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Moved
           or HttpStatusCode.Redirect
           or HttpStatusCode.RedirectMethod
           or HttpStatusCode.TemporaryRedirect
           or HttpStatusCode.PermanentRedirect;

    protected static Uri ResolveRedirectUri(Uri currentUri, string location)
        => Uri.TryCreate(location, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(currentUri, location);

    protected static void setHandlerPropertyIfAvailable(System.Reflection.PropertyInfo? property, SocketsHttpHandler handler, object? value)
    {
        if (property == null || value == null)
            return;

        try
        {
            property.SetValue(handler, value);
        }
        catch (Exception e)
        {
            BeatmapAccelLogging.Log($"Unable to set SocketsHttpHandler.{property.Name}: {e.GetType().Name}: {e.Message}");
        }
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
