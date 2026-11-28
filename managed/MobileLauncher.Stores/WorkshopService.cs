using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SteamKit2;
using SteamKit2.CDN;
using SteamKit2.Internal;

namespace MobileLauncher.Stores;

internal sealed record WorkshopQuery(uint AppId, string Sort, uint Page, uint PageSize, string? Search = null, string? Category = null, string? Version = null, string? Language = null, uint Days = 90);

internal static class WorkshopService
{
    private const long MaximumFileSize = 1024L * 1024 * 1024;
    private static readonly HashSet<uint> SupportedApps = [105600, 1281930];
    private static readonly Dictionary<string, uint> SortTypes = new(StringComparer.Ordinal)
    {
        ["popular"] = 3,
        ["highestRated"] = 0,
        ["newest"] = 1,
        ["updated"] = 19,
        ["subscriptions"] = 12,
        ["playtimeTrend"] = 13,
        ["averagePlaytimeTrend"] = 15,
        ["totalPlaytime"] = 14,
        ["averagePlaytime"] = 16,
        ["sessionsTrend"] = 17,
        ["totalSessions"] = 18,
    };

    private static readonly HashSet<uint> SupportedDayRanges = [0, 1, 7, 30, 90, 180, 365];

    internal static CPublishedFile_QueryFiles_Request BuildQueryRequest(WorkshopQuery query)
    {
        if (!SupportedApps.Contains(query.AppId)) throw new ArgumentOutOfRangeException(nameof(query.AppId), "不支持的 Steam 工坊应用");
        if (!SortTypes.TryGetValue(query.Sort, out var queryType)) throw new ArgumentException("不支持的工坊排序方式", nameof(query.Sort));
        if (query.Page is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(query.Page));
        if (query.PageSize is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(query.PageSize));
        if (!SupportedDayRanges.Contains(query.Days)) throw new ArgumentOutOfRangeException(nameof(query.Days));
        if (query.Search?.Length > 256) throw new ArgumentException("搜索文字过长", nameof(query.Search));
        if (query.Category?.Length > 128) throw new ArgumentException("分类标签过长", nameof(query.Category));
        if (query.Version is not null && (query.AppId != 1281930 || query.Version is not ("1.3" or "1.4.3" or "1.4.4" or "1.4.4-Transitive")))
            throw new ArgumentException("不支持的 tModLoader 版本标签", nameof(query.Version));
        if (query.Language is not null && (query.AppId != 1281930 || !ModLanguages.Contains(query.Language)))
            throw new ArgumentException("不支持的 tModLoader 语言标签", nameof(query.Language));

        var request = new CPublishedFile_QueryFiles_Request
        {
            query_type = queryType,
            days = query.Days,
            page = query.Page,
            numperpage = query.PageSize,
            creator_appid = query.AppId,
            appid = query.AppId,
            filetype = 0,
            return_vote_data = true,
            return_tags = true,
            return_short_description = true,
            search_text = query.Search?.Trim() ?? ""
        };
        if (!string.IsNullOrWhiteSpace(query.Category)) request.requiredtags.Add(query.Category.Trim());
        if (query.Version is not null) request.requiredtags.Add(query.Version);
        if (query.Language is not null) request.requiredtags.Add(query.Language);
        return request;
    }

    private static readonly HashSet<string> ModLanguages = new(StringComparer.Ordinal)
    {
        "English", "German", "Italian", "French", "Spanish", "Russian", "Chinese", "Portuguese", "Polish"
    };

    internal static async Task<JsonObject> QueryAsync(SteamClient client, WorkshopQuery query, CancellationToken ct)
    {
        var unified = client.GetHandler<SteamUnifiedMessages>() ?? throw new IOException("Steam Workshop 服务不可用");
        var service = unified.CreateService<PublishedFile>();
        var response = await service.QueryFiles(BuildQueryRequest(query)).ToTask().WaitAsync(ct);
        if (response.Result != EResult.OK) throw new IOException($"Steam 工坊查询失败：{response.Result}");

        var items = new JsonArray();
        foreach (var item in response.Body.publishedfiledetails)
            items.Add(MapItem(query.AppId, item));
        return new JsonObject
        {
            ["appId"] = query.AppId,
            ["items"] = items,
            ["total"] = response.Body.total,
            ["nextCursor"] = string.IsNullOrEmpty(response.Body.next_cursor) ? "*" : response.Body.next_cursor
        };
    }

    internal static async Task<JsonObject> DetailsAsync(SteamClient client, uint appId, ulong publishedFileId, CancellationToken ct) =>
        MapItem(appId, await GetDetailsAsync(client, appId, publishedFileId, ct));

    private static async Task<PublishedFileDetails> GetDetailsAsync(SteamClient client, uint appId, ulong publishedFileId, CancellationToken ct)
    {
        if (!SupportedApps.Contains(appId)) throw new ArgumentOutOfRangeException(nameof(appId), "不支持的 Steam 工坊应用");
        if (publishedFileId == 0) throw new ArgumentOutOfRangeException(nameof(publishedFileId));
        var unified = client.GetHandler<SteamUnifiedMessages>() ?? throw new IOException("Steam Workshop 服务不可用");
        var service = unified.CreateService<PublishedFile>();
        var request = new CPublishedFile_GetDetails_Request
        {
            appid = appId,
            includechildren = true,
            includekvtags = true,
            includemetadata = true,
            includevotes = true,
            short_description = true
        };
        request.publishedfileids.Add(publishedFileId);
        var response = await service.GetDetails(request).ToTask().WaitAsync(ct);
        if (response.Result != EResult.OK) throw new IOException($"Steam 工坊详情读取失败：{response.Result}");
        var item = response.Body.publishedfiledetails.SingleOrDefault()
            ?? throw new IOException("Steam 没有返回该工坊项目");
        if ((EResult)item.result != EResult.OK)
            throw new IOException($"Steam 工坊项目不可用：{(EResult)item.result}");
        if (item.consumer_appid != appId || item.publishedfileid != publishedFileId)
            throw new InvalidDataException("工坊项目应用编号与请求不一致");
        return item;
    }

    internal static async Task<JsonObject> DownloadAsync(
        SteamClient client,
        uint appId,
        ulong publishedFileId,
        string root,
        Action<string, int> progress,
        CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetFullPath(root), "workshop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "content.bin");
        try
        {
            progress("正在读取工坊项目详情…", -1);
            var detail = await GetDetailsAsync(client, appId, publishedFileId, ct);
            if (!string.IsNullOrWhiteSpace(detail.file_url))
            {
                progress("正在下载工坊文件…", -1);
                if (detail.file_size > (ulong)MaximumFileSize)
                    throw new InvalidDataException("工坊文件超过 1 GB");
                await DownloadFileAsync(detail.file_url, file, (long)detail.file_size, progress, ct);
            }
            else if (detail.hcontent_file != 0)
            {
                progress("正在获取 Steam 工坊下载清单…", -1);
                await DownloadManifestContentAsync(client, appId, detail.hcontent_file, file, progress, ct);
            }
            else
                throw new IOException("Steam 工坊项目没有可下载的内容文件");
            progress("下载完成，正在交给安装器校验…", 95);
            return new JsonObject
            {
                ["appId"] = appId,
                ["id"] = publishedFileId.ToString(),
                ["directory"] = directory,
                ["file"] = file,
                ["fileName"] = detail.filename ?? ""
            };
        }
        catch (HttpRequestException error)
        {
            Directory.Delete(directory, true);
            var status = error.StatusCode is null ? "网络连接失败" : $"HTTP {(int)error.StatusCode.Value}";
            throw new IOException($"工坊文件下载失败：{status}。请检查网络后重试。", error);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    private static async Task DownloadManifestContentAsync(
        SteamClient client, uint appId, ulong manifestId, string destination,
        Action<string, int> progress, CancellationToken ct)
    {
        var apps = client.GetHandler<SteamApps>()!;
        var access = await apps.PICSGetAccessTokens(appId, null).ToTask().WaitAsync(ct);
        var product = await apps.PICSGetProductInfo(
            new SteamApps.PICSRequest(appId, access.AppTokens.GetValueOrDefault(appId)), null).ToTask().WaitAsync(ct);
        var info = product.Results?.SelectMany(result => result.Apps).FirstOrDefault(pair => pair.Key == appId).Value;
        var depot = info?.KeyValues["depots"]["workshopdepot"].AsUnsignedInteger() ?? 0;
        if (depot == 0) depot = appId;

        var content = client.GetHandler<SteamContent>()!;
        var key = await apps.GetDepotDecryptionKey(depot, appId).ToTask().WaitAsync(ct);
        if (key.Result != EResult.OK) throw new IOException($"Steam 工坊资源库访问失败：{key.Result}");
        var code = await content.GetManifestRequestCode(depot, appId, manifestId).WaitAsync(ct);
        if (code == 0) throw new IOException("Steam 未授予工坊清单访问权限");
        var servers = await SteamCdnPool.CreateAsync(client, appId, ct);
        using var cdn = new Client(client);
        var manifest = await servers.FetchAsync(depot,
            (server, auth) => cdn.DownloadManifestAsync(depot, manifestId, code, server, key.DepotKey, cdnAuthToken: auth), ct);
        var files = manifest.Files?.Where(file => !file.Flags.HasFlag(EDepotFileFlag.Directory)).ToArray()
            ?? throw new InvalidDataException("Steam 工坊清单没有文件");
        if (files.Length is 0 or > 20_000) throw new InvalidDataException("Steam 工坊清单文件数量无效");

        ulong total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = WorkshopArchivePath(file.FileName);
            if (!paths.Add(path) || !string.IsNullOrEmpty(file.LinkTarget))
                throw new InvalidDataException("Steam 工坊清单存在重复路径或链接");
            if (file.TotalSize > (ulong)MaximumFileSize - total)
                throw new InvalidDataException("工坊内容超过 1 GB");
            total += file.TotalSize;
        }
        if (total == 0) throw new InvalidDataException("Steam 工坊清单没有内容");

        ulong downloaded = 0;
        using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            await using var output = archive.CreateEntry(WorkshopArchivePath(file.FileName), CompressionLevel.NoCompression).Open();
            ulong offset = 0;
            foreach (var chunk in file.Chunks.OrderBy(chunk => chunk.Offset))
            {
                ct.ThrowIfCancellationRequested();
                if (chunk.Offset != offset || chunk.UncompressedLength is 0 or > 16 * 1024 * 1024 ||
                    chunk.UncompressedLength > file.TotalSize - offset)
                    throw new InvalidDataException("工坊文件分块范围无效");
                var bytes = new byte[chunk.UncompressedLength];
                var count = await servers.FetchAsync(depot,
                    (server, auth) => cdn.DownloadDepotChunkAsync(depot, chunk, server, bytes, key.DepotKey, cdnAuthToken: auth), ct);
                if (count != bytes.Length) throw new InvalidDataException("工坊文件分块下载不完整");
                await output.WriteAsync(bytes, ct);
                hash.AppendData(bytes);
                offset += (uint)count;
                downloaded += (uint)count;
                progress($"正在下载工坊内容 · {downloaded / 1048576}/{total / 1048576} MB", (int)(downloaded * 90 / total));
            }
            if (offset != file.TotalSize || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), file.FileHash))
                throw new InvalidDataException("工坊文件 SHA-1 校验失败");
        }
    }

    internal static string WorkshopArchivePath(string path)
    {
        var relative = path.Replace('\\', '/');
        if (relative.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':') || part.Any(char.IsControl)))
            throw new InvalidDataException("Steam 工坊清单包含无效路径");
        return relative;
    }

    private static async Task DownloadFileAsync(
        string address,
        string destination,
        long expectedSize,
        Action<string, int> progress,
        CancellationToken ct)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(20) };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            throw new InvalidDataException("Steam 返回了无效的工坊文件地址");
        if (expectedSize < 0 || expectedSize > MaximumFileSize)
            throw new InvalidDataException("工坊文件超过 1 GB");
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            if (!IsSteamContentUri(uri)) throw new InvalidDataException("Steam 返回了不受支持的 UGC 文件地址");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location ?? throw new IOException("Steam 文件重定向地址缺失");
                uri = new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length is > MaximumFileSize || (expectedSize > 0 && length.HasValue && length != expectedSize))
                throw new InvalidDataException("工坊文件长度无效");
            var declaredSize = expectedSize > 0 ? expectedSize : length;
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                var buffer = new byte[64 * 1024];
                long received = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var count = await input.ReadAsync(buffer, ct);
                    if (count == 0) break;
                    received = checked(received + count);
                    if (received > MaximumFileSize || received > declaredSize)
                        throw new InvalidDataException("工坊文件超过允许的长度");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress($"正在下载工坊内容 · {received / 1048576} MB",
                        declaredSize > 0 ? (int)(received * 90 / declaredSize.Value) : -1);
                }
                await output.FlushAsync(ct);
                if (received == 0 || (declaredSize.HasValue && received != declaredSize))
                    throw new InvalidDataException("工坊文件下载不完整");
            }
            return;
        }
        throw new IOException("Steam 文件重定向次数过多");
    }

    private static bool IsSteamContentUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.Host == "steamusercontent.com" || uri.Host.EndsWith(".steamusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host == "steamcontent.com" || uri.Host.EndsWith(".steamcontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host is "steamuserimages-a.akamaihd.net" or "steamusercontent-a.akamaihd.net" or "steamcontent-a.akamaihd.net" or "steamcdn-a.akamaihd.net");

    internal static JsonObject MapItem(uint appId, PublishedFileDetails item)
    {
        var tags = new JsonArray();
        foreach (var tag in item.tags ?? [])
            if (!string.IsNullOrWhiteSpace(tag.tag)) tags.Add(tag.tag);
        var kvTags = new JsonObject();
        foreach (var tag in item.kvtags ?? [])
            if (!string.IsNullOrWhiteSpace(tag.key)) kvTags[tag.key] = tag.value;
        var dependencyIds = new JsonArray();
        if (appId == 1281930)
            foreach (var id in item.children.Select(child => child.publishedfileid)
                .Where(id => id != 0 && id != item.publishedfileid).Distinct().Take(100))
                dependencyIds.Add(id.ToString());
        return new JsonObject
        {
            ["appId"] = appId,
            ["id"] = item.publishedfileid.ToString(),
            ["title"] = item.title ?? "",
            ["description"] = string.IsNullOrWhiteSpace(item.short_description) ? item.file_description ?? "" : item.short_description,
            ["creator"] = item.creator.ToString(),
            ["previewUrl"] = item.preview_url ?? "",
            ["fileName"] = item.filename ?? "",
            ["fileSize"] = item.file_size,
            ["contentHandle"] = item.hcontent_file.ToString(),
            ["subscriptions"] = item.subscriptions,
            ["views"] = item.views,
            ["votesUp"] = item.vote_data?.votes_up ?? 0,
            ["votesDown"] = item.vote_data?.votes_down ?? 0,
            ["timeCreated"] = item.time_created,
            ["timeUpdated"] = item.time_updated,
            ["tags"] = tags,
            ["keyValues"] = kvTags,
            ["dependencyIds"] = dependencyIds
        };
    }
}
