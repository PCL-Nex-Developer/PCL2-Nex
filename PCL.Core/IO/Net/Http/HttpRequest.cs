using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace PCL.Core.IO.Net.Http;

public static class HttpRequest
{
    private static Uri CreateUri(string url)
    {
        return new Uri(GitHubAccelerator.RewriteByConfig(url));
    }

    public static HttpRequestMessage Create(string url)
    {
        return new HttpRequestMessage(HttpMethod.Get, CreateUri(url));
    }
    public static HttpRequestMessage CreateHead(string url)
    {
        return new HttpRequestMessage(HttpMethod.Head, CreateUri(url));
    }
    public static HttpRequestMessage CreatePost(string url)
    {
        return new HttpRequestMessage(HttpMethod.Post, CreateUri(url));
    }
    public static HttpRequestMessage CreatePut(string url)
    {
        return new HttpRequestMessage(HttpMethod.Put, CreateUri(url));
    }
    public static HttpRequestMessage CreateDelete(string url)
    {
        return new HttpRequestMessage(HttpMethod.Delete, CreateUri(url));
    }

    public static async Task<string> GetStringAsync(string url)
    {
        using var resp = await Create(url).SendAsync().ConfigureAwait(false);
        return await resp.AsStringAsync().ConfigureAwait(false);
    }

    public static async Task<T?> GetJsonAsync<T>(string url)
    {
        using var resp = await Create(url).SendAsync().ConfigureAwait(false);
        return await resp.AsJsonAsync<T>().ConfigureAwait(false);
    }

    public static async Task<HttpResponseMessage> PostJsonAsync<T>(string url, T data, string? contentType = null)
    {
        return await CreatePost(url)
            .WithJsonContent(data, contentType)
            .SendAsync()
            .ConfigureAwait(false);
    }
}
