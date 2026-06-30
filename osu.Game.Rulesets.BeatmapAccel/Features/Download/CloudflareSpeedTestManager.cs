using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BeatmapAccel.Compatibility;
using osu.Game.Rulesets.BeatmapAccel.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
    private const int http_probe_count = 8;
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
    private static readonly object failureRecoveryLock = new();
    private static readonly IBeatmapAccelPlatformRuntime runtime = BeatmapAccelCompatibility.Current;
    private static readonly TimeSpan default_tcp_probe_timeout = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan mobile_tcp_probe_timeout = TimeSpan.FromMilliseconds(3000);
    private static readonly TimeSpan mobile_tcp_probe_retry_timeout = TimeSpan.FromMilliseconds(5000);
    private static readonly TimeSpan default_http_probe_timeout = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan mobile_http_probe_timeout = TimeSpan.FromMilliseconds(5000);
    private static readonly TimeSpan default_speed_test_timeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan mobile_speed_test_timeout = TimeSpan.FromSeconds(30);

    private static int startupSwitchTriggered;
    private static int failureRecoveryTriggered;
    private static int failureRecoveryKeepCurrentSkipCount;

    private const int failure_detail_limit = 3;
    private const int mobile_retry_candidate_limit = 8;
    // 同一优选 IP 连续 N 次"探测存活却仍下载失败"后，强制走完整切换，避免 keep-current 探测造成死锁。
    private const int failure_recovery_force_switch_after = 3;

    public static Action<Action>? ScheduleToMainThread { get; set; }
    public static Action<Notification>? NotificationPoster { get; set; }

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
            try
            {
                BeatmapAccelLogging.Log($"Startup speed test running on {runtime.Name} runtime strategy.");
                SpeedTestSelectionResult result = await SwitchToFastestIpAsync(SpeedTestTrigger.Startup).ConfigureAwait(false);

                if (!result.Success)
                    BeatmapAccelLogging.Log($"Startup preferred-IP speed test failed: {result.Message}");
                else
                    BeatmapAccelLogging.Log($"Startup preferred-IP speed test selected {result.SelectedIp}. {result.Message}");
            }
            catch (Exception e)
            {
                BeatmapAccelLogging.LogError(e, "Startup speed test threw an unexpected exception.");
            }
        });
    }

    public static void BeginFailureRecoverySpeedTest(Action<Notification>? postNotification = null)
    {
        if (Interlocked.Exchange(ref failureRecoveryTriggered, 1) != 0)
            return;

        string preferredIp;

        try
        {
            var config = BeatmapAccelRulesetConfigManager.Instance;

            if (config == null || !config.GetAutoSwitchOnDownloadFailure())
            {
                Interlocked.Exchange(ref failureRecoveryTriggered, 0);
                return;
            }

            preferredIp = config.GetPreferredIp();
        }
        catch (Exception e)
        {
            // 前言阶段抛异常必须复位单飞标志，否则 failureRecoveryTriggered 永久卡在 1，后续恢复全部失效。
            Interlocked.Exchange(ref failureRecoveryTriggered, 0);
            BeatmapAccelLogging.LogError(e, "Failed to begin download failure recovery.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                int skipCount;
                bool shouldForceSwitch;

                lock (failureRecoveryLock)
                {
                    skipCount = failureRecoveryKeepCurrentSkipCount;
                    shouldForceSwitch = skipCount + 1 >= failure_recovery_force_switch_after;
                }

                // 小探测通过不代表大文件持续传输也能成功（移动端常见"传一半被掐"）。
                // 仅在尚未触及强制切换阈值时探测当前 IP：探测存活则跳过切换并累计跳过次数；
                // 达到阈值后不再做无谓探测，直接走完整切换，避免死循环。
                bool probeSucceeded;

                if (!shouldForceSwitch)
                {
                    using var probeTimeout = new CancellationTokenSource();
                    probeTimeout.CancelAfter(getTcpProbeTimeout() + getHttpProbeTimeout() + TimeSpan.FromSeconds(1));
                    probeSucceeded = await tryKeepCurrentPreferredIpAsync(preferredIp, probeTimeout.Token).ConfigureAwait(false);
                }
                else
                {
                    BeatmapAccelLogging.Log($"Current preferred IP {preferredIp} survived {skipCount} consecutive recovery probes while downloads keep failing; forcing a full switch.");
                    probeSucceeded = false;
                }

                if (probeSucceeded)
                {
                    int newSkipCount;
                    lock (failureRecoveryLock)
                        newSkipCount = ++failureRecoveryKeepCurrentSkipCount;

                    string message = $"当前优选 IP {preferredIp} 探测可用，已跳过切换（第 {newSkipCount} 次，连续 {failure_recovery_force_switch_after} 次后将强制切换）。";
                    BeatmapAccelLogging.Log($"Download failure recovery skipped. {message}");
                    postNotificationOrFallback(new SimpleNotification
                    {
                        Text = $"BeatmapAccel: {message}"
                    }, postNotification);
                    return;
                }

                // 探测失败（"探测存活"连续被打断）或已达阈值强制切换：重置跳过计数，让切换后从新 IP 重新起算。
                lock (failureRecoveryLock)
                    failureRecoveryKeepCurrentSkipCount = 0;

                BeatmapAccelLogging.Log($"Failure recovery speed test running on {runtime.Name} runtime strategy.");
                SpeedTestSelectionResult result = await SwitchToFastestIpAsync(SpeedTestTrigger.DownloadFailure).ConfigureAwait(false);

                if (result.Success)
                {
                    BeatmapAccelLogging.Log($"Download failure recovery speed test selected {result.SelectedIp}. {result.Message}");
                    postNotificationOrFallback(new SimpleNotification
                    {
                        Text = $"BeatmapAccel: 下载失败后已切换为 {result.SelectedIp}。\n{result.Message}"
                    }, postNotification);
                }
                else
                {
                    BeatmapAccelLogging.Log($"Download failure recovery speed test failed: {result.Message}");
                    postNotificationOrFallback(new SimpleNotification
                    {
                        Text = $"BeatmapAccel: 下载失败后重测速失败。{result.Message}"
                    }, postNotification);
                }
            }
            catch (Exception e)
            {
                // finally 会复位单飞标志；此处吞掉异常避免 fire-and-forget 的未观察任务异常。
                BeatmapAccelLogging.LogError(e, "Download failure recovery speed test threw an unexpected exception.");
            }
            finally
            {
                Interlocked.Exchange(ref failureRecoveryTriggered, 0);
            }
        });
    }

    /// <summary>
    /// 一次下载成功说明当前优选 IP 可用，重置"探测存活却下载失败"的连续计数。
    /// </summary>
    public static void OnDownloadSucceeded()
    {
        lock (failureRecoveryLock)
        {
            if (failureRecoveryKeepCurrentSkipCount != 0)
                failureRecoveryKeepCurrentSkipCount = 0;
        }
    }

    public static async Task<SpeedTestSelectionResult> SwitchToFastestIpAsync(SpeedTestTrigger trigger, INotificationOverlay? notifications = null, CancellationToken cancellationToken = default)
    {
        using var overallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallTimeout.CancelAfter(getOverallSpeedTestTimeout());

        await switchLock.WaitAsync(overallTimeout.Token).ConfigureAwait(false);

        try
        {
            var config = BeatmapAccelRulesetConfigManager.Instance;

            if (config == null)
                return new SpeedTestSelectionResult(false, string.Empty, "测速配置尚未初始化。");

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<ProbeCandidate> candidates = buildCandidates(config.GetPreferredIp());
            BeatmapAccelLogging.Log($"Speed test built {candidates.Count} candidates on {runtime.Name} runtime strategy.");
            List<TcpProbeResult> tcpResults = await probeTcpCandidatesAsync(candidates, overallTimeout.Token).ConfigureAwait(false);

            if (tcpResults.Count == 0 && runtime.PreferConservativeNetworking)
            {
                BeatmapAccelLogging.Log("No TCP speed test candidates succeeded on conservative runtime. Retrying with reduced concurrency and longer timeout.");
                tcpResults = await probeTcpCandidatesAsync(candidates.Take(mobile_retry_candidate_limit), overallTimeout.Token, 1, mobile_tcp_probe_retry_timeout).ConfigureAwait(false);
            }

            if (tcpResults.Count == 0)
            {
                stopwatch.Stop();
                return await finishAsync(config, notifications, trigger, new SpeedTestSelectionResult(false, string.Empty, buildFailureSummary(trigger, candidates.Count, stopwatch.Elapsed, candidates))).ConfigureAwait(false);
            }

            List<TcpProbeResult> finalists = tcpResults.OrderBy(result => result.Latency).Take(http_probe_count).ToList();
            List<HttpProbeResult> httpResults = await probeHttpCandidatesAsync(finalists, overallTimeout.Token).ConfigureAwait(false);

            stopwatch.Stop();

            if (httpResults.Count == 0)
            {
                return await finishAsync(config, notifications, trigger,
                    new SpeedTestSelectionResult(false, string.Empty, buildHttpFailureSummary(trigger, candidates.Count, tcpResults.Count, stopwatch.Elapsed, finalists))).ConfigureAwait(false);
            }

            HttpProbeResult winner = httpResults
                                     .OrderBy(result => result.HttpLatency)
                                     .ThenBy(result => result.TcpLatency)
                                     .First();

            await runOnMainThreadAsync(() => config.SetPreferredIp(winner.Ip)).ConfigureAwait(false);
            resetFailureRecoverySkipCount();

            return await finishAsync(config, notifications, trigger, new SpeedTestSelectionResult(true, winner.Ip, buildSuccessSummary(trigger, candidates.Count, tcpResults.Count, httpResults.Count, stopwatch.Elapsed, winner))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (overallTimeout.IsCancellationRequested)
        {
            return new SpeedTestSelectionResult(false, string.Empty, $"测速超时：在 {getOverallSpeedTestTimeout().TotalSeconds:F0}s 内未能完成。");
        }
        finally
        {
            switchLock.Release();
        }
    }

    private static async Task<SpeedTestSelectionResult> finishAsync(BeatmapAccelRulesetConfigManager config, INotificationOverlay? notifications, SpeedTestTrigger trigger, SpeedTestSelectionResult result)
    {
        await runOnMainThreadAsync(() => config.SetLastSpeedTestSummary(result.Message)).ConfigureAwait(false);

        if (trigger == SpeedTestTrigger.Manual && notifications != null)
        {
            await runOnMainThreadAsync(() => postNotificationOrFallback(new SimpleNotification
            {
                Text = result.Success
                    ? $"BeatmapAccel: 当前已切换为 {result.SelectedIp}。\n{result.Message}"
                    : $"BeatmapAccel: 测速失败。\n{result.Message}"
            }, notifications.Post)).ConfigureAwait(false);
        }
        else if (trigger == SpeedTestTrigger.Manual)
        {
            await runOnMainThreadAsync(() => postNotificationOrFallback(new SimpleNotification
            {
                Text = result.Success
                    ? $"BeatmapAccel: 当前已切换为 {result.SelectedIp}。\n{result.Message}"
                    : $"BeatmapAccel: 测速失败。\n{result.Message}"
            })).ConfigureAwait(false);
        }

        return result;
    }

    private static void resetFailureRecoverySkipCount()
    {
        lock (failureRecoveryLock)
            failureRecoveryKeepCurrentSkipCount = 0;
    }

    private static async Task<bool> tryKeepCurrentPreferredIpAsync(string preferredIp, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(preferredIp, out _))
            return false;

        var candidate = new ProbeCandidate("current", preferredIp, true);
        TcpProbeResult tcpResult = await probeTcpAsync(candidate, getTcpProbeTimeout(), cancellationToken).ConfigureAwait(false);

        if (!tcpResult.Success)
        {
            BeatmapAccelLogging.Log($"Current preferred IP {preferredIp} TCP probe failed before failure recovery: {tcpResult.FailureReason}");
            return false;
        }

        HttpProbeResult? httpResult = await probeHttpAsync(tcpResult, cancellationToken).ConfigureAwait(false);

        if (httpResult == null)
        {
            BeatmapAccelLogging.Log($"Current preferred IP {preferredIp} HTTP probe failed before failure recovery.");
            return false;
        }

        BeatmapAccelLogging.Log($"Current preferred IP {preferredIp} probe succeeded before failure recovery: TCP {tcpResult.Latency.TotalMilliseconds:F0} ms, HTTP {httpResult.HttpLatency.TotalMilliseconds:F0} ms, status {(int)httpResult.StatusCode}.");
        return true;
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

    private static void postNotificationOrFallback(Notification notification, Action<Notification>? preferredPoster = null)
    {
        (preferredPoster ?? NotificationPoster)?.Invoke(notification);
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
            long nextOffset = runtime.NextInt64((long)minOffset, (long)maxExclusive);
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
        => await probeTcpCandidatesAsync(candidates, cancellationToken, getTcpProbeConcurrency(), getTcpProbeTimeout()).ConfigureAwait(false);

    private static async Task<List<TcpProbeResult>> probeTcpCandidatesAsync(IEnumerable<ProbeCandidate> candidates, CancellationToken cancellationToken, int maxConcurrency, TimeSpan timeout)
    {
        var results = new ConcurrentBag<TcpProbeResult>();

        await runtime.ForEachAsync(candidates, maxConcurrency, cancellationToken, async (candidate, token) =>
        {
            TcpProbeResult result = await probeTcpAsync(candidate, timeout, token).ConfigureAwait(false);
            if (result.Success)
                results.Add(result);
        }).ConfigureAwait(false);

        return results.ToList();
    }

    private static async Task<TcpProbeResult> probeTcpAsync(ProbeCandidate candidate, TimeSpan timeoutDuration, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(candidate.Ip, out IPAddress? address))
            return new TcpProbeResult(candidate, false, TimeSpan.MaxValue, "invalid ip");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration);

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            await runtime.ConnectSocketAsync(socket, address, 443, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new TcpProbeResult(candidate, true, stopwatch.Elapsed, null);
        }
        catch (Exception e)
        {
            stopwatch.Stop();
            return new TcpProbeResult(candidate, false, TimeSpan.MaxValue, describeProbeError(e));
        }
    }

    private static async Task<List<HttpProbeResult>> probeHttpCandidatesAsync(IEnumerable<TcpProbeResult> finalists, CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<HttpProbeResult>();

        await runtime.ForEachAsync(finalists, getHttpProbeConcurrency(), cancellationToken, async (candidate, token) =>
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
        timeout.CancelAfter(getHttpProbeTimeout());

        try
        {
            PreferredIpHttpProbeResponse? response = await runtime.ProbePreferredIpHttpAsync(new PreferredIpHttpProbeRequest(
                address,
                probe_host,
                "/",
                "BeatmapAccel-SpeedTest",
                getTcpProbeTimeout()), timeout.Token).ConfigureAwait(false);

            if (response == null || (int)response.Value.StatusCode >= 500)
                return null;

            return new HttpProbeResult(candidate.Candidate, candidate.Latency, response.Value.Latency, response.Value.StatusCode, null);
        }
        catch (Exception e)
        {
            BeatmapAccelLogging.Log($"HTTP probe failed for {candidate.Candidate.Ip}: {describeProbeError(e)}");
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
        => runtime.FormatIpv4(value);

    private static string formatIpv6(BigInteger value)
        => runtime.FormatIpv6(value);

    private static BigInteger randomBigInteger(BigInteger exclusiveMax)
    {
        byte[] bytes = exclusiveMax.ToByteArray();
        BigInteger result;

        do
        {
            runtime.NextBytes(bytes);
            bytes[^1] &= 0x7F;
            result = new BigInteger(bytes);
        }
        while (result >= exclusiveMax);

        return result;
    }

    private static int getTcpProbeConcurrency()
        => runtime.PreferConservativeNetworking ? 4 : 16;

    private static int getHttpProbeConcurrency()
        => runtime.PreferConservativeNetworking ? 2 : 6;

    private static TimeSpan getTcpProbeTimeout()
        => runtime.PreferConservativeNetworking ? mobile_tcp_probe_timeout : default_tcp_probe_timeout;

    private static TimeSpan getHttpProbeTimeout()
        => runtime.PreferConservativeNetworking ? mobile_http_probe_timeout : default_http_probe_timeout;

    private static TimeSpan getOverallSpeedTestTimeout()
        => runtime.PreferConservativeNetworking ? mobile_speed_test_timeout : default_speed_test_timeout;

    private static string buildFailureSummary(SpeedTestTrigger trigger, int candidateCount, TimeSpan elapsed, IEnumerable<ProbeCandidate> candidates)
    {
        string detail = string.Join("；", candidates
                                         .Select(candidate => candidate.LastTcpFailure)
                                         .Where(message => !string.IsNullOrWhiteSpace(message))
                                         .Distinct()
                                         .Take(failure_detail_limit)!);

        if (!string.IsNullOrWhiteSpace(detail))
            BeatmapAccelLogging.Log($"Speed test TCP failures: {detail}");

        return string.IsNullOrWhiteSpace(detail)
            ? $"{describeTrigger(trigger)}：{candidateCount} 个候选 IP 全部 TCP 连接失败，总耗时 {elapsed.TotalSeconds:F1}s"
            : $"{describeTrigger(trigger)}：{candidateCount} 个候选 IP 全部 TCP 连接失败，总耗时 {elapsed.TotalSeconds:F1}s。原因：{detail}";
    }

    private static string buildHttpFailureSummary(SpeedTestTrigger trigger, int candidateCount, int tcpSuccessCount, TimeSpan elapsed, IEnumerable<TcpProbeResult> finalists)
    {
        string detail = string.Join("；", finalists
                                         .Select(result => result.FailureReason)
                                         .Where(message => !string.IsNullOrWhiteSpace(message))
                                         .Distinct()
                                         .Take(failure_detail_limit)!);

        return string.IsNullOrWhiteSpace(detail)
            ? $"{describeTrigger(trigger)}：{candidateCount} 个候选 IP 中 {tcpSuccessCount} 个 TCP 可达，但 HTTP 复测全部失败，总耗时 {elapsed.TotalSeconds:F1}s"
            : $"{describeTrigger(trigger)}：{candidateCount} 个候选 IP 中 {tcpSuccessCount} 个 TCP 可达，但 HTTP 复测全部失败，总耗时 {elapsed.TotalSeconds:F1}s。TCP 失败细节：{detail}";
    }

    private static string buildSuccessSummary(SpeedTestTrigger trigger, int candidateCount, int tcpSuccessCount, int httpSuccessCount, TimeSpan elapsed, HttpProbeResult winner)
        => $"{describeTrigger(trigger)}：{candidateCount} 个候选中 {tcpSuccessCount} 个 TCP 可达，{httpSuccessCount} 个 HTTP 复测成功，已选 {winner.Ip}（{winner.Candidate.Cidr}，TCP {winner.TcpLatency.TotalMilliseconds:F0} ms，HTTP {winner.HttpLatency.TotalMilliseconds:F0} ms，状态 {(int)winner.StatusCode}），总耗时 {elapsed.TotalSeconds:F1}s";

    private static string describeTrigger(SpeedTestTrigger trigger)
        => trigger switch
        {
            SpeedTestTrigger.Startup => "启动自动测速",
            SpeedTestTrigger.DownloadFailure => "下载失败后自动测速",
            _ => "手动测速"
        };

    private static string describeProbeError(Exception error)
    {
        string message = error switch
        {
            OperationCanceledException => "timeout",
            SocketException socketException => $"socket {socketException.SocketErrorCode}: {socketException.Message}",
            _ => $"{error.GetType().Name}: {error.Message}"
        };

        BeatmapAccelLogging.Log($"Speed test probe error: {message}");
        return message;
    }

    private sealed record ProbeCandidate(string Cidr, string Ip, bool IsCurrent)
    {
        public string? LastTcpFailure { get; set; }
    }

    private sealed class TcpProbeResult
    {
        public ProbeCandidate Candidate { get; }

        public bool Success { get; }

        public TimeSpan Latency { get; }

        public string? FailureReason { get; }

        public TcpProbeResult(ProbeCandidate candidate, bool success, TimeSpan latency, string? failureReason)
        {
            Candidate = candidate;
            Success = success;
            Latency = latency;
            FailureReason = failureReason;

            if (!Success && !string.IsNullOrWhiteSpace(FailureReason))
                Candidate.LastTcpFailure = FailureReason;
        }
    }

    private sealed record HttpProbeResult(ProbeCandidate Candidate, TimeSpan TcpLatency, TimeSpan HttpLatency, HttpStatusCode StatusCode, string? FailureReason)
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
