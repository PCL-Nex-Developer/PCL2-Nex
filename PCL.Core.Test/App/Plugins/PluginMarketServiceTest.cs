using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class PluginMarketServiceTest
{
    [TestMethod]
    public async Task SearchTopicAsync_ShouldPaginateAndSendGitHubHeaders()
    {
        var requests = new List<HttpRequestMessage>();
        using var handler = new StubHandler(request =>
        {
            requests.Add(CloneHeaders(request));
            var url = request.RequestUri!.OriginalString;
            if (url.Contains("/search/repositories", StringComparison.Ordinal))
            {
                var page = url.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
                return Json("{\"total_count\":2,\"items\":[{\"id\":" + page
                    + ",\"name\":\"plugin" + page + "\",\"full_name\":\"Owner/plugin" + page
                    + "\",\"html_url\":\"https://github.com/Owner/plugin" + page
                    + "\",\"default_branch\":\"main\",\"archived\":false,\"disabled\":false,\"fork\":false,\"owner\":{\"login\":\"Owner\",\"avatar_url\":\"https://avatars.githubusercontent.com/u/1?v=4\"}}]}");
            }
            var repositoryName = request.RequestUri.AbsolutePath.Split('/')[3];
            return Json(ValidManifestJson(repositoryName));
        });
        using var client = new HttpClient(handler);
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                PerPage = 1,
                MaxPages = 3,
                GitHubToken = "token-value",
                GitHubMirror = 0,
                CacheDirectory = cache,
                Architecture = Architecture.X64
            }, client);

            Assert.AreEqual(2, result.Entries.Count);
            Assert.IsTrue(requests.Count(request => request.RequestUri!.Host == "api.github.com") >= 4);
            Assert.IsTrue(requests.Any(request => request.RequestUri!.Query.Contains("q=topic%3Apclnexplugin", StringComparison.Ordinal)));
            Assert.IsTrue(requests.Any(request => request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal)));
            Assert.IsTrue(requests.All(request => request.Headers.Contains("User-Agent")));
            Assert.IsTrue(requests.All(request => request.Headers.Contains("X-GitHub-Api-Version")));
            Assert.IsTrue(requests.All(request => request.Headers.Authorization?.Scheme == "Bearer"));
            Assert.IsTrue(result.Entries.All(entry => entry.Logo == "https://avatars.githubusercontent.com/u/1?v=4"));
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldUseRepositoryAndManifestCacheAfterNetworkFailure()
    {
        var cache = NewTempDirectory();
        try
        {
            using (var successClient = new HttpClient(new StubHandler(request =>
                       request.RequestUri!.AbsolutePath.Contains("search/repositories", StringComparison.Ordinal)
                           ? Json("""{"total_count":1,"items":[{"id":1,"name":"plugin","full_name":"Owner/plugin","html_url":"https://github.com/Owner/plugin","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}}]}""")
                           : Json(ValidManifestJson("plugin")))))
            {
                var first = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
                {
                    GitHubMirror = 0,
                    CacheDirectory = cache,
                    Architecture = Architecture.X64
                }, successClient);
                Assert.AreEqual(1, first.Entries.Count);
            }

            using var failedClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            var fallback = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache,
                Architecture = Architecture.X64
            }, failedClient);

            Assert.IsTrue(fallback.UsedRepositoryCache);
            Assert.AreEqual(1, fallback.Entries.Count);
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldKeepRepositoryCachesIsolatedByTopic()
    {
        var cache = NewTempDirectory();
        try
        {
            using (var successClient = new HttpClient(new StubHandler(request =>
                       request.RequestUri!.AbsolutePath.Contains("search/repositories", StringComparison.Ordinal)
                           ? Json("""{"total_count":1,"items":[{"id":1,"name":"plugin","full_name":"Owner/plugin","html_url":"https://github.com/Owner/plugin","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}}]}""")
                           : Json(ValidManifestJson("plugin")))))
            {
                var first = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
                {
                    Topic = "topic-one",
                    GitHubMirror = 0,
                    CacheDirectory = cache,
                    Architecture = Architecture.X64
                }, successClient);
                Assert.AreEqual(1, first.Entries.Count);
            }

            using var failedClient = new HttpClient(new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            var second = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                Topic = "topic-two",
                GitHubMirror = 0,
                CacheDirectory = cache,
                Architecture = Architecture.X64
            }, failedClient);

            Assert.IsFalse(second.UsedRepositoryCache);
            Assert.AreEqual(0, second.Entries.Count);
            Assert.IsTrue(second.Errors.Any(error => error.Repository == "GitHub"));
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldLoadManifestCommitTimeAndReleaseDownloads()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath + request.RequestUri.Query);
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("search/repositories", StringComparison.Ordinal))
                return Json("""{"total_count":1,"items":[{"id":1,"name":"plugin","full_name":"Owner/plugin","html_url":"https://github.com/Owner/plugin","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}}]}""");
            if (path.EndsWith("/commits", StringComparison.Ordinal))
                return Json("""[{"commit":{"committer":{"date":"2026-07-19T12:34:56Z"}}}]""");
            if (path.EndsWith("/releases", StringComparison.Ordinal))
                return Json("""[{"assets":[{"download_count":12},{"download_count":30}]},{"assets":[{"download_count":8}]}]""");
            return Json(ValidManifestJson("plugin"));
        }));
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache,
                Architecture = Architecture.X64
            }, client);

            Assert.AreEqual(1, result.Entries.Count);
            Assert.AreEqual(DateTimeOffset.Parse("2026-07-19T12:34:56Z"), result.Entries[0].LastUpdatedAt);
            Assert.AreEqual(50L, result.Entries[0].DownloadCount);
            Assert.IsTrue(requests.Any(url => url.Contains("/commits?path=manifest.json", StringComparison.Ordinal)));
            Assert.IsTrue(requests.Any(url => url.Contains("/releases?per_page=100", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldReportHeaderless429AsRateLimited()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache
            }, client);
            Assert.IsTrue(result.RateLimited);
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldFallbackFromAcceleratorToOriginalWithHeadersAndQuery()
    {
        var requests = new List<HttpRequestMessage>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(CloneHeaders(request));
            return request.RequestUri!.Host == "gh-proxy.org"
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : Json("""{"total_count":0,"items":[]}""");
        }));
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 1,
                GitHubToken = "fallback-token",
                CacheDirectory = cache
            }, client);

            Assert.AreEqual(0, result.Entries.Count);
            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual("gh-proxy.org", requests[0].RequestUri!.Host);
            Assert.AreEqual("api.github.com", requests[1].RequestUri!.Host);
            Assert.AreEqual(requests[0].RequestUri!.Query, requests[1].RequestUri!.Query);
            Assert.IsTrue(requests.All(request => request.Headers.Authorization?.Parameter == "fallback-token"));
            Assert.IsTrue(requests.All(request => request.Headers.Contains("X-GitHub-Api-Version")));
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public void SelectDownload_ShouldHonorArchitectureAndAnyCpuFallback()
    {
        var version = new PluginMarketVersion
        {
            Downloads = new PluginMarketDownloads
            {
                Amd64 = new PluginMarketDownload { PackageUrl = "https://github.com/Owner/plugin/releases/download/v1.0.0/amd64.pclx", Sha256 = ValidSha256 },
                Arm64 = new PluginMarketDownload { PackageUrl = "https://github.com/Owner/plugin/releases/download/v1.0.0/arm64.pclx", Sha256 = ValidSha256 },
                AnyCpu = new PluginMarketDownload { PackageUrl = "https://github.com/Owner/plugin/releases/download/v1.0.0/anycpu.pclx", Sha256 = ValidSha256 }
            }
        };

        Assert.AreEqual("https://github.com/Owner/plugin/releases/download/v1.0.0/amd64.pclx", PluginRepositoryService.SelectDownload(version, Architecture.X64)!.PackageUrl);
        Assert.AreEqual("https://github.com/Owner/plugin/releases/download/v1.0.0/arm64.pclx", PluginRepositoryService.SelectDownload(version, Architecture.Arm64)!.PackageUrl);
        version.Downloads.Arm64 = null;
        Assert.AreEqual("https://github.com/Owner/plugin/releases/download/v1.0.0/anycpu.pclx", PluginRepositoryService.SelectDownload(version, Architecture.Arm64)!.PackageUrl);
        version.Downloads.AnyCpu = null;
        Assert.IsNull(PluginRepositoryService.SelectDownload(version, Architecture.Arm64));
    }

    [TestMethod]
    public void SelectLatestVersion_ShouldUseSemanticVersionPrecedence()
    {
        var manifest = new PluginMarketManifest
        {
            Versions =
            [
                new PluginMarketVersion { Version = "1.0.0-beta.9" },
                new PluginMarketVersion { Version = "1.0.0" },
                new PluginMarketVersion { Version = "1.0.0-beta.10" }
            ]
        };

        Assert.AreEqual("1.0.0", PluginRepositoryService.SelectLatestVersion(manifest)!.Version);
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldRespectArchivedDisabledAndForkOptions()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("search/repositories", StringComparison.Ordinal))
                return Json("""{"total_count":3,"items":[{"id":1,"name":"archived","full_name":"Owner/archived","html_url":"https://github.com/Owner/archived","default_branch":"main","archived":true,"disabled":false,"fork":false,"owner":{"login":"Owner"}},{"id":2,"name":"disabled","full_name":"Owner/disabled","html_url":"https://github.com/Owner/disabled","default_branch":"main","archived":false,"disabled":true,"fork":false,"owner":{"login":"Owner"}},{"id":3,"name":"fork","full_name":"Owner/fork","html_url":"https://github.com/Owner/fork","default_branch":"main","archived":false,"disabled":false,"fork":true,"owner":{"login":"Owner"}}]}""");
            var repositoryName = request.RequestUri.AbsolutePath.Split('/')[3];
            return Json(ValidManifestJson(repositoryName));
        }));
        var cache = NewTempDirectory();
        try
        {
            var hidden = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache
            }, client);
            Assert.AreEqual(0, hidden.Entries.Count);

            var shown = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache,
                IncludeArchived = true,
                IncludeDisabled = true,
                IncludeForks = true
            }, client);
            Assert.AreEqual(3, shown.Entries.Count);
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldIsolateOversizedManifest()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("search/repositories", StringComparison.Ordinal))
                return Json("""{"total_count":2,"items":[{"id":1,"name":"bad","full_name":"Owner/bad","html_url":"https://github.com/Owner/bad","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}},{"id":2,"name":"good","full_name":"Owner/good","html_url":"https://github.com/Owner/good","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}}]}""");
            if (request.RequestUri.AbsolutePath.Contains("/bad/", StringComparison.Ordinal))
                return Json(new string('x', 1024));
            return Json(ValidManifestJson("good"));
        }));
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache,
                MaxManifestBytes = 512
            }, client);
            Assert.AreEqual(1, result.Entries.Count);
            Assert.AreEqual(1, result.Errors.Count);
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public void DeveloperTrust_ShouldRequireOfficialLevelAndIgnoreLoginCase()
    {
        var allowlist = new PluginDeveloperAllowlist
        {
            Developers =
            [
                new PluginDeveloperRecord { GitHubLogin = "OfficialUser", Level = "official" },
                new PluginDeveloperRecord { GitHubLogin = "NotOfficial", Level = "community" }
            ]
        };

        Assert.AreEqual(PluginDeveloperTrustLevel.Official,
            PluginDeveloperTrustService.GetTrustLevel("officialuser", allowlist, []));
        Assert.AreEqual(PluginDeveloperTrustLevel.Local,
            PluginDeveloperTrustService.GetTrustLevel("LocalUser", allowlist, ["localuser"]));
        Assert.AreEqual(PluginDeveloperTrustLevel.Other,
            PluginDeveloperTrustService.GetTrustLevel("NotOfficial", allowlist, []));
    }

    [TestMethod]
    public void DeveloperVisibility_ShouldRespectShowNonWhitelistedSetting()
    {
        var entries = new[]
        {
            new PluginRepositoryEntry { Id = "example.official", DeveloperTrustLevel = PluginDeveloperTrustLevel.Official },
            new PluginRepositoryEntry { Id = "example.local", DeveloperTrustLevel = PluginDeveloperTrustLevel.Local },
            new PluginRepositoryEntry { Id = "example.other", DeveloperTrustLevel = PluginDeveloperTrustLevel.Other }
        };

        Assert.AreEqual(2, PluginDeveloperTrustService.FilterVisible(entries, false).Count);
        Assert.AreEqual(3, PluginDeveloperTrustService.FilterVisible(entries, true).Count);
    }

    [TestMethod]
    public async Task DeveloperWhitelistFetch_ShouldUseStableJsonEndpointAndWriteCache()
    {
        HttpRequestMessage? capturedRequest = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = CloneHeaders(request);
            return Json("""{"version":1,"updatedAt":null,"developers":[{"githubLogin":"OfficialUser","displayName":"Official User","level":"official"}]}""");
        }));
        var cacheDirectory = NewTempDirectory();
        var cachePath = Path.Combine(cacheDirectory, "developers.json");
        try
        {
            var result = await PluginDeveloperTrustService.FetchOfficialAsync(client, cachePath);

            Assert.AreEqual("cdn.jsdelivr.net", capturedRequest!.RequestUri!.Host);
            Assert.AreEqual("/gh/PCL-Nex-Developer/Nex_Server@main/apiv2/developers.json", capturedRequest.RequestUri.AbsolutePath);
            Assert.IsTrue(capturedRequest.Headers.Accept.Any(value => value.MediaType == "application/json"));
            Assert.AreEqual(1, result.Developers.Count);
            Assert.IsTrue(File.Exists(cachePath));
        }
        finally { Directory.Delete(cacheDirectory, true); }
    }

    [TestMethod]
    public async Task DeveloperWhitelistFetch_ShouldUseCacheThenEmptyFallback()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var cacheDirectory = NewTempDirectory();
        var cachePath = Path.Combine(cacheDirectory, "developers.json");
        try
        {
            File.WriteAllText(cachePath,
                """{"version":1,"updatedAt":null,"developers":[{"githubLogin":"CachedUser","displayName":"Cached User","level":"official"}]}""");
            var cached = await PluginDeveloperTrustService.FetchOfficialAsync(client, cachePath);
            Assert.AreEqual("CachedUser", cached.Developers.Single().GitHubLogin);

            File.Delete(cachePath);
            var empty = await PluginDeveloperTrustService.FetchOfficialAsync(client, cachePath);
            Assert.AreEqual(0, empty.Developers.Count);
        }
        finally { Directory.Delete(cacheDirectory, true); }
    }

    [TestMethod]
    public void GetInstallSources_ShouldNotFallbackToManifestWhenPlatformIsIncompatible()
    {
        var entry = new PluginRepositoryEntry
        {
            ManifestUrl = "https://raw.githubusercontent.com/Owner/plugin/refs/heads/main/manifest.json",
            SelectedVersion = new PluginMarketVersion { Version = "1.0.0" },
            SelectedDownload = null
        };

        Assert.AreEqual(0, PluginRepositoryService.GetInstallSources(entry).Count());
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldRetainVersionForPlatformIncompatibleEntry()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("search/repositories", StringComparison.Ordinal))
                return Json("""{"total_count":1,"items":[{"id":1,"name":"plugin","full_name":"Owner/plugin","html_url":"https://github.com/Owner/plugin","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}}]}""");

            return Json(ValidManifestJson("plugin")
                .Replace("\"anycpu\"", "\"arm64\"", StringComparison.Ordinal)
                .Replace("plugin.pclx", "plugin-arm64.pclx", StringComparison.Ordinal));
        }));
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache,
                Architecture = Architecture.X64
            }, client);

            var entry = result.Entries.Single();
            Assert.AreEqual("1.0.0", entry.SelectedVersion?.Version);
            Assert.IsNull(entry.SelectedDownload);
            Assert.AreEqual(PluginCoreCompatibilityStatus.Compatible, entry.CompatibilityStatus);
            Assert.AreEqual(0, PluginRepositoryService.GetInstallSources(entry).Count());
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public void SelectLatestVersion_ShouldSkipNewerVersionWithoutCurrentPlatformPackage()
    {
        var manifest = new PluginMarketManifest
        {
            Versions =
            [
                new PluginMarketVersion
                {
                    Version = "2.0.0",
                    Downloads = new PluginMarketDownloads
                    {
                        Arm64 = new PluginMarketDownload
                        {
                            PackageUrl = "https://github.com/Owner/plugin/releases/download/v2.0.0/plugin-arm64.pclx",
                            Sha256 = ValidSha256
                        }
                    }
                },
                new PluginMarketVersion
                {
                    Version = "1.5.0",
                    Downloads = new PluginMarketDownloads
                    {
                        Amd64 = new PluginMarketDownload
                        {
                            PackageUrl = "https://github.com/Owner/plugin/releases/download/v1.5.0/plugin-amd64.pclx",
                            Sha256 = ValidSha256
                        }
                    }
                }
            ]
        };

        Assert.AreEqual("1.5.0", PluginRepositoryService.SelectLatestVersion(manifest, Architecture.X64)!.Version);
    }

    [TestMethod]
    public void SelectLatestVersion_ShouldPreferHighestInstallableCoreVersion()
    {
        var manifest = new PluginMarketManifest
        {
            Versions =
            [
                new PluginMarketVersion
                {
                    Version = "2.0.0",
                    PclCoreVersion = "2026.06.1",
                    Downloads = new PluginMarketDownloads
                    {
                        AnyCpu = new PluginMarketDownload
                        {
                            PackageUrl = "https://github.com/Owner/plugin/releases/download/v2.0.0/plugin.pclx",
                            Sha256 = ValidSha256
                        }
                    }
                },
                new PluginMarketVersion
                {
                    Version = "1.5.0",
                    PclCoreVersion = "2026.07.1",
                    Downloads = new PluginMarketDownloads
                    {
                        AnyCpu = new PluginMarketDownload
                        {
                            PackageUrl = "https://github.com/Owner/plugin/releases/download/v1.5.0/plugin.pclx",
                            Sha256 = ValidSha256
                        }
                    }
                }
            ]
        };

        Assert.AreEqual("1.5.0", PluginRepositoryService.SelectLatestVersion(manifest, Architecture.X64)!.Version);
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldIsolateMalformedManifestAndKeepRepositoryOwnerIdentity()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("search/repositories", StringComparison.Ordinal))
                return Json("""{"total_count":2,"items":[{"id":1,"name":"bad","full_name":"Owner/bad","html_url":"https://github.com/Owner/bad","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}},{"id":2,"name":"good","full_name":"Owner/good","html_url":"https://github.com/Owner/good","default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"Owner"}}]}""");
            if (request.RequestUri.AbsolutePath.Contains("/bad/", StringComparison.Ordinal))
                return Json(ValidManifestJson("bad").Replace("\"downloads\":", "\"packageUrl\":\"https://github.com/Owner/bad/releases/download/v1.0.0/legacy.pclx\",\"downloads\":"));
            return Json(ValidManifestJson("good", authorLogin: "owner"));
        }));
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache
            }, client);

            Assert.AreEqual(1, result.Entries.Count);
            Assert.AreEqual("Owner", result.Entries[0].GitHubLogin);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains(result.Errors[0].Message, "only under downloads");
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task SearchTopicAsync_ShouldNormalizeLegacyRepositoryVersionIndex()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.Contains("search/repositories", StringComparison.Ordinal))
                return Json("""{"total_count":1,"items":[{"id":1,"name":"PCL2-EasytierPlugin","full_name":"PCL-Nex-Developer/PCL2-EasytierPlugin","html_url":"https://github.com/PCL-Nex-Developer/PCL2-EasytierPlugin","description":"EasyTier plugin","topics":["pclnexplugin","networking"],"default_branch":"main","archived":false,"disabled":false,"fork":false,"owner":{"login":"PCL-Nex-Developer","avatar_url":"https://avatars.githubusercontent.com/u/1?v=4"}}]}""");
            if (request.RequestUri.AbsolutePath.EndsWith("/contents/plugin.json", StringComparison.Ordinal))
                return Json("""{"id":"pclnex.easytier","name":"EasyTier 联机大厅","author":"Nex(XueLing)","description":"实现 EasyTier 联机大厅功能。"}""");
            return Json("""
            {
              "versions": [
                {
                  "version": "1.0.7",
                  "packageUrl": "https://github.com/PCL-Nex-Developer/PCL2-EasytierPlugin/releases/download/v1.0.7/pclnex.easytier-v1.0.7.pclx",
                  "sha256": "6722ff163339a1003ab495c4c733110ee3376995d2e88b68ccb59b536fe12ac9",
                  "minApiVersion": "1.2.1.0",
                  "minHostVersion": "3.0.0",
                  "releaseNotes": "https://github.com/PCL-Nex-Developer/PCL2-EasytierPlugin/releases/tag/v1.0.7"
                }
              ]
            }
            """);
        }));
        var cache = NewTempDirectory();
        try
        {
            var result = await PluginRepositoryService.SearchTopicAsync(new PluginMarketQueryOptions
            {
                GitHubMirror = 0,
                CacheDirectory = cache,
                Architecture = Architecture.X64
            }, client);

            Assert.AreEqual(0, result.Errors.Count);
            Assert.AreEqual(1, result.Entries.Count);
            var entry = result.Entries[0];
            Assert.AreEqual("pclnex.easytier", entry.Id);
            Assert.AreEqual("EasyTier 联机大厅", entry.Name);
            Assert.AreEqual("Nex(XueLing)", entry.Author);
            Assert.AreEqual("PCL-Nex-Developer", entry.GitHubLogin);
            Assert.AreEqual(PluginCoreCompatibilityStatus.Unknown, entry.CompatibilityStatus);
            Assert.AreEqual("https://github.com/PCL-Nex-Developer/PCL2-EasytierPlugin/releases/download/v1.0.7/pclnex.easytier-v1.0.7.pclx", entry.SelectedDownload!.PackageUrl);
            CollectionAssert.Contains(entry.Tags, "networking");
            CollectionAssert.DoesNotContain(entry.Tags, "pclnexplugin");
            Assert.IsTrue(requests.Any(path => path.EndsWith("/contents/plugin.json", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task MarketSourceJson_ShouldLoadInlinePluginsGroupsTagsAndRelativeLogo()
    {
        var directory = NewTempDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "market.json");
            File.WriteAllText(Path.Combine(directory, "logo.png"), "logo");
            File.WriteAllText(Path.Combine(directory, "README.md"), "# Inline plugin");
            var manifest = ValidManifestJson("inline")
                .Replace("\"versions\":", "\"logo\":\"logo.png\",\"readmeUrl\":\"README.md\",\"group\":\"Tools\",\"tags\":[\"utility\"],\"versions\":");
            File.WriteAllText(sourcePath,
                "{\"version\":1,\"name\":\"Custom\",\"tags\":[\"featured\"],\"topics\":[],\"manifests\":[],\"plugins\":[" + manifest + "]}");

            using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
            var result = await PluginMarketplaceService.LoadSourceAsync(sourcePath,
                new PluginMarketQueryOptions { Architecture = Architecture.X64 }, client);

            Assert.AreEqual(1, result.Entries.Count);
            var entry = result.Entries[0];
            Assert.AreEqual("Custom", entry.SourceGroup);
            Assert.AreEqual("Tools", entry.Group);
            CollectionAssert.AreEquivalent(new[] { "utility", "featured" }, entry.Tags);
            Assert.AreEqual(Path.Combine(directory, "logo.png"), entry.Logo);
            Assert.AreEqual(Path.Combine(directory, "README.md"), entry.ReadmeUrl);
            Assert.IsFalse(entry.ManifestUrlIsDirect);
            var installSource = PluginRepositoryService.GetInstallSources(entry).Single();
            var persistent = PluginRepositoryService.GetPersistentInstallSource(
                entry, installSource, PluginInstallSourceType.Repository, installSource.Url);
            Assert.AreEqual(PluginInstallSourceType.Git, persistent.Type);
            Assert.AreEqual("https://github.com/Owner/inline", persistent.Url);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task MarketSourceJson_ShouldNeverSendGitHubTokenToThirdPartySource()
    {
        var requests = new List<HttpRequestMessage>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(CloneHeaders(request));
            return Json("""{"version":1,"name":"Third Party","topics":[],"manifests":[],"plugins":[]}""");
        }));

        var result = await PluginMarketplaceService.LoadSourceAsync(
            "https://plugins.example.test/source.json",
            new PluginMarketQueryOptions { GitHubToken = "must-not-leak", GitHubMirror = 0 },
            client);

        Assert.AreEqual(0, result.Entries.Count);
        Assert.AreEqual(1, requests.Count);
        Assert.IsNull(requests[0].Headers.Authorization);
        Assert.IsFalse(requests[0].Headers.Contains("X-GitHub-Api-Version"));
    }

    [TestMethod]
    public void DirectManifest_ShouldAllowNonGitRepositoryLogoTagsAndManifestUpdateSource()
    {
        var manifest = new PluginMarketManifest
        {
            Id = "example.direct",
            Name = "Direct",
            Description = "Direct manifest plugin",
            Author = new PluginMarketAuthor { DisplayName = "Direct Developer" },
            Repository = "https://plugins.example.test/direct",
            Logo = "https://cdn.example.test/direct.png",
            Group = "Utilities",
            Tags = ["utility", "server"],
            Versions =
            [
                new PluginMarketVersion
                {
                    Version = "1.0.0",
                    PclCoreVersion = "2026.07.1",
                    Downloads = new PluginMarketDownloads
                    {
                        AnyCpu = new PluginMarketDownload
                        {
                            PackageUrl = "https://cdn.example.test/direct-1.0.0.pclx",
                            Sha256 = ValidSha256
                        }
                    },
                    ReleaseNotes = "https://plugins.example.test/direct/releases/1.0.0"
                }
            ]
        };

        var entry = PluginRepositoryService.CreateManifestEntry(
            manifest, "https://plugins.example.test/direct/manifest.json", Architecture.X64);
        var installSource = PluginRepositoryService.GetInstallSources(entry).Single();
        var persistent = PluginRepositoryService.GetPersistentInstallSource(
            entry, installSource, PluginInstallSourceType.Repository, installSource.Url);

        Assert.AreEqual("https://cdn.example.test/direct.png", entry.Logo);
        Assert.AreEqual("plugins.example.test", entry.SourceGroup);
        Assert.AreEqual("Utilities", entry.Group);
        Assert.IsTrue(entry.ManifestUrlIsDirect);
        Assert.AreEqual(PluginInstallSourceType.Manifest, persistent.Type);
        Assert.AreEqual("https://plugins.example.test/direct/manifest.json", persistent.Url);
    }

    [TestMethod]
    public void DirectGitHubManifest_ShouldDeriveDeveloperAndLogoFromRepositoryOwner()
    {
        var manifest = new PluginMarketManifest
        {
            Id = "example.owner-avatar",
            Name = "Owner Avatar",
            Description = "Uses repository owner identity",
            Author = new PluginMarketAuthor { DisplayName = "Owner Organization" },
            Repository = "https://github.com/Owner/owner-avatar",
            Versions =
            [
                new PluginMarketVersion
                {
                    Version = "1.0.0",
                    PclCoreVersion = "2026.07.1",
                    Downloads = new PluginMarketDownloads
                    {
                        AnyCpu = new PluginMarketDownload
                        {
                            PackageUrl = "https://github.com/Owner/owner-avatar/releases/download/v1.0.0/plugin.pclx",
                            Sha256 = ValidSha256
                        }
                    },
                    ReleaseNotes = "https://github.com/Owner/owner-avatar/releases/tag/v1.0.0"
                }
            ]
        };

        var entry = PluginRepositoryService.CreateManifestEntry(
            manifest, "https://raw.githubusercontent.com/Owner/owner-avatar/main/manifest.json", Architecture.X64);

        Assert.AreEqual("Owner", entry.GitHubLogin);
        Assert.AreEqual("https://github.com/Owner.png?size=128", entry.Logo);
    }

    [TestMethod]
    public async Task FetchReadmeAsync_ShouldPreferInlineMarkdown()
    {
        var entry = new PluginRepositoryEntry
        {
            Id = "example.inline-readme",
            Name = "Inline README",
            Readme = "# Inline\n\nContent",
            SourceRepoUrl = "https://github.com/Owner/inline-readme"
        };
        using var client = new HttpClient(new StubHandler(_ =>
            throw new AssertFailedException("Inline README should not make a network request.")));

        var readme = await PluginRepositoryService.FetchReadmeAsync(entry, client);

        Assert.AreEqual("# Inline\n\nContent", readme);
    }

    [TestMethod]
    public async Task FetchReadmeAsync_ShouldLoadGitHubReadmeAndUseCacheFallback()
    {
        var cache = NewTempDirectory();
        var requests = new List<HttpRequestMessage>();
        var entry = new PluginRepositoryEntry
        {
            Id = "example.github-readme",
            Name = "GitHub README",
            SourceRepoUrl = "https://github.com/Owner/github-readme"
        };
        try
        {
            using (var client = new HttpClient(new StubHandler(request =>
                   {
                       requests.Add(CloneHeaders(request));
                       return new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent("# GitHub README", Encoding.UTF8, "text/markdown")
                       };
                   })))
            {
                var readme = await PluginRepositoryService.FetchReadmeAsync(entry, client, cache);
                Assert.AreEqual("# GitHub README", readme);
            }

            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual("/repos/Owner/github-readme/readme", requests[0].RequestUri!.AbsolutePath);
            Assert.IsTrue(requests[0].Headers.Accept.Any(value =>
                value.MediaType == "application/vnd.github.raw+json"));

            using var failedClient = new HttpClient(new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            var cached = await PluginRepositoryService.FetchReadmeAsync(entry, failedClient, cache);
            Assert.AreEqual("# GitHub README", cached);
        }
        finally { Directory.Delete(cache, true); }
    }

    [TestMethod]
    public async Task FetchReadmeAsync_ShouldNeverSendGitHubTokenToThirdPartyHost()
    {
        var requests = new List<HttpRequestMessage>();
        var cache = NewTempDirectory();
        try
        {
            var entry = new PluginRepositoryEntry
            {
                Id = "example.third-party-readme",
                Name = "Third Party README",
                ReadmeUrl = "https://docs.example.test/README.md"
            };
            using var client = new HttpClient(new StubHandler(request =>
            {
                requests.Add(CloneHeaders(request));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("# Safe", Encoding.UTF8, "text/markdown")
                };
            }));

            var readme = await PluginRepositoryService.FetchReadmeAsync(
                entry, client, cache, githubToken: "must-not-leak");

            Assert.AreEqual("# Safe", readme);
            Assert.AreEqual(1, requests.Count);
            Assert.IsNull(requests[0].Headers.Authorization);
            Assert.IsFalse(requests[0].Headers.Contains("X-GitHub-Api-Version"));
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    [TestMethod]
    public void GetVersionsNewestFirst_ShouldUseSemanticVersionOrdering()
    {
        var manifest = new PluginMarketManifest
        {
            Versions =
            [
                new PluginMarketVersion { Version = "1.9.0" },
                new PluginMarketVersion { Version = "1.10.0" },
                new PluginMarketVersion { Version = "1.10.0-beta.1" }
            ]
        };

        CollectionAssert.AreEqual(
            new[] { "1.10.0", "1.10.0-beta.1", "1.9.0" },
            PluginRepositoryService.GetVersionsNewestFirst(manifest).Select(version => version.Version).ToArray());
    }

    private const string ValidSha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static string ValidManifestJson(string repositoryName, string authorLogin = "Owner")
        => "{\"id\":\"example." + repositoryName + "\",\"name\":\"Example\",\"author\":{\"githubLogin\":\"" + authorLogin
           + "\",\"displayName\":\"Owner\"},\"description\":\"Test\",\"repository\":\"https://github.com/Owner/" + repositoryName
           + "\",\"versions\":[{\"version\":\"1.0.0\",\"pclCoreVersion\":\"2026.07.1\",\"downloads\":{\"anycpu\":{\"packageUrl\":\"https://github.com/Owner/"
           + repositoryName + "/releases/download/v1.0.0/plugin.pclx\",\"sha256\":\"" + ValidSha256
           + "\"}},\"releaseNotes\":\"https://github.com/Owner/" + repositoryName + "/releases/tag/v1.0.0\"}]}";

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpRequestMessage CloneHeaders(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pcl-market-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
