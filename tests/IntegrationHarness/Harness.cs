using MediaBrowser.Controller.Entities;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Users;
using StrmAssistant;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;
using StrmAssistant.Web.Service;
using System.Reflection;
using System.Text.Json;

namespace FilmIndex.IntegrationTests;

// This DLL is built only for isolated fixture servers. It is never part of an installation package.
public sealed class Harness : IServerEntryPoint
{
    private readonly ILibraryManager _library;
    private readonly IItemRepository _repository;
    private readonly IFileSystem _files;
    private readonly IUserManager _users;
    private readonly ILogger _logger;
    private readonly IProviderManager _providers;
    private readonly IUserDataManager _userdata;
    private readonly IHttpClient _http;
    private readonly MediaBrowser.Controller.MediaEncoding.IMediaProbeManager _probe;
    private readonly List<string> _checks = new();
    public Harness(ILibraryManager library, IItemRepository repository, IFileSystem files,
        IUserManager users, ILogManager logs, IProviderManager providers, IUserDataManager userdata, IHttpClient http,
        MediaBrowser.Controller.MediaEncoding.IMediaProbeManager probe)
    { _library = library; _repository = repository; _files = files; _users = users; _logger = logs.GetLogger("FilmIndexIntegration"); _providers = providers; _userdata = userdata; _http = http; _probe = probe; }

    public void Run()
    {
        var root = Environment.GetEnvironmentVariable("FILM_INDEX_TEST_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        root = Path.GetFullPath(root);
        if (!Path.GetFileName(root).StartsWith("film-index-integration-", StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(root, ".fixture-only"))) throw new InvalidOperationException("Test fixture marker missing");
        _ = Execute(root);
    }
    private void Check(bool value, string description)
    { if (!value) throw new InvalidOperationException(description); _checks.Add(description); _logger.Info("PASS: " + description); }
    public void Dispose() { }

    private async Task Execute(string root)
    {
        try
        {
            var options = Plugin.Instance.GetPluginOptions();
            options.MediaInfoExtractOptions.MediaInfoJsonRootFolder = Path.Combine(root, "sidecars");
            options.MediaInfoExtractOptions.LibraryScope = string.Empty;
            options.MediaInfoExtractOptions.ScanExternalAudioTracks = true;
            options.MediaInfoExtractOptions.PersistMediaInfoMode = "Default";
            SaveOptions(options);
            var musicPath = Path.Combine(root, "music");
            var moviePath = Path.Combine(root, "movies");
            var libraryOptions = new LibraryOptions { EnableRealtimeMonitor = false, PathInfos = new[] { new MediaPathInfo { Path = musicPath } } };
            var music = _library.AddVirtualFolder("FilmIndex Test Music", "music", libraryOptions, false);
            var movies = _library.AddVirtualFolder("FilmIndex Test Movies", "movies", new LibraryOptions
            { EnableRealtimeMonitor = false, PathInfos = new[] { new MediaPathInfo { Path = moviePath } } }, false);
            Plugin.LibraryApi.UpdateLibraryPathsInScope();
            var directory = new DirectoryService(_logger, _files);
            var audio = new Audio { Id = Guid.NewGuid(), Name = "音乐测试", Album = "测试专辑", Artists = new[] { "测试歌手" }, AlbumArtists = new[] { "专辑歌手" },
                Path = Path.Combine(musicPath, "track.mp3"), Size = new FileInfo(Path.Combine(musicPath, "track.mp3")).Length,
                RunTimeTicks = TimeSpan.FromSeconds(3).Ticks, Container = "mp3", IndexNumber = 2, ParentIndexNumber = 1 };
            _library.CreateItem(audio, music);
            _repository.SaveMediaStreams(audio.InternalId, new List<MediaStream> { new MediaStream
                { Type = MediaStreamType.Audio, Codec = "mp3", Index = 0, SampleRate = 44100, Channels = 2 } }, CancellationToken.None);
            _library.UpdateItem(audio, music, ItemUpdateType.MetadataImport);
            await _providers.SaveImage(audio, libraryOptions, Path.Combine(root, "cover.jpg"), ImageType.Primary,
                null, Array.Empty<long>(), directory, false, CancellationToken.None);
            _library.UpdateItem(audio, music, ItemUpdateType.ImageUpdate);
            Check(audio.HasImage(ImageType.Primary), "Native image provider accepts the audio cover fixture");
            Check(await Plugin.MediaInfoApi.SerializeMediaInfo(audio.InternalId, directory, true, "fixture"), "Music sidecar is written through the production serializer");
            var sidecar = MediaInfoApi.GetMediaInfoJsonPath(audio);
            using (var payload = JsonDocument.Parse(await File.ReadAllTextAsync(sidecar)))
            {
                var entry = payload.RootElement[0];
                Check(entry.GetProperty("Album").GetString() == "测试专辑", "Music sidecar contains album metadata");
                Check(!string.IsNullOrWhiteSpace(entry.GetProperty("EmbeddedImage").GetString()), "Music sidecar contains embedded cover data");
            }
            _repository.SaveMediaStreams(audio.InternalId, new List<MediaStream>(), CancellationToken.None);
            audio.Name = "lost"; audio.Album = string.Empty; audio.Artists = Array.Empty<string>(); audio.AlbumArtists = Array.Empty<string>(); audio.RunTimeTicks = null;
            _library.UpdateItem(audio, music, ItemUpdateType.MetadataImport);
            Check(await Plugin.MediaInfoApi.DeserializeMediaInfo(audio, new DirectoryService(_logger, _files), "fixture", true), "Music sidecar restores through production deserialization");
            var restored = (Audio)_library.GetItemById(audio.InternalId);
            Check(restored.Album == "测试专辑" && restored.Artists.Contains("测试歌手") && restored.AlbumArtists.Contains("专辑歌手"),
                "Album and artist data survive the database round trip: " + restored.Album + ";" + string.Join(",", restored.Artists) + ";" + string.Join(",", restored.AlbumArtists));
            Check(restored.GetMediaStreams().Any(stream => stream.Type == MediaStreamType.Audio) && restored.RunTimeTicks > 0, "Audio streams and duration survive the database round trip");
            Check(restored.HasImage(ImageType.Primary), "Cover is registered in Emby after restoration");

            var movie = new Movie { Id = Guid.NewGuid(), Name = "电视剧测试电影", Path = Path.Combine(moviePath, "fixture.mp4"),
                Size = new FileInfo(Path.Combine(moviePath, "fixture.mp4")).Length,
                RunTimeTicks = TimeSpan.FromSeconds(3).Ticks, Container = "mp4" };
            _library.CreateItem(movie, movies);
            _repository.SaveMediaStreams(movie.InternalId, new List<MediaStream> {
                new MediaStream { Type = MediaStreamType.Video, Codec = "mpeg4", Index = 0, Width = 720, Height = 480 },
                new MediaStream { Type = MediaStreamType.Audio, Codec = "aac", Index = 1, Channels = 2, SampleRate = 44100 }
            }, CancellationToken.None);
            _library.UpdateItem(movie, movies, ItemUpdateType.MetadataImport);
            Check(Plugin.SubtitleApi.HasExternalSubtitleChanged(movie, directory, true), "External subtitle/audio addition is detected");
            await Plugin.SubtitleApi.UpdateExternalSubtitles(movie, Plugin.SubtitleApi.GetExternalSubtitleRefreshOptions(), true, false);
            var streams = _repository.GetMediaStreams(new MediaStreamQuery { ItemId = movie.InternalId }, CancellationToken.None);
            Check(streams.Any(stream => stream.Type == MediaStreamType.Subtitle && stream.IsExternal), "External subtitle is persisted in the real stream repository");
            Check(streams.Any(stream => stream.Type == MediaStreamType.Audio && stream.IsExternal && !string.IsNullOrWhiteSpace(stream.Codec)), "External audio is probed and persisted");
            Check(streams.Select(stream => stream.Index).Distinct().Count() == streams.Count, "External streams have unique indexes");
            var previous = streams.Select(stream => (stream.Type, stream.Index, stream.Path)).ToArray();
            await File.WriteAllTextAsync(Path.Combine(moviePath, "fixture.en.mka"), "invalid audio fixture");
            var failed = false;
            try { await Plugin.SubtitleApi.UpdateExternalSubtitles(movie, Plugin.SubtitleApi.GetExternalSubtitleRefreshOptions(), true, false); }
            catch { failed = true; }
            Check(failed && previous.SequenceEqual(_repository.GetMediaStreams(new MediaStreamQuery { ItemId = movie.InternalId }, CancellationToken.None).Select(stream => (stream.Type, stream.Index, stream.Path))), "Failed external audio probing does not overwrite existing stream records");

            options.ExperienceEnhanceOptions.EnhancedChineseSearch = true;
            SaveOptions(options);
            SearchIndexApi.OptionsChanged();
            var searchType = typeof(SearchIndexApi);
            var ready = false;
            for (var retry = 0; retry < 100; retry++)
            {
                var instance = searchType.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                ready = instance != null && (bool)searchType.GetField("_ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
                if (ready) break;
                await Task.Delay(100);
            }
            Check(ready, "Chinese search index builds in a running Emby server");
            var query = new InternalItemsQuery { SearchTerm = "電視劇", IncludeItemTypes = new[] { nameof(Movie) }, Recursive = true };
            var results = _library.GetItemsResult(query);
            Check(results.Items.Any(item => item.InternalId == movie.InternalId), "Traditional Chinese search finds a simplified Chinese title through the native query");
            Check(query.SearchTerm == "電視劇" && (query.ItemIds == null || query.ItemIds.Length == 0), "Search adapter preserves the caller query object");
            var missing = _library.GetItemList(new InternalItemsQuery { SearchTerm = "不存在的超长测试标题", Recursive = true });
            Check(missing.Length == 0, "Search with no candidates does not return the whole library");
            var restricted = await _users.CreateUser("film-index-fixture-user", new UserPolicy
            { IsAdministrator = false, EnableAllFolders = false, EnabledFolders = new[] { music.Id.ToString("N") }, EnableAllDevices = true });
            var restrictedResults = _library.GetItemList(new InternalItemsQuery { User = restricted, SearchTerm = "電視劇", Recursive = true });
            Check(restrictedResults.All(item => item.InternalId != movie.InternalId), "Chinese search preserves native library access restrictions");
            await _users.DeleteUser(restricted);

            var cleanup = new CleanupService(_library, _repository, _files);
            var bif = Path.Combine(moviePath, "fixture-320-10.bif");
            var neighborBif = Path.Combine(moviePath, "fixture-other-320-10.bif");
            await File.WriteAllBytesAsync(bif, new byte[] { 137, 66, 73, 70 });
            await File.WriteAllBytesAsync(neighborBif, new byte[] { 137, 66, 73, 70 });
            var chapterImage = Path.Combine(moviePath, "chapter-fixture.jpg");
            File.Copy(Directory.EnumerateFiles(Path.Combine(root, "state", "metadata"), "*.jpg", SearchOption.AllDirectories).First(), chapterImage);
            _repository.SaveChapters(movie.InternalId, new List<ChapterInfo> { new ChapterInfo
                { Name = "Intro start", MarkerType = MarkerType.IntroStart, StartPositionTicks = 10, ImagePath = chapterImage } });
            await cleanup.Post(new CleanupItem { Id = movie.Id.ToString("N"), Action = CleanupAction.Thumbnails });
            Check(!File.Exists(bif) && !File.Exists(chapterImage), "Thumbnail cleanup removes selected BIF and chapter image files");
            Check(File.Exists(neighborBif) && File.Exists(movie.Path), "Thumbnail cleanup preserves neighboring BIF and source video");
            Check(_repository.GetChapters(movie).Any(chapter => chapter.MarkerType == MarkerType.IntroStart && chapter.StartPositionTicks == 10 && string.IsNullOrEmpty(chapter.ImagePath)), "Thumbnail cleanup preserves intro marker timing");
            var person = new Person { Id = Guid.NewGuid(), Name = "Fixture duplicate actor" };
            _library.CreateItem(person, _library.RootFolder);
            await cleanup.Post(new CleanupItem { Id = person.Id.ToString("N"), Action = CleanupAction.Person });
            Check(_library.GetItemById(person.Id) == null && File.Exists(movie.Path), "Deleting an individual actor record preserves the media file");
            await cleanup.Post(new CleanupItem { Id = movie.Id.ToString("N"), Action = CleanupAction.MediaInfo, DeleteSidecar = true });
            Check(_repository.GetMediaStreams(new MediaStreamQuery { ItemId = movie.InternalId }, CancellationToken.None).Count == 0 &&
                !_library.GetItemById(movie.Id).RunTimeTicks.HasValue, "Media-info cleanup clears stored streams and duration");
            Check(File.Exists(movie.Path) && !File.Exists(MediaInfoApi.GetMediaInfoJsonPath(movie)), "Media-info cleanup preserves video and removes only its plugin backup");
            await NetworkChecks.Run(_http, options, SaveOptions, Check);
            await CompletionChecks.Run(root, _library, _users, _userdata, options, SaveOptions, Check);
            await DistributedChecks.Run(root, movie, _users, _probe, options, SaveOptions, Check);
            await MultiVersionChecks.Run(root, _library, _users, _userdata, _files, options, SaveOptions, Check);
            await LocalMetadataChecks.Run(root, movie, _library, _files, _providers, options, SaveOptions, Check);
            NotificationChecks.Run(movie, _library, options, SaveOptions, Check);
            await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { success = true, checks = _checks }));
        }
        catch (Exception error)
        {
            _logger.Error(error.ToString());
            await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { success = false, checks = _checks, error = error.ToString() }));
        }
    }
    private static void SaveOptions(StrmAssistant.Options.PluginOptions options)
    {
        var save = typeof(Plugin).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .First(method => method.Name == "SaveOptions" && method.GetParameters().Length == 1);
        save.Invoke(Plugin.Instance, new object[] { options });
    }
}
