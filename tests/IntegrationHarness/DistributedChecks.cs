using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Users;
using StrmAssistant;
using StrmAssistant.Options;
using StrmAssistant.Web.Api;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace FilmIndex.IntegrationTests;

internal static class DistributedChecks
{
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object value) =>
        client.PostAsync(path, new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json"));

    public static async Task Run(string root, BaseItem item, IUserManager users, IMediaProbeManager probe,
        PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = new Uri("http://127.0.0.1:18766/emby/") };
        var admin = await users.CreateUser("film-index-worker-fixture", new UserPolicy
            { IsAdministrator = true, EnableAllFolders = true, EnableAllDevices = true });
        try
        {
            var password = Guid.NewGuid().ToString("N");
            await users.ChangePassword(admin, password);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Emby-Authorization",
                "MediaBrowser Client=\"FilmIndexFixture\", Device=\"Fixture\", DeviceId=\"worker-fixture\", Version=\"1\"");
            using var auth = await Post(client, "Users/AuthenticateByName", new { Username = admin.Name, Pw = password });
            auth.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await auth.Content.ReadAsStringAsync());
            var token = payload.RootElement.GetProperty("AccessToken").GetString();
            client.DefaultRequestHeaders.Remove("X-Emby-Authorization");
            var request = new ProbeWorker { RequestId = Guid.NewGuid(), SourcePath = item.Path, ExpectedSize = new FileInfo(item.Path).Length };
            using (var anonymous = await Post(client, "FilmIndex/Worker/Probe", request))
                check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Worker endpoint rejects anonymous probing");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Emby-Token", token);
            options.DistributedOptions.ServeWorker = false; save(options);
            using (var disabled = await Post(client, "FilmIndex/Worker/Probe", request))
                check(!disabled.IsSuccessStatusCode, "Worker endpoint rejects requests while worker mode is disabled");
            options.DistributedOptions.ServeWorker = true;
            options.DistributedOptions.AllowedRoots = Path.Combine(root, "movies"); save(options);
            using (var response = await Post(client, "FilmIndex/Worker/Probe", request))
            {
                response.EnsureSuccessStatusCode();
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                check(Guid.Parse(result.RootElement.GetProperty("RequestId").GetString()!) == request.RequestId &&
                    result.RootElement.GetProperty("SourceSize").GetInt64() == request.ExpectedSize,
                    "Worker probes a real video and returns matching file identity");
            }
            request.ExpectedSize++;
            using (var mismatch = await Post(client, "FilmIndex/Worker/Probe", request))
                check(!mismatch.IsSuccessStatusCode, "Worker refuses a source whose size differs from the master");
            request.SourcePath = Path.Combine(root, "music", "track.mp3"); request.ExpectedSize = null;
            using (var outside = await Post(client, "FilmIndex/Worker/Probe", request))
                check(!outside.IsSuccessStatusCode, "Worker refuses files outside configured roots");
            options.DistributedOptions.UseWorker = true;
            options.DistributedOptions.WorkerUrl = "http://127.0.0.1:18766";
            options.DistributedOptions.WorkerApiKey = token; save(options);
            var assembly = typeof(Plugin).Assembly;
            var remote = assembly.GetType("StrmAssistant.Common.RemoteProbeClient")!;
            var bridge = assembly.GetType("StrmAssistant.Common.RemoteProbeBridge")!;
            var info = await (Task<MediaInfo>)remote.GetMethod("TryGet")!.Invoke(null, new object[] { item, item.Path, CancellationToken.None })!;
            check(info?.MediaStreams.Any(stream => stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Video) == true,
                "Master accepts verified worker data serialized by the real Emby API");
            var before = (long)bridge.GetProperty("CacheHits")!.GetValue(null)!;
            using ((IDisposable)bridge.GetMethod("Enter")!.Invoke(null, new object[] { info!, item.Path })!)
            {
                var result = await probe.GetMediaInfo(new MediaInfoRequest { MediaType = DlnaProfileType.Video,
                    MediaSource = new MediaSourceInfo { Path = item.Path, Protocol = MediaProtocol.File } }, CancellationToken.None);
                check(ReferenceEquals(info, result) && (long)bridge.GetProperty("CacheHits")!.GetValue(null)! > before,
                    "Native media probe consumes worker results in the matching execution scope");
            }
            options.DistributedOptions.WorkerUrl = "http://127.0.0.1:1"; save(options);
            var fallback = await (Task<MediaInfo>)remote.GetMethod("TryGet")!.Invoke(null, new object[] { item, item.Path, CancellationToken.None })!;
            var local = await probe.GetMediaInfo(new MediaInfoRequest { MediaType = DlnaProfileType.Video,
                MediaSource = new MediaSourceInfo { Path = item.Path, Protocol = MediaProtocol.File } }, CancellationToken.None);
            check(fallback == null && local.MediaStreams.Count > 0, "Unavailable worker falls back without leaving remote data in the local probe scope");
            var logPath = Path.Combine(Plugin.Instance.ApplicationPaths.LogDirectoryPath, "film-index-log-fixture.txt");
            await File.WriteAllTextAsync(logPath, "2026-10-21 10:00:01 Info First\n2026-10-21 10:00:02 Error Second\n  stack line A\n  stack line B\n2026-10-21 10:00:03 Info Third\n");
            options.ExperienceEnhanceOptions.NewestLogsFirst = true; save(options);
            using (var response = await client.GetAsync("System/Logs/film-index-log-fixture.txt/Lines?StartIndex=0&Limit=2&Sanitize=true"))
            {
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var entries = document.RootElement.GetProperty("Items");
                check(entries.GetArrayLength() == 2 && entries[0].GetString()!.Contains("Third") && entries[1].GetString()!.IndexOf("stack line A") < entries[1].GetString()!.IndexOf("stack line B"),
                    "Authenticated native log API returns newest entries first and preserves stack trace order");
                check(document.RootElement.GetProperty("TotalRecordCount").GetInt32() == 3, "Newest-first log API reports the full logical entry count");
            }
            using (var raw = await client.GetAsync("System/Logs/film-index-log-fixture.txt?Sanitize=true"))
            {
                raw.EnsureSuccessStatusCode(); var text = await raw.Content.ReadAsStringAsync();
                check(text.IndexOf("First") < text.IndexOf("Third"), "Downloading the original log retains chronological order");
            }
            options.ExperienceEnhanceOptions.NewestLogsFirst = false; save(options);
        }
        finally
        {
            options.DistributedOptions.UseWorker = false; options.DistributedOptions.ServeWorker = false;
            options.DistributedOptions.WorkerApiKey = string.Empty; save(options);
            await users.DeleteUser(admin);
        }
    }
}

