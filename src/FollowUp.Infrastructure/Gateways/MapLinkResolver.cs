using FollowUp.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Gateways;

/// <summary>
/// Resolves a Google-Maps short link to its redirect target with an SSRF guard (SRS FR-21): the input host
/// and the resolved host must both be on the allow-list, auto-redirects are disabled (the Location header is
/// read once), and the request times out at 5 seconds. Returns null when the target is not allow-listed.
/// </summary>
public sealed class MapLinkResolver : IMapLinkResolver
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "goo.gl", "maps.app.goo.gl", "www.google.com", "google.com", "maps.google.com",
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MapLinkResolver> _logger;

    public MapLinkResolver(IHttpClientFactory httpFactory, ILogger<MapLinkResolver> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<string?> ResolveAsync(string shortUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(shortUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !AllowedHosts.Contains(uri.Host))
        {
            _logger.LogWarning("Map resolve refused for non-allow-listed input host {Host}", uri?.Host);
            return null;
        }

        // No auto-redirect: read the Location header once and validate its host.
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await client.SendAsync(request, ct);

        var location = response.Headers.Location;
        if (location is null) return null;

        var target = location.IsAbsoluteUri ? location : new Uri(uri, location);
        if (!AllowedHosts.Contains(target.Host) &&
            !target.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Map resolve refused for non-allow-listed target host {Host}", target.Host);
            return null;
        }
        return target.ToString();
    }
}
