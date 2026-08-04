using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility.Windows;

internal sealed class WindowsBeatmapAccelPlatformRuntime : BeatmapAccelPlatformRuntimeBase
{
    private static readonly System.Reflection.PropertyInfo? connectCallbackProperty = typeof(SocketsHttpHandler).GetProperty("ConnectCallback");

    private static readonly Lazy<bool> hasSystemProxyLazy = new Lazy<bool>(() =>
    {
        try
        {
            // HttpClient.DefaultProxy resolves the system proxy (WinINET) exactly like osu-framework's WebRequest stack.
            // When no proxy is configured it returns a proxy instance whose GetProxy() yields null for every URI.
            return HttpClient.DefaultProxy?.GetProxy(new Uri("https://osu.ppy.sh/")) != null;
        }
        catch
        {
            return false;
        }
    });

    public override string Name => "windows";

    public override bool HasSystemProxy => hasSystemProxyLazy.Value;

    public override long NextInt64(long minInclusive, long maxExclusive)
        => Random.Shared.NextInt64(minInclusive, maxExclusive);

    public override void NextBytes(byte[] buffer)
        => Random.Shared.NextBytes(buffer);

    public override async Task<PreferredIpHttpProbeResponse?> ProbePreferredIpHttpAsync(PreferredIpHttpProbeRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.ConnectTimeout);

        using var handler = createHandler(request.PreferredIp, allowAutoRedirect: false, DecompressionMethods.None, request.ConnectTimeout);
        using var client = CreateHttpClient(handler);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{request.Host}{normalizePath(request.PathAndQuery)}");
        SetRequestHeader(httpRequest, "User-Agent", request.UserAgent);

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if ((int)response.StatusCode >= 500)
                return null;

            return new PreferredIpHttpProbeResponse(response.StatusCode, stopwatch.Elapsed);
        }
        catch
        {
            stopwatch.Stop();
            return null;
        }
    }

    public override async Task DownloadFileAsync(PreferredIpFileDownloadRequest request, CancellationToken cancellationToken)
    {
        using var handler = createHandler(request.PreferredIp, allowAutoRedirect: true, DecompressionMethods.None, request.ConnectTimeout);
        using var client = CreateHttpClient(handler);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.RequestUri);

        foreach (BeatmapAccelHttpHeader header in request.Headers)
            SetRequestHeader(httpRequest, header.Name, header.Value);

        using HttpResponseMessage response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string body = await ReadHttpStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            throw new PreferredIpDownloadHttpException(response.StatusCode, response.ReasonPhrase, body);
        }

        await CopyHttpContentToFileAsync(response.Content, request.DestinationPath, cancellationToken, request.Progress).ConfigureAwait(false);
    }

    private SocketsHttpHandler createHandler(IPAddress? preferredIp, bool allowAutoRedirect, DecompressionMethods decompressionMethods, TimeSpan connectTimeout)
    {
        SocketsHttpHandler handler = CreatePreferredIpHttpHandler(new PreferredIpHttpHandlerOptions(
            preferredIp,
            allowAutoRedirect,
            decompressionMethods,
            connectTimeout,
            PooledConnectionLifetime: TimeSpan.Zero,
            PooledConnectionIdleTimeout: TimeSpan.Zero));

        // With a system proxy active, connecting straight to the preferred IP would bypass the proxy and fail.
        // Leave the default connection path in place so requests go through the proxy (SocketsHttpHandler uses
        // HttpClient.DefaultProxy by default, matching the rest of osu!lazer).
        if (preferredIp == null || connectCallbackProperty == null || HasSystemProxy)
            return handler;

        var callback = new Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>((context, cancellationToken) => connectPreferredIpAsync(preferredIp, context, cancellationToken));
        connectCallbackProperty.SetValue(handler, callback);
        return handler;
    }

    private async ValueTask<Stream> connectPreferredIpAsync(IPAddress preferredIp, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(preferredIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            await ConnectSocketAsync(socket, preferredIp, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string normalizePath(string pathAndQuery)
        => string.IsNullOrWhiteSpace(pathAndQuery) ? "/" : pathAndQuery;
}
