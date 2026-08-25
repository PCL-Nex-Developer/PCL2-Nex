using System.Collections.Generic;

namespace PCL.Core.IO.Net.Http;

/// <summary>
/// Compatibility helpers retained for plugin services. GitHub requests are no longer rewritten through third-party proxies.
/// </summary>
public static class GitHubAccelerator
{
    public static string Rewrite(string url, int mirror, IEnumerable<string>? enabledDomains = null) => url;

    public static bool ShouldRewrite(string url, IEnumerable<string>? enabledDomains = null) => false;

    public static IReadOnlyList<string> GetRequestCandidates(string url, int mirror) => [url];

    public static IReadOnlyList<string> GetRequestCandidatesByConfig(string url, int? mirror = null) => [url];

    public static IReadOnlyList<string> GetConfiguredDomains() => [];
}
