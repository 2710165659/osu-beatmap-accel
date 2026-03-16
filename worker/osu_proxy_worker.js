/**
 * Cloudflare Worker: osu beatmap download proxy
 *
 * Route example:
 *   https://<your-worker-domain>/beatmapsets/123456/download
 *
 * Upstream:
 *   https://osu.ppy.sh/api/v2/beatmapsets/{sid}/download
 *
 * Notes:
 *   - this worker is meant to proxy lazer's direct download traffic
 *   - the caller should provide an Authorization header from lazer
 */

const OSU_ORIGIN = "https://osu.ppy.sh";
const DOWNLOAD_PATH = /^\/beatmapsets\/(\d+)\/download\/?$/;
const ERROR_BODY_LIMIT = 2048;
const DEFAULT_BROWSER_UA =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET,HEAD,OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type,Authorization,Range,Cookie",
  "Access-Control-Expose-Headers":
    "Content-Length,Content-Disposition,Content-Range,Accept-Ranges",
};

export default {
  async fetch(request) {
    const url = new URL(request.url);

    if (url.pathname === "/healthz") {
      return jsonResponse(
        {
          ok: true,
          service: "osu-beatmap-proxy-worker",
          now: new Date().toISOString(),
        },
        200
      );
    }

    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: CORS_HEADERS });
    }

    if (request.method !== "GET" && request.method !== "HEAD") {
      return jsonResponse(
        {
          error: "method_not_allowed",
          message: "Only GET/HEAD/OPTIONS are supported.",
        },
        405
      );
    }

    const match = url.pathname.match(DOWNLOAD_PATH);
    if (!match) {
      return jsonResponse(
        {
          error: "not_found",
          message:
            "Use /beatmapsets/{sid}/download, e.g. /beatmapsets/75/download",
        },
        404
      );
    }

    const sid = match[1];
    const upstreamUrl = new URL(`${OSU_ORIGIN}/api/v2/beatmapsets/${sid}/download`);
    upstreamUrl.search = url.search;

    const upstreamHeaders = new Headers();
    forwardHeaderIfPresent(request.headers, upstreamHeaders, "Range");
    forwardHeaderIfPresent(request.headers, upstreamHeaders, "Authorization");

    const userAgent = request.headers.get("User-Agent") || DEFAULT_BROWSER_UA;
    upstreamHeaders.set("User-Agent", userAgent);
    upstreamHeaders.set("Accept", request.headers.get("Accept") || "*/*");

    if (!upstreamHeaders.get("Authorization")) {
      return jsonResponse(
        {
          error: "missing_authorization",
          message: "Authorization header is required to proxy lazer direct downloads.",
        },
        401
      );
    }

    let upstreamResponse;
    try {
      upstreamResponse = await fetch(upstreamUrl.toString(), {
        method: request.method,
        headers: upstreamHeaders,
        redirect: "follow",
        cf: {
          cacheEverything: false,
          cacheTtl: 0,
        },
      });
    } catch (error) {
      return jsonResponse(
        {
          error: "upstream_fetch_failed",
          reason: "Worker failed to reach osu upstream.",
          upstream_url: upstreamUrl.toString(),
          message: String(error),
        },
        502
      );
    }

    if (!upstreamResponse.ok) {
      const excerpt = await readBodyExcerpt(upstreamResponse, ERROR_BODY_LIMIT);
      return jsonResponse(
        {
          error: "upstream_http_error",
          message: "Upstream returned a non-2xx status.",
          upstream_status: upstreamResponse.status,
          upstream_status_text: upstreamResponse.statusText,
          upstream_url: upstreamResponse.url || upstreamUrl.toString(),
          upstream_content_type: upstreamResponse.headers.get("Content-Type") || "",
          upstream_body_excerpt: excerpt,
        },
        upstreamResponse.status
      );
    }

    const contentType = upstreamResponse.headers.get("Content-Type") || "";
    const contentDisposition = upstreamResponse.headers.get("Content-Disposition") || "";
    const isHtml = /^text\/html\b/i.test(contentType);
    const isAttachment = /attachment/i.test(contentDisposition);

    if (request.method === "GET" && isHtml && !isAttachment) {
      const excerpt = await readBodyExcerpt(upstreamResponse, ERROR_BODY_LIMIT);
      return jsonResponse(
        {
          error: "unexpected_html_response",
          message:
            "Upstream returned HTML instead of beatmap file. Direct API auth may be missing or invalid.",
          upstream_status: upstreamResponse.status,
          upstream_status_text: upstreamResponse.statusText,
          upstream_url: upstreamResponse.url || upstreamUrl.toString(),
          upstream_content_type: contentType,
          upstream_body_excerpt: excerpt,
        },
        502
      );
    }

    const responseHeaders = new Headers(upstreamResponse.headers);
    responseHeaders.set("Cache-Control", "no-store");
    responseHeaders.set("X-Proxy-Upstream-Status", String(upstreamResponse.status));
    if (upstreamResponse.url) {
      responseHeaders.set("X-Proxy-Upstream-Url", upstreamResponse.url);
    }
    for (const [k, v] of Object.entries(CORS_HEADERS)) {
      responseHeaders.set(k, v);
    }

    return new Response(upstreamResponse.body, {
      status: upstreamResponse.status,
      statusText: upstreamResponse.statusText,
      headers: responseHeaders,
    });
  },
};

function forwardHeaderIfPresent(input, output, headerName) {
  const value = input.get(headerName);
  if (value) {
    output.set(headerName, value);
  }
}

function jsonResponse(payload, status) {
  const headers = new Headers({
    "Content-Type": "application/json; charset=utf-8",
    ...CORS_HEADERS,
  });
  return new Response(JSON.stringify(payload), { status, headers });
}

async function readBodyExcerpt(response, limit) {
  try {
    const text = await response.text();
    return text.slice(0, limit);
  } catch {
    return "";
  }
}
