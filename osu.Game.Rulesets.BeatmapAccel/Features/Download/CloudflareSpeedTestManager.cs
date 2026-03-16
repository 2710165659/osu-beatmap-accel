using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BeatmapAccel.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Download;

public static class CloudflareSpeedTestManager
{
    private const string probe_host = "osu.ppy.sh";
    private const int ipv4_samples_per_range = 2;
    private const int ipv6_samples_per_range = 1;
    private const int tcp_probe_concurrency = 16;
    private const int http_probe_concurrency = 6;
    private const int http_probe_count = 8;

    private static readonly TimeSpan tcp_probe_timeout = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan http_probe_timeout = TimeSpan.FromMilliseconds(2500);
    private static readonly string[] cloudflare_ipv4_ranges =
    {
        "173.245.48.0/20",
        "103.21.244.0/22",
        "103.22.200.0/22",
        "103.31.4.0/22",
        "141.101.64.0/18",
        "108.162.192.0/18",
        "190.93.240.0/20",
        "188.114.96.0/20",
        "197.234.240.0/22",
        "198.41.128.0/17",
        "162.158.0.0/15",
        "104.16.0.0/12",
        "172.64.0.0/17",
        "172.64.128.0/18",
        "172.64.192.0/19",
        "172.64.224.0/22",
        "172.64.229.0/24",
        "172.64.230.0/23",
        "172.64.232.0/21",
        "172.64.240.0/21",
        "172.64.248.0/21",
        "172.65.0.0/16",
        "172.66.0.0/16",
        "172.67.0.0/16",
        "131.0.72.0/22",
    };
    private static readonly string[] cloudflare_ipv6_ranges =
    {
        "2400:cb00::/32",
        "2400:cb00:2049::/48",
        "2400:cb00:f00e::/48",
        "2606:4700::/32",
        "2606:4700:10::/48",
        "2606:4700:130::/48",
        "2606:4700:3000::/48",
        "2606:4700:3001::/48",
        "2606:4700:3002::/48",
        "2606:4700:3003::/48",
        "2606:4700:3004::/48",
        "2606:4700:3005::/48",
        "2606:4700:3006::/48",
        "2606:4700:3007::/48",
        "2606:4700:3008::/48",
        "2606:4700:3009::/48",
        "2606:4700:3010::/48",
        "2606:4700:3011::/48",
        "2606:4700:3012::/48",
        "2606:4700:3013::/48",
        "2606:4700:3014::/48",
        "2606:4700:3015::/48",
        "2606:4700:3016::/48",
        "2606:4700:3017::/48",
        "2606:4700:3018::/48",
        "2606:4700:3019::/48",
        "2606:4700:3020::/48",
        "2606:4700:3021::/48",
        "2606:4700:3022::/48",
        "2606:4700:3023::/48",
        "2606:4700:3024::/48",
        "2606:4700:3025::/48",
        "2606:4700:3026::/48",
        "2606:4700:3027::/48",
        "2606:4700:3028::/48",
        "2606:4700:3029::/48",
        "2606:4700:3030::/48",
        "2606:4700:3031::/48",
        "2606:4700:3032::/48",
        "2606:4700:3033::/48",
        "2606:4700:3034::/48",
        "2606:4700:3035::/48",
        "2606:4700:3036::/48",
        "2606:4700:3037::/48",
        "2606:4700:3038::/48",
        "2606:4700:3039::/48",
        "2606:4700:a0::/48",
        "2606:4700:a1::/48",
        "2606:4700:a8::/48",
        "2606:4700:a9::/48",
        "2606:4700:a::/48",
        "2606:4700:b::/48",
        "2606:4700:c::/48",
        "2606:4700:d0::/48",
        "2606:4700:d1::/48",
        "2606:4700:d::/48",
        "2606:4700:e0::/48",
        "2606:4700:e1::/48",
        "2606:4700:e2::/48",
        "2606:4700:e3::/48",
        "2606:4700:e4::/48",
        "2606:4700:e5::/48",
        "2606:4700:e6::/48",
        "2606:4700:e7::/48",
        "2606:4700:e::/48",
        "2606:4700:f1::/48",
        "2606:4700:f2::/48",
        "2606:4700:f3::/48",
        "2606:4700:f4::/48",
        "2606:4700:f5::/48",
        "2606:4700:f::/48",
        "2803:f800::/32",
        "2803:f800:50::/48",
        "2803:f800:51::/48",
        "2405:b500::/32",
        "2405:8100::/32",
        "2a06:98c0::/29",
        "2a06:98c1:3100::/48",
        "2a06:98c1:3101::/48",
        "2a06:98c1:3102::/48",
        "2a06:98c1:3103::/48",
        "2a06:98c1:3104::/48",
        "2a06:98c1:3105::/48",
        "2a06:98c1:3106::/48",
        "2a06:98c1:3107::/48",
        "2a06:98c1:3108::/48",
        "2a06:98c1:3109::/48",
        "2a06:98c1:310a::/48",
        "2a06:98c1:310b::/48",
        "2a06:98c1:310c::/48",
        "2a06:98c1:310d::/48",
        "2a06:98c1:310e::/48",
        "2a06:98c1:310f::/48",
        "2a06:98c1:3120::/48",
        "2a06:98c1:3121::/48",
        "2a06:98c1:3122::/48",
        "2a06:98c1:3123::/48",
        "2a06:98c1:3200::/48",
        "2a06:98c1:50::/48",
        "2a06:98c1:51::/48",
        "2a06:98c1:54::/48",
        "2a06:98c1:58::/48",
        "2c0f:f248::/32",
    };

    private static readonly SemaphoreSlim switchLock = new(1, 1);

    private static int startupSwitchTriggered;
    private static int failureRecoveryTriggered;

    public static Action<Action>? ScheduleToMainThread { get; set; }

    public static string GetPreferredIp()
        => BeatmapAccelRulesetConfigManager.Instance?.GetPreferredIp() ?? string.Empty;

    public static string GetLastSummary()
        => BeatmapAccelRulesetConfigManager.Instance?.GetLastSpeedTestSummary() ?? "尚未测速";

    public static void BeginStartupSpeedTest()
    {
        if (Interlocked.Exchange(ref startupSwitchTriggered, 1) != 0)
            return;

        var config = BeatmapAccelRulesetConfigManager.Instance;

        if (config == null || !config.GetAutoSwitchOnStartup())
            return;

        _ = Task.Run(async () =>
        {
            SpeedTestSelectionResult result = await SwitchToFastestIpAsync(SpeedTestTrigger.Startup).ConfigureAwait(false);

            if (!result.Success)
                BeatmapAccelLogging.Log($"Startup preferred-IP speed test failed: {result.Message}");
            else
                BeatmapAccelLogging.Log($"Startup preferred-IP speed test selected {result.SelectedIp}. {result.Message}");
        });
    }

    public static void BeginFailureRecoverySpeedTest(Action<Notification>? postNotification = null)
    {
        if (Interlocked.Exchange(ref failureRecoveryTriggered, 1) != 0)
            return;

        var config = BeatmapAccelRulesetConfigManager.Instance;

        if (config == null || !config.GetAutoSwitchOnDownloadFailure())
        {
            Interlocked.Exchange(ref failureRecoveryTriggered, 0);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                SpeedTestSelectionResult result = await SwitchToFastestIpAsync(SpeedTestTrigger.DownloadFailure).ConfigureAwait(false);

                if (result.Success)
                {
                    BeatmapAccelLogging.Log($"Download failure recovery speed test selected {result.SelectedIp}. {result.Message}");
                    postNotification?.Invoke(new SimpleNotification
                    {
                        Text = $"BeatmapAccel: 下载失败后已切换为 {result.SelectedIp}。\n{result.Message}"
                    });
                }
                else
                {
                    BeatmapAccelLogging.Log($"Download failure recovery speed test failed: {result.Message}");
                    postNotification?.Invoke(new SimpleNotification
                    {
                        Text = $"BeatmapAccel: 下载失败后重测速失败。{result.Message}"
                    });
                }
            }
            finally
            {
                Interlocked.Exchange(ref failureRecoveryTriggered, 0);
            }
        });
    }

    public static async Task<SpeedTestSelectionResult> SwitchToFastestIpAsync(SpeedTestTrigger trigger, INotificationOverlay? notifications = null, CancellationToken cancellationToken = default)
    {
        await switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var config = BeatmapAccelRulesetConfigManager.Instance;

            if (config == null)
                return new SpeedTestSelectionResult(false, string.Empty, "测速配置尚未初始化。");

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<ProbeCandidate> candidates = buildCandidates(config.GetPreferredIp());
            List<TcpProbeResult> tcpResults = await probeTcpCandidatesAsync(candidates, cancellationToken).ConfigureAwait(false);

            if (tcpResults.Count == 0)
            {
                stopwatch.Stop();
                return finish(config, notifications, trigger, new SpeedTestSelectionResult(false, string.Empty, buildFailureSummary(trigger, candidates.Count, stopwatch.Elapsed)));
            }

            List<TcpProbeResult> finalists = tcpResults.OrderBy(result => result.Latency).Take(http_probe_count).ToList();
            List<HttpProbeResult> httpResults = await probeHttpCandidatesAsync(finalists, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (httpResults.Count == 0)
            {
                return finish(config, notifications, trigger,
                    new SpeedTestSelectionResult(false, string.Empty, buildHttpFailureSummary(trigger, candidates.Count, tcpResults.Count, stopwatch.Elapsed)));
            }

            HttpProbeResult winner = httpResults
                                     .OrderBy(result => result.HttpLatency)
                                     .ThenBy(result => result.TcpLatency)
                                     .First();

            await runOnMainThreadAsync(() => config.SetPreferredIp(winner.Ip)).ConfigureAwait(false);

            return finish(config, notifications, trigger, new SpeedTestSelectionResult(true, winner.Ip, buildSuccessSummary(trigger, candidates.Count, tcpResults.Count, httpResults.Count, stopwatch.Elapsed, winner)));
        }
        finally
        {
            switchLock.Release();
        }
    }

    private static SpeedTestSelectionResult finish(BeatmapAccelRulesetConfigManager config, INotificationOverlay? notifications, SpeedTestTrigger trigger, SpeedTestSelectionResult result)
    {
        runOnMainThreadAsync(() => config.SetLastSpeedTestSummary(result.Message)).GetAwaiter().GetResult();

        if (trigger == SpeedTestTrigger.Manual && notifications != null)
        {
            runOnMainThreadAsync(() => notifications.Post(new SimpleNotification
            {
                Text = result.Success
                    ? $"BeatmapAccel: 当前已切换为 {result.SelectedIp}。\n{result.Message}"
                    : $"BeatmapAccel: 测速失败。\n{result.Message}"
            })).GetAwaiter().GetResult();
        }

        return result;
    }

    private static Task runOnMainThreadAsync(Action action)
    {
        if (ScheduleToMainThread == null)
        {
            action();
            return Task.CompletedTask;
        }

        var completionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        ScheduleToMainThread(() =>
        {
            try
            {
                action();
                completionSource.SetResult(null);
            }
            catch (Exception e)
            {
                completionSource.SetException(e);
            }
        });

        return completionSource.Task;
    }

    private static List<ProbeCandidate> buildCandidates(string preferredIp)
    {
        var candidates = new Dictionary<string, ProbeCandidate>(StringComparer.Ordinal);

        if (IPAddress.TryParse(preferredIp, out IPAddress? parsedPreferredIp))
            candidates[preferredIp] = new ProbeCandidate("current", preferredIp, true);

        foreach (string cidr in cloudflare_ipv4_ranges)
        {
            foreach (string ip in sampleIpv4Range(cidr, ipv4_samples_per_range))
                candidates.TryAdd(ip, new ProbeCandidate(cidr, ip, false));
        }

        if (BeatmapAccelRulesetConfigManager.Instance?.GetEnableIpv6Candidates() == true)
        {
            foreach (string cidr in cloudflare_ipv6_ranges)
            {
                foreach (string ip in sampleIpv6Range(cidr, ipv6_samples_per_range))
                    candidates.TryAdd(ip, new ProbeCandidate(cidr, ip, false));
            }
        }

        return candidates.Values.ToList();
    }

    private static IEnumerable<string> sampleIpv4Range(string cidr, int sampleCount)
    {
        if (!tryParseCidr(cidr, out uint network, out int prefixLength))
            yield break;

        ulong hostCount = 1UL << (32 - prefixLength);
        ulong minOffset = hostCount > 2 ? 1UL : 0UL;
        ulong maxExclusive = hostCount > 2 ? hostCount - 1UL : hostCount;

        if (maxExclusive <= minOffset)
        {
            yield return formatIpv4(network);
            yield break;
        }

        var offsets = new HashSet<ulong>();

        while (offsets.Count < sampleCount && offsets.Count < (int)(maxExclusive - minOffset))
        {
            long nextOffset = Random.Shared.NextInt64((long)minOffset, (long)maxExclusive);
            if (!offsets.Add((ulong)nextOffset))
                continue;

            yield return formatIpv4(network + (uint)nextOffset);
        }
    }

    private static IEnumerable<string> sampleIpv6Range(string cidr, int sampleCount)
    {
        if (!tryParseIpv6Cidr(cidr, out BigInteger network, out int prefixLength))
            yield break;

        int hostBits = 128 - prefixLength;
        BigInteger maxOffsetExclusive = BigInteger.One << hostBits;
        var yielded = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < sampleCount; i++)
        {
            BigInteger offset = hostBits == 0 ? BigInteger.Zero : randomBigInteger(maxOffsetExclusive);
            string ip = formatIpv6(network + offset);

            if (yielded.Add(ip))
                yield return ip;
        }
    }

    private static async Task<List<TcpProbeResult>> probeTcpCandidatesAsync(IEnumerable<ProbeCandidate> candidates, CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<TcpProbeResult>();

        await Parallel.ForEachAsync(candidates, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = tcp_probe_concurrency
        }, async (candidate, token) =>
        {
            TcpProbeResult result = await probeTcpAsync(candidate, token).ConfigureAwait(false);
            if (result.Success)
                results.Add(result);
        }).ConfigureAwait(false);

        return results.ToList();
    }

    private static async Task<TcpProbeResult> probeTcpAsync(ProbeCandidate candidate, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(candidate.Ip, out IPAddress? address))
            return new TcpProbeResult(candidate, false, TimeSpan.MaxValue);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(tcp_probe_timeout);

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            await socket.ConnectAsync(address, 443, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new TcpProbeResult(candidate, true, stopwatch.Elapsed);
        }
        catch
        {
            stopwatch.Stop();
            return new TcpProbeResult(candidate, false, TimeSpan.MaxValue);
        }
    }

    private static async Task<List<HttpProbeResult>> probeHttpCandidatesAsync(IEnumerable<TcpProbeResult> finalists, CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<HttpProbeResult>();

        await Parallel.ForEachAsync(finalists, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = http_probe_concurrency
        }, async (candidate, token) =>
        {
            HttpProbeResult? result = await probeHttpAsync(candidate, token).ConfigureAwait(false);
            if (result != null)
                results.Add(result);
        }).ConfigureAwait(false);

        return results.ToList();
    }

    private static async Task<HttpProbeResult?> probeHttpAsync(TcpProbeResult candidate, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(candidate.Candidate.Ip, out IPAddress? address))
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(http_probe_timeout);

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = tcp_probe_timeout,
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };

                await socket.ConnectAsync(address, context.DnsEndPoint.Port, token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{probe_host}/");
        request.Headers.TryAddWithoutValidation("User-Agent", "BeatmapAccel-SpeedTest");

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            int statusCode = (int)response.StatusCode;

            if (statusCode >= 500)
                return null;

            return new HttpProbeResult(candidate.Candidate, candidate.Latency, stopwatch.Elapsed, response.StatusCode);
        }
        catch
        {
            stopwatch.Stop();
            return null;
        }
    }

    private static bool tryParseCidr(string cidr, out uint network, out int prefixLength)
    {
        network = 0;
        prefixLength = 0;

        string[] parts = cidr.Split('/');
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out IPAddress? ipAddress) || ipAddress.AddressFamily != AddressFamily.InterNetwork)
            return false;

        if (!int.TryParse(parts[1], out prefixLength) || prefixLength is < 0 or > 32)
            return false;

        uint value = parseIpv4(ipAddress);
        uint mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        network = value & mask;
        return true;
    }

    private static bool tryParseIpv6Cidr(string cidr, out BigInteger network, out int prefixLength)
    {
        network = BigInteger.Zero;
        prefixLength = 0;

        string[] parts = cidr.Split('/');
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out IPAddress? ipAddress) || ipAddress.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (!int.TryParse(parts[1], out prefixLength) || prefixLength is < 0 or > 128)
            return false;

        network = parseIpv6(ipAddress);

        if (prefixLength < 128)
        {
            BigInteger hostMask = (BigInteger.One << (128 - prefixLength)) - 1;
            network &= ~hostMask;
        }

        return true;
    }

    private static uint parseIpv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        return ((uint)bytes[0] << 24)
             | ((uint)bytes[1] << 16)
             | ((uint)bytes[2] << 8)
             | bytes[3];
    }

    private static BigInteger parseIpv6(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        Array.Reverse(bytes);

        byte[] unsignedLittleEndian = new byte[bytes.Length + 1];
        Array.Copy(bytes, unsignedLittleEndian, bytes.Length);
        return new BigInteger(unsignedLittleEndian);
    }

    private static string formatIpv4(uint value)
        => new IPAddress(new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        }).ToString();

    private static string formatIpv6(BigInteger value)
    {
        byte[] littleEndian = value.ToByteArray();
        Array.Resize(ref littleEndian, 16);
        Array.Reverse(littleEndian);
        return new IPAddress(littleEndian).ToString();
    }

    private static BigInteger randomBigInteger(BigInteger exclusiveMax)
    {
        byte[] bytes = exclusiveMax.ToByteArray();
        BigInteger result;

        do
        {
            Random.Shared.NextBytes(bytes);
            bytes[^1] &= 0x7F;
            result = new BigInteger(bytes);
        }
        while (result >= exclusiveMax);

        return result;
    }

    private static string buildFailureSummary(SpeedTestTrigger trigger, int candidateCount, TimeSpan elapsed)
        => $"{describeTrigger(trigger)}：{candidateCount} 个候选 IP 全部 TCP 连接失败，总耗时 {elapsed.TotalSeconds:F1}s";

    private static string buildHttpFailureSummary(SpeedTestTrigger trigger, int candidateCount, int tcpSuccessCount, TimeSpan elapsed)
        => $"{describeTrigger(trigger)}：{candidateCount} 个候选 IP 中 {tcpSuccessCount} 个 TCP 可达，但 HTTP 复测全部失败，总耗时 {elapsed.TotalSeconds:F1}s";

    private static string buildSuccessSummary(SpeedTestTrigger trigger, int candidateCount, int tcpSuccessCount, int httpSuccessCount, TimeSpan elapsed, HttpProbeResult winner)
        => $"{describeTrigger(trigger)}：{candidateCount} 个候选中 {tcpSuccessCount} 个 TCP 可达，{httpSuccessCount} 个 HTTP 复测成功，已选 {winner.Ip}（{winner.Candidate.Cidr}，TCP {winner.TcpLatency.TotalMilliseconds:F0} ms，HTTP {winner.HttpLatency.TotalMilliseconds:F0} ms，状态 {(int)winner.StatusCode}），总耗时 {elapsed.TotalSeconds:F1}s";

    private static string describeTrigger(SpeedTestTrigger trigger)
        => trigger switch
        {
            SpeedTestTrigger.Startup => "启动自动测速",
            SpeedTestTrigger.DownloadFailure => "下载失败后自动测速",
            _ => "手动测速"
        };

    private sealed record ProbeCandidate(string Cidr, string Ip, bool IsCurrent);

    private sealed record TcpProbeResult(ProbeCandidate Candidate, bool Success, TimeSpan Latency);

    private sealed record HttpProbeResult(ProbeCandidate Candidate, TimeSpan TcpLatency, TimeSpan HttpLatency, HttpStatusCode StatusCode)
    {
        public string Ip => Candidate.Ip;
    }
}

public readonly record struct SpeedTestSelectionResult(bool Success, string SelectedIp, string Message);

public enum SpeedTestTrigger
{
    Startup,
    Manual,
    DownloadFailure,
}
