using System.Text.RegularExpressions;

namespace Dustweave.Ota;

static class MirrorNginx
{
    public static string BundlePatch(string original, Config c)
    {
        Rules.Need(c.Layout == 3 && c.Public == "/home/nginx/dustweave-updates-aio", "Unexpected bundled web mount");
        string url = Mirror.BaseUrl(c);
        const string aioBegin = "# BEGIN DUSTWEAVE AIO", aioEnd = "# END DUSTWEAVE AIO";
        int anchor = original.IndexOf(aioBegin, StringComparison.Ordinal), after;
        if (anchor >= 0)
        {
            int end = original.IndexOf(aioEnd, anchor, StringComparison.Ordinal);
            Rules.Need(end > anchor && original.IndexOf(aioBegin, anchor + aioBegin.Length, StringComparison.Ordinal) < 0 &&
                original.IndexOf(aioEnd, end + aioEnd.Length, StringComparison.Ordinal) < 0, "Ambiguous bundled web configuration");
            after = end + aioEnd.Length;
        }
        else
        {
            anchor = original.IndexOf(Begin, StringComparison.Ordinal);
            Rules.Need(anchor >= 0 && original.IndexOf(Begin, anchor + Begin.Length, StringComparison.Ordinal) < 0 &&
                !original.Contains("/updates/dustweave/all-in-one/",StringComparison.Ordinal),"Inspect existing bundled web configuration before installation");
            after = anchor;
        }
        string block = $$"""
        # BEGIN DUSTWEAVE AIO
        location = /updates/dustweave/all-in-one/releases.json {
            alias /usr/share/nginx/dustweave-updates-aio/releases.json;
            add_header Cache-Control "no-store" always;
            add_header X-Content-Type-Options "nosniff" always;
            limit_except GET { deny all; }
        }
        location = /updates/dustweave/all-in-one/releases-v2.json {
            alias /usr/share/nginx/dustweave-updates-aio/releases-v2.json;
            add_header Cache-Control "no-store" always;
            add_header X-Content-Type-Options "nosniff" always;
            limit_except GET { deny all; }
        }
        location = /updates/dustweave/all-in-one/releases-1.6.0.json {
            alias /usr/share/nginx/dustweave-updates-aio/.mirror/manifests/releases-1.6.0.json;
            add_header Cache-Control "no-store" always;
            add_header X-Content-Type-Options "nosniff" always;
            limit_except GET { deny all; }
        }
        location ^~ /updates/dustweave/all-in-one/.mirror/ { return 404; }
        location ^~ /updates/dustweave/all-in-one/ {
            # Never serve patch bodies from this server, even during CDN outages.
            # Only immutable, fully verified per-file routes can redirect.
            set $dw_aio_relative "";
            if ($uri ~ "^/updates/dustweave/all-in-one/(v[0-9A-Za-z.-]+/Dustweave-[0-9A-Za-z.-]+-(?:Lite|Portable)-from-[0-9A-Za-z.-]+-win-x64[.]delta[.]zip)$") { set $dw_aio_relative $1; }
            if ($dw_aio_relative = "") { return 404; }
            set $dw_aio_cloud_relative $dw_aio_relative;
            if ($dw_aio_relative ~ "^v1[.]5[.]17/(.*)$") { set $dw_aio_cloud_relative v1.5.17-transition/$1; }
            # Old clients' ?origin=1 retries also stay on object storage.
            if (-f /usr/share/nginx/dustweave-updates-aio/.mirror/routes/$dw_aio_relative) {
                add_header Cache-Control "no-store" always;
                add_header X-Dustweave-Delivery "qicaiyun" always;
                return 302 {{url}}/$dw_aio_cloud_relative;
            }
            add_header Cache-Control "no-store" always;
            add_header X-Dustweave-Delivery "unavailable" always;
            add_header Retry-After "60" always;
            return 503;
        }
        # END DUSTWEAVE AIO

        """;
        return original[..anchor] + block.TrimEnd() + (after == anchor ? "\n\n" : "") + original[after..];
    }
    const string Begin = "# BEGIN DUSTWEAVE OTA CDN";
    const string End = "# END DUSTWEAVE OTA CDN";
    public static string Patch(string original, Config c)
    {
        string baseUrl = Mirror.BaseUrl(c);
        Rules.Need(c.Public == "/home/nginx/dustweave-updates-v2", "Unexpected production web mount");
        string block = $$"""
        {{Begin}}
        location = /updates/dustweave/v2/updates-v3.json {
            alias /usr/share/nginx/dustweave-updates-v2/updates-v3.json;
            add_header Cache-Control "no-store" always;
            add_header X-Content-Type-Options "nosniff" always;
            limit_except GET { deny all; }
        }
        location ^~ /updates/dustweave/v2/.mirror/ { return 404; }
        location ^~ /updates/dustweave/v2/ {
            alias /usr/share/nginx/dustweave-updates-v2/;
            autoindex off;
            set $dw_relative "";
            set $dw_mirror "";
            if ($uri ~ "^/updates/dustweave/v2/(v[0-9A-Za-z.-]+/Dustweave-[0-9A-Za-z.-]+-win-x64[.]delta[.]zip)$") {
                set $dw_relative $1;
            }
            if ($uri ~ "^/updates/dustweave/v2/(repair/objects/[0-9a-f]{64}[.]bin)$") {
                set $dw_relative $1;
            }
            if (-f /usr/share/nginx/dustweave-updates-v2/.mirror/enabled) {
                set $dw_mirror "1";
            }
            if (-f /usr/share/nginx/dustweave-updates-v2/.mirror/routes/$dw_relative) {
                set $dw_mirror "${dw_mirror}1";
            }
            if ($arg_origin = "1") { set $dw_mirror ""; }
            if ($dw_mirror = "11") {
                add_header Cache-Control "no-store" always;
                add_header X-Dustweave-Delivery "qicaiyun" always;
                add_header X-Content-Type-Options "nosniff" always;
                return 302 {{baseUrl}}/$dw_relative;
            }
            add_header Cache-Control "public, max-age=31536000, immutable";
            add_header X-Dustweave-Delivery "origin" always;
            add_header X-Content-Type-Options "nosniff" always;
            limit_rate 2m;
            limit_except GET { deny all; }
        }
        {{End}}
""";
        block = block.TrimStart();
        int begin = original.IndexOf(Begin, StringComparison.Ordinal);
        if (begin >= 0)
        {
            int end = original.IndexOf(End, begin, StringComparison.Ordinal);
            Rules.Need(end > begin && original.IndexOf(Begin, begin + Begin.Length, StringComparison.Ordinal) < 0, "Ambiguous mirror configuration");
            return original[..begin] + block + original[(end + End.Length)..];
        }
        var matches = Regex.Matches(original, @"location\s+\^~\s+/updates/dustweave/v2/\s*\{");
        Rules.Need(matches.Count == 1, "Expected exactly one existing layout-2 download location");
        int start = matches[0].Index, brace = original.IndexOf('{', start), depth = 1, at = brace + 1;
        // The old static alias contains no nested string braces; reject drift
        // rather than guessing at an unrelated or customized server section.
        for (; at < original.Length && depth > 0; at++)
        {
            if (original[at] == '{') depth++;
            else if (original[at] == '}') depth--;
        }
        Rules.Need(depth == 0, "Unterminated existing download location");
        string old = original[start..at];
        Rules.Need(old.Contains("alias /usr/share/nginx/dustweave-updates-v2/;", StringComparison.Ordinal) &&
            !old.Contains("proxy_pass", StringComparison.Ordinal) && !old.Contains("rewrite", StringComparison.Ordinal), "Existing download configuration requires inspection");
        return original[..start] + block + original[at..];
    }
}
