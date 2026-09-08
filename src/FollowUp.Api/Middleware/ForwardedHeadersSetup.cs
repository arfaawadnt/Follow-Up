using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using AspNetIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace FollowUp.Api.Middleware;

/// <summary>
/// Configures forwarded-header handling so that, when the app runs behind a reverse proxy, the real
/// client IP (X-Forwarded-For) and scheme (X-Forwarded-Proto) are honoured. Without this the per-IP
/// login/e-sign rate limiters and the IP audit trail all see the proxy's address — every client
/// collapses into one rate-limit partition and the audit log records the proxy, not the caller
/// (finding IAM-006).
///
/// Trusting a forwarded IP is only safe from a known proxy: an arbitrary client could otherwise spoof
/// X-Forwarded-For and defeat the rate limiter. The default (no configuration) keeps the framework's
/// loopback-only trust, so a directly-exposed deployment is unchanged. An operator behind a proxy
/// declares it under the "ForwardedHeaders" section, which then becomes the entire trusted set:
///   "ForwardedHeaders": { "KnownProxies": ["10.0.0.5"], "KnownNetworks": ["10.0.0.0/24"], "ForwardLimit": 1 }
/// </summary>
public static class ForwardedHeadersSetup
{
    public static void Configure(ForwardedHeadersOptions options, IConfiguration config)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        var section = config.GetSection("ForwardedHeaders");
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? Array.Empty<string>();
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? Array.Empty<string>();

        if (proxies.Length > 0 || networks.Length > 0)
        {
            // An explicit trust set was declared — replace the loopback-only defaults with exactly it,
            // so no address outside the operator's declared proxies/networks is ever trusted.
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();
            foreach (var p in proxies)
                if (IPAddress.TryParse(p, out var ip)) options.KnownProxies.Add(ip);
            foreach (var n in networks)
                if (TryParseNetwork(n, out var net)) options.KnownNetworks.Add(net);
        }

        if (int.TryParse(section["ForwardLimit"], out var limit) && limit > 0)
            options.ForwardLimit = limit;
    }

    private static bool TryParseNetwork(string cidr, out AspNetIPNetwork network)
    {
        network = default!;
        var slash = cidr.IndexOf('/');
        if (slash <= 0) return false;
        if (!IPAddress.TryParse(cidr[..slash], out var prefix)) return false;
        if (!int.TryParse(cidr[(slash + 1)..], out var bits) || bits < 0) return false;
        network = new AspNetIPNetwork(prefix, bits);
        return true;
    }
}
