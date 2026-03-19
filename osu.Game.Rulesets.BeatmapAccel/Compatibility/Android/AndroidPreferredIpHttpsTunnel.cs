using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility.Android;

internal static class AndroidPreferredIpHttpsTunnel
{
    private const int header_limit_bytes = 64 * 1024;
    private const int body_buffer_size = 81920;
    private const int error_body_limit_bytes = 4096;
    private static readonly TimeSpan io_timeout = TimeSpan.FromSeconds(30);
    private static readonly ConstructorInfo? sslStreamCtorWithLeaveInnerStreamOpen = typeof(SslStream).GetConstructor(new[] { typeof(Stream), typeof(bool) });
    private static readonly ConstructorInfo? sslStreamCtorWithStreamOnly = typeof(SslStream).GetConstructor(new[] { typeof(Stream) });
    private static readonly MethodInfo? authenticateAsClientStringMethod = typeof(SslStream).GetMethod(nameof(SslStream.AuthenticateAsClient), new[] { typeof(string) });
    private static readonly MethodInfo? authenticateAsClientLegacyMethod = typeof(SslStream).GetMethod(nameof(SslStream.AuthenticateAsClient), new[] { typeof(string), typeof(X509CertificateCollection), typeof(SslProtocols), typeof(bool) });

    public static Task<PreferredIpHttpProbeResponse?> ProbeAsync(PreferredIpHttpProbeRequest request, CancellationToken cancellationToken)
        => Task.Run(() => probeSync(request, cancellationToken), CancellationToken.None);

    public static Task DownloadFileAsync(PreferredIpFileDownloadRequest request, CancellationToken cancellationToken)
        => Task.Run(() => downloadSync(request, cancellationToken), CancellationToken.None);

    private static PreferredIpHttpProbeResponse? probeSync(PreferredIpHttpProbeRequest request, CancellationToken cancellationToken)
    {
        var requestUri = new Uri($"https://{request.Host}{normalizePath(request.PathAndQuery)}");
        DateTime startedAt = DateTime.UtcNow;

        using ManualHttpResponse response = sendGetRequestSync(requestUri, request.PreferredIp, new[]
        {
            new BeatmapAccelHttpHeader("User-Agent", request.UserAgent)
        }, request.ConnectTimeout, cancellationToken);

        TimeSpan latency = DateTime.UtcNow - startedAt;
        return (int)response.StatusCode >= 500
            ? null
            : new PreferredIpHttpProbeResponse(response.StatusCode, latency);
    }

    private static void downloadSync(PreferredIpFileDownloadRequest request, CancellationToken cancellationToken)
    {
        Uri currentUri = request.RequestUri;

        for (int redirectCount = 0; redirectCount <= request.MaxRedirects; redirectCount++)
        {
            using ManualHttpResponse response = sendGetRequestSync(currentUri, request.PreferredIp, request.Headers, request.ConnectTimeout, cancellationToken);

            if (IsRedirectStatusCode(response.StatusCode))
            {
                string? location = response.GetHeaderValue("Location");

                if (string.IsNullOrWhiteSpace(location))
                    throw new InvalidOperationException($"HTTP {(int)response.StatusCode} redirect without Location header.");

                currentUri = resolveRedirectUri(currentUri, location);
                continue;
            }

            if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
            {
                string body = response.ReadBodyAsString(error_body_limit_bytes);
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} {body}");
            }

            response.CopyBodyToFile(request.DestinationPath, request.Progress);
            return;
        }

        throw new InvalidOperationException($"HTTP redirect limit exceeded for {request.RequestUri}.");
    }

    private static ManualHttpResponse sendGetRequestSync(Uri requestUri, IPAddress? preferredIp, IReadOnlyList<BeatmapAccelHttpHeader> headers, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IPAddress address = preferredIp ?? resolveAddress(requestUri);
        var transport = new TransportState();
        CancellationTokenSource? connectTimeoutSource = null;
        CancellationTokenRegistration connectRegistration = default;

        try
        {
            connectTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeoutSource.CancelAfter(connectTimeout);
            connectRegistration = connectTimeoutSource.Token.Register(static state => ((TransportState)state!).Dispose(), transport);

            Socket socket = createSocket(address);
            transport.AttachSocket(socket);
            socket.Connect(new IPEndPoint(address, 443));

            var networkStream = new NetworkStream(socket, ownsSocket: true);
            SslStream sslStream = createSslStream(networkStream);
            sslStream.ReadTimeout = (int)Math.Min(io_timeout.TotalMilliseconds, int.MaxValue);
            sslStream.WriteTimeout = (int)Math.Min(io_timeout.TotalMilliseconds, int.MaxValue);
            transport.AttachSslStream(sslStream);

            authenticateAsClient(sslStream, requestUri.Host);

            byte[] requestBytes = buildGetRequestBytes(requestUri, headers);
            sslStream.Write(requestBytes, 0, requestBytes.Length);
            sslStream.Flush();

            ResponseHeaders headersResult = readResponseHeaders(sslStream);
            connectRegistration.Dispose();
            connectTimeoutSource.Dispose();
            connectRegistration = default;
            connectTimeoutSource = null;

            return new ManualHttpResponse(transport, cancellationToken, headersResult);
        }
        catch (Exception e) when (isCancellation(e, cancellationToken))
        {
            transport.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
        finally
        {
            connectRegistration.Dispose();
            connectTimeoutSource?.Dispose();
        }
    }

    private static Socket createSocket(IPAddress address)
        => new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            ReceiveTimeout = (int)Math.Min(io_timeout.TotalMilliseconds, int.MaxValue),
            SendTimeout = (int)Math.Min(io_timeout.TotalMilliseconds, int.MaxValue)
        };

    private static SslStream createSslStream(Stream transportStream)
    {
        if (sslStreamCtorWithStreamOnly != null)
            return (SslStream)sslStreamCtorWithStreamOnly.Invoke(new object[] { transportStream });

        if (sslStreamCtorWithLeaveInnerStreamOpen != null)
            return (SslStream)sslStreamCtorWithLeaveInnerStreamOpen.Invoke(new object[] { transportStream, false });

        throw new InvalidOperationException("Compatible System.Net.Security.SslStream constructor not found.");
    }

    private static void authenticateAsClient(SslStream sslStream, string host)
    {
        if (authenticateAsClientLegacyMethod != null)
        {
            authenticateAsClientLegacyMethod.Invoke(sslStream, new object[] { host, new X509CertificateCollection(), SslProtocols.None, false });
            return;
        }

        if (authenticateAsClientStringMethod != null)
        {
            authenticateAsClientStringMethod.Invoke(sslStream, new object[] { host });
            return;
        }

        throw new InvalidOperationException("Compatible SslStream.AuthenticateAsClient overload not found.");
    }

    private static byte[] buildGetRequestBytes(Uri uri, IReadOnlyList<BeatmapAccelHttpHeader> headers)
    {
        var builder = new StringBuilder();
        builder.Append("GET ").Append(string.IsNullOrWhiteSpace(uri.PathAndQuery) ? "/" : uri.PathAndQuery).Append(" HTTP/1.1\r\n");
        builder.Append("Host: ").Append(uri.Authority).Append("\r\n");
        builder.Append("Connection: close\r\n");
        builder.Append("Accept-Encoding: identity\r\n");

        foreach (BeatmapAccelHttpHeader header in headers)
        {
            if (header.Name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                continue;

            if (header.Name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                continue;

            if (header.Name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        builder.Append("\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static ResponseHeaders readResponseHeaders(Stream stream)
    {
        byte[] headerBytes = new byte[header_limit_bytes];
        int totalRead = 0;
        int headerEnd = -1;

        while (headerEnd < 0)
        {
            if (totalRead == headerBytes.Length)
                throw new InvalidOperationException("HTTP response headers exceed the supported size limit.");

            int read = stream.Read(headerBytes, totalRead, headerBytes.Length - totalRead);

            if (read <= 0)
                throw new InvalidOperationException("Connection closed before HTTP response headers were received.");

            totalRead += read;
            headerEnd = findHeaderTerminator(headerBytes, totalRead);
        }

        int bodyOffset = headerEnd + 4;
        int headerLength = headerEnd;
        int prefetchedBodyLength = totalRead - bodyOffset;

        string headerText = Encoding.ASCII.GetString(headerBytes, 0, headerLength);
        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);

        if (lines.Length == 0)
            throw new InvalidOperationException("Received an empty HTTP response.");

        string[] statusParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int statusCode))
            throw new InvalidOperationException($"Invalid HTTP status line: {lines[0]}");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i]))
                continue;

            int separatorIndex = lines[i].IndexOf(':');
            if (separatorIndex <= 0)
                continue;

            string name = lines[i].Substring(0, separatorIndex).Trim();
            string value = lines[i].Substring(separatorIndex + 1).Trim();
            headers[name] = value;
        }

        byte[] prefetchedBody = prefetchedBodyLength > 0 ? new byte[prefetchedBodyLength] : Array.Empty<byte>();

        if (prefetchedBodyLength > 0)
            Array.Copy(headerBytes, bodyOffset, prefetchedBody, 0, prefetchedBodyLength);

        return new ResponseHeaders((HttpStatusCode)statusCode, statusParts.Length >= 3 ? statusParts[2] : string.Empty, headers, prefetchedBody);
    }

    private static int findHeaderTerminator(byte[] buffer, int length)
    {
        for (int i = 3; i < length; i++)
        {
            if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                return i - 3;
        }

        return -1;
    }

    private static IPAddress resolveAddress(Uri uri)
    {
        IPAddress[] addresses = Dns.GetHostAddresses(uri.Host);

        if (addresses.Length == 0)
            throw new InvalidOperationException($"Unable to resolve host {uri.Host}.");

        return addresses[0];
    }

    private static bool isCancellation(Exception exception, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
            return false;

        if (exception is OperationCanceledException)
            return true;

        if (exception is ObjectDisposedException)
            return true;

        if (exception is IOException)
            return true;

        if (exception is AuthenticationException)
            return false;

        return exception is SocketException;
    }

    private static string normalizePath(string pathAndQuery)
        => string.IsNullOrWhiteSpace(pathAndQuery) ? "/" : pathAndQuery;

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Moved
           or HttpStatusCode.Redirect
           or HttpStatusCode.RedirectMethod
           or HttpStatusCode.TemporaryRedirect
           or HttpStatusCode.PermanentRedirect;

    private static Uri resolveRedirectUri(Uri currentUri, string location)
        => Uri.TryCreate(location, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(currentUri, location);

    private sealed class ManualHttpResponse : IDisposable
    {
        private readonly TransportState transport;
        private readonly CancellationTokenRegistration cancellationRegistration;
        private readonly Dictionary<string, string> headers;
        private readonly BufferedBodyReader bodyReader;

        public HttpStatusCode StatusCode { get; }

        public string ReasonPhrase { get; }

        public ManualHttpResponse(TransportState transport, CancellationToken cancellationToken, ResponseHeaders responseHeaders)
        {
            this.transport = transport;
            headers = responseHeaders.Headers;
            StatusCode = responseHeaders.StatusCode;
            ReasonPhrase = responseHeaders.ReasonPhrase;
            bodyReader = new BufferedBodyReader(transport.GetReadableStream(), responseHeaders.PrefetchedBody);
            cancellationRegistration = cancellationToken.Register(static state => ((TransportState)state!).Dispose(), transport);
        }

        public string? GetHeaderValue(string name)
            => headers.TryGetValue(name, out string? value) ? value : null;

        public string ReadBodyAsString(int maxBytes)
        {
            byte[] body = readBodyBytes(maxBytes);
            return body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(body);
        }

        public void CopyBodyToFile(string destinationPath, Action<long, long?>? progress)
        {
            using FileStream output = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, body_buffer_size, useAsync: false);
            long written = 0;
            long? totalBytes = tryGetContentLength();

            if (isChunked())
                copyChunkedBody(output, ref written, totalBytes, progress);
            else if (totalBytes.HasValue)
                copyFixedLengthBody(output, totalBytes.Value, ref written, progress);
            else
                copyUntilEnd(output, ref written, progress);

            output.Flush();
        }

        public void Dispose()
        {
            cancellationRegistration.Dispose();
            transport.Dispose();
        }

        private byte[] readBodyBytes(int maxBytes)
        {
            using var stream = new MemoryStream();
            var buffer = new byte[Math.Min(body_buffer_size, maxBytes)];
            int remaining = maxBytes;

            while (remaining > 0)
            {
                int read = bodyReader.Read(buffer, 0, Math.Min(buffer.Length, remaining));

                if (read <= 0)
                    break;

                stream.Write(buffer, 0, read);
                remaining -= read;
            }

            return stream.ToArray();
        }

        private void copyChunkedBody(Stream output, ref long written, long? totalBytes, Action<long, long?>? progress)
        {
            while (true)
            {
                string sizeLine = bodyReader.ReadAsciiLine();
                int extensionIndex = sizeLine.IndexOf(';');

                if (extensionIndex >= 0)
                    sizeLine = sizeLine.Substring(0, extensionIndex);

                if (!long.TryParse(sizeLine.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long chunkSize))
                    throw new InvalidOperationException($"Invalid chunk size '{sizeLine}'.");

                if (chunkSize == 0)
                {
                    while (!string.IsNullOrEmpty(bodyReader.ReadAsciiLine()))
                    {
                    }

                    return;
                }

                copyKnownLength(bodyReader, output, chunkSize, ref written, totalBytes, progress);
                expectChunkTerminator();
            }
        }

        private void copyFixedLengthBody(Stream output, long contentLength, ref long written, Action<long, long?>? progress)
            => copyKnownLength(bodyReader, output, contentLength, ref written, contentLength, progress);

        private void copyUntilEnd(Stream output, ref long written, Action<long, long?>? progress)
        {
            var buffer = new byte[body_buffer_size];

            while (true)
            {
                int read = bodyReader.Read(buffer, 0, buffer.Length);

                if (read <= 0)
                    break;

                output.Write(buffer, 0, read);
                written += read;
                progress?.Invoke(written, null);
            }
        }

        private static void copyKnownLength(BufferedBodyReader reader, Stream output, long remaining, ref long written, long? totalBytes, Action<long, long?>? progress)
        {
            var buffer = new byte[body_buffer_size];

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = reader.Read(buffer, 0, toRead);

                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading HTTP response body.");

                output.Write(buffer, 0, read);
                remaining -= read;
                written += read;
                progress?.Invoke(written, totalBytes);
            }
        }

        private void expectChunkTerminator()
        {
            int first = bodyReader.ReadByte();
            int second = bodyReader.ReadByte();

            if (first != '\r' || second != '\n')
                throw new InvalidOperationException("Invalid chunk terminator in HTTP response body.");
        }

        private bool isChunked()
            => headers.TryGetValue("Transfer-Encoding", out string? transferEncoding)
               && transferEncoding.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0;

        private long? tryGetContentLength()
        {
            return headers.TryGetValue("Content-Length", out string? value)
                   && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long contentLength)
                ? contentLength
                : null;
        }
    }

    private sealed class BufferedBodyReader
    {
        private readonly Stream stream;
        private readonly byte[] prefetched;
        private int prefetchedOffset;

        public BufferedBodyReader(Stream stream, byte[] prefetched)
        {
            this.stream = stream;
            this.prefetched = prefetched;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0)
                return 0;

            int copied = 0;

            if (prefetchedOffset < prefetched.Length)
            {
                copied = Math.Min(count, prefetched.Length - prefetchedOffset);
                Array.Copy(prefetched, prefetchedOffset, buffer, offset, copied);
                prefetchedOffset += copied;
            }

            if (copied == count)
                return copied;

            int read = stream.Read(buffer, offset + copied, count - copied);
            return copied + read;
        }

        public int ReadByte()
        {
            if (prefetchedOffset < prefetched.Length)
                return prefetched[prefetchedOffset++];

            return stream.ReadByte();
        }

        public string ReadAsciiLine()
        {
            using var line = new MemoryStream();
            bool sawCarriageReturn = false;

            while (true)
            {
                int value = ReadByte();

                if (value < 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading an HTTP line.");

                if (sawCarriageReturn)
                {
                    if (value == '\n')
                        return Encoding.ASCII.GetString(line.ToArray());

                    line.WriteByte((byte)'\r');
                    sawCarriageReturn = false;
                }

                if (value == '\r')
                {
                    sawCarriageReturn = true;
                    continue;
                }

                line.WriteByte((byte)value);

                if (line.Length > 8192)
                    throw new InvalidOperationException("HTTP line exceeds the supported size limit.");
            }
        }
    }

    private sealed class TransportState : IDisposable
    {
        private Socket? socket;
        private SslStream? sslStream;
        private int disposed;

        public void AttachSocket(Socket socket)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                socket.Dispose();
                throw new OperationCanceledException();
            }

            this.socket = socket;
        }

        public void AttachSslStream(SslStream sslStream)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                sslStream.Dispose();
                throw new OperationCanceledException();
            }

            this.sslStream = sslStream;
        }

        public Stream GetReadableStream()
            => sslStream ?? throw new InvalidOperationException("HTTPS stream is not attached.");

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            try
            {
                sslStream?.Dispose();
            }
            catch
            {
            }

            try
            {
                socket?.Dispose();
            }
            catch
            {
            }
        }
    }

    private sealed record ResponseHeaders(
        HttpStatusCode StatusCode,
        string ReasonPhrase,
        Dictionary<string, string> Headers,
        byte[] PrefetchedBody);
}
