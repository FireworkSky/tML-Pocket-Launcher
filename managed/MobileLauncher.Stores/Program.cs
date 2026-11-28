using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MobileLauncher.Stores;

if (args.Length != 3) return 2;
var root = Path.GetFullPath(args[0]);
var secret = args[1];
var endpoint = args[2];
Directory.CreateDirectory(root);
var steam = new SteamSession();
var gog = new GogSession();
var gate = new SemaphoreSlim(1);
CancellationTokenSource? download = null;
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start(8);
File.WriteAllText(endpoint, ((IPEndPoint)listener.LocalEndpoint).Port.ToString());
while (true)
{
    var socket = await listener.AcceptTcpClientAsync();
    _ = HandleAsync(socket);
}

async Task HandleAsync(TcpClient socket)
{
    using (socket)
    {
        socket.ReceiveTimeout = 30000;
        using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 8192, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, true) { AutoFlush = true };
        var entered = false;
        try
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (line == null || line.Length > 100_000) return;
            var request = JsonNode.Parse(line)!.AsObject();
            var supplied = request["secret"]?.GetValue<string>() ?? "";
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(supplied))) return;
            var method = request["method"]!.GetValue<string>();
            var data = request["params"]?.AsObject() ?? [];
            if (method == "cancel") { download?.Cancel(); await ReplyAsync(true); return; }
            // Ownership checks use a generation-checked Steam session and can run during a transfer.
            if (method != "steam.verify")
            {
                await gate.WaitAsync();
                entered = true;
            }
            object? result;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(method == "steam.probe" ? 120 : 90));
            var ct = timeout.Token;
            switch (method)
            {
                case "tml.resources": result = TmlResources.Read(data["assembly"]!.GetValue<string>()); break;
                case "status": result = new { steam = steam.Snapshot(true), gog = gog.Snapshot(true) }; break;
                case "steam.login": steam.Begin(data); result = steam.Snapshot(); break;
                case "steam.guard": steam.SubmitCode(data["code"]!.GetValue<string>()); result = steam.Snapshot(); break;
                case "steam.reconnect": steam.Reconnect(); result = steam.Snapshot(); break;
                case "steam.resume": steam.Resume(); result = steam.Snapshot(); break;
                case "steam.probe": result = await steam.ProbeAsync(ct); break;
                case "steam.verify": result = await steam.VerifyOwnershipAsync(ct); break;
                case "steam.logout": steam.Logout(); result = true; break;
                case "workshop.query":
                    result = await steam.WithWorkshopConnectionAsync((connection, token) => WorkshopService.QueryAsync(
                        connection.Client,
                        new WorkshopQuery(
                            data["appId"]!.GetValue<uint>(),
                            data["sort"]?.GetValue<string>() ?? "popular",
                            data["page"]?.GetValue<uint>() ?? 1,
                            data["pageSize"]?.GetValue<uint>() ?? 20,
                            data["search"]?.GetValue<string>(),
                            data["category"]?.GetValue<string>(),
                            data["version"]?.GetValue<string>(),
                            data["language"]?.GetValue<string>(),
                            data["days"]?.GetValue<uint>() ?? 90),
                        token), ct);
                    break;
                case "workshop.details":
                    result = await steam.WithWorkshopConnectionAsync((connection, token) => WorkshopService.DetailsAsync(
                        connection.Client,
                        data["appId"]!.GetValue<uint>(),
                        ulong.Parse(data["id"]!.GetValue<string>()),
                        token), ct);
                    break;
                case "gog.auth": result = new { url = gog.AuthorizationUrl(data["state"]!.GetValue<string>()) }; break;
                case "gog.login": await gog.LoginAsync(data["code"]!.GetValue<string>(), false, ct); result = gog.Snapshot(true); break;
                case "gog.restore": await gog.LoginAsync(data["refreshToken"]!.GetValue<string>(), true, ct); result = gog.Snapshot(true); break;
                case "gog.logout": gog.Logout(); result = true; break;
                case "gog.installers": result = await gog.InstallersAsync(ct); break;
                case "steam.download":
                case "workshop.download":
                case "gog.download":
                case "gog.import":
                    using (var task = new CancellationTokenSource(TimeSpan.FromHours(2)))
                    {
                        download = task;
                        var last = Environment.TickCount64;
                        void Progress(string text, int percent)
                        {
                            if (Environment.TickCount64 - last < 150 && percent != 100) return;
                            last = Environment.TickCount64;
                            writer.WriteLine(new JsonObject { ["event"] = "progress", ["message"] = text, ["progress"] = percent }.ToJsonString());
                        }
                        try
                        {
                            if (method == "workshop.download")
                            {
                                result = await DownloadWorkshopAsync(data, Progress, task.Token);
                            }
                            else
                            {
                                var archive = method == "steam.download" ? await steam.DownloadAsync(root, Progress, task.Token, data["installedContent"]?.GetValue<string>())
                                    : method == "gog.import" ? await GogSession.ImportAsync(root, data["file"]!.GetValue<string>(), Progress, task.Token)
                                    : await gog.DownloadAsync(data["id"]!.ToString(), root, Progress, task.Token);
                                result = new { archive };
                            }
                        }
                        finally { download = null; }
                    }
                    break;
                default: throw new ArgumentException("未知商店操作");
            }
            await ReplyAsync(result);
        }
        catch (Exception error)
        {
            var message = error switch
            {
                OperationCanceledException => "操作已取消或超时",
                IOException or InvalidDataException or InvalidOperationException or ArgumentException => error.Message,
                _ => "商店请求失败，请检查网络或重新登录"
            };
            // Never log token endpoint URLs, passwords, response bodies or credentials.
            try { await writer.WriteLineAsync(new JsonObject { ["ok"] = false, ["error"] = message }.ToJsonString()); }
            catch (IOException) { }
        }
        finally { if (entered) gate.Release(); }

        async Task ReplyAsync(object? result) => await writer.WriteLineAsync(new JsonObject
        {
            ["ok"] = true,
            ["data"] = System.Text.Json.JsonSerializer.SerializeToNode(result)
        }.ToJsonString());
    }
}

async Task<JsonObject> DownloadWorkshopAsync(JsonObject data, Action<string, int> progress, CancellationToken ct)
{
    return await steam.WithWorkshopConnectionAsync((connection, token) => WorkshopService.DownloadAsync(
        connection.Client,
        data["appId"]!.GetValue<uint>(),
        ulong.Parse(data["id"]!.GetValue<string>()),
        root,
        progress,
        token), ct);
}
