using System.Collections.Concurrent;
using System.Net;
using SteamKit2;
using SteamKit2.CDN;

namespace MobileLauncher.Stores;

internal sealed class SteamCdnPool(SteamContent content, uint appId, Server[] servers)
{
    private readonly ConcurrentDictionary<(uint Depot, string Host), string> tokens = new();
    private int cursor = -1;

    internal static async Task<SteamCdnPool> CreateAsync(SteamClient client, uint appId, CancellationToken ct)
    {
        var content = client.GetHandler<SteamContent>()!;
        var candidates = await content.GetServersForSteamPipe(client.CellID, 20).WaitAsync(ct);
        var servers = candidates.Where(s => s.Type is "CDN" or "SteamCache" && !s.UseAsProxy && !string.IsNullOrEmpty(s.Host))
            .Where(s => s.AllowedAppIds is not { Length: > 0 } || s.AllowedAppIds.Contains(appId))
            .OrderBy(s => s.WeightedLoad).DistinctBy(s => s.Host).Take(8).ToArray();
        if (servers.Length == 0) throw new IOException("Steam 未返回可用内容服务器");
        return new SteamCdnPool(content, appId, servers);
    }

    internal async Task<T> FetchAsync<T>(uint depot, Func<Server, string?, Task<T>> download, CancellationToken ct)
    {
        uint start = unchecked((uint)Interlocked.Increment(ref cursor));
        Exception? last = null;
        for (uint attempt = 0; attempt < servers.Length; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var server = servers[unchecked(start + attempt) % (uint)servers.Length];
            var key = (Depot: depot, Host: server.Host!);
            tokens.TryGetValue(key, out var auth);
            try
            {
                try { return await download(server, auth).WaitAsync(ct); }
                catch (SteamKitWebRequestException error) when (error.StatusCode == HttpStatusCode.Forbidden)
                {
                    var ticket = await content.GetCDNAuthToken(appId, depot, key.Host)
                        .WaitAsync(TimeSpan.FromSeconds(20), ct);
                    if (ticket.Result != EResult.OK) throw new IOException("Steam CDN 授权失败");
                    tokens[key] = ticket.Token;
                    return await download(server, ticket.Token).WaitAsync(ct);
                }
            }
            catch (Exception error) when (!ct.IsCancellationRequested) { last = error; }
        }
        throw new IOException("Steam 内容服务器下载失败，请重试", last);
    }
}
