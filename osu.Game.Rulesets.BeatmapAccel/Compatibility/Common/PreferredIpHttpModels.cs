using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace osu.Game.Rulesets.BeatmapAccel.Compatibility;

internal sealed record BeatmapAccelHttpHeader(string Name, string Value);

internal sealed record PreferredIpHttpProbeRequest(
    IPAddress PreferredIp,
    string Host,
    string PathAndQuery,
    string UserAgent,
    TimeSpan ConnectTimeout);

internal readonly record struct PreferredIpHttpProbeResponse(HttpStatusCode StatusCode, TimeSpan Latency);

internal sealed record PreferredIpFileDownloadRequest(
    Uri RequestUri,
    IPAddress? PreferredIp,
    string DestinationPath,
    TimeSpan ConnectTimeout,
    IReadOnlyList<BeatmapAccelHttpHeader> Headers,
    Action<long, long?>? Progress = null,
    int MaxRedirects = 5);
