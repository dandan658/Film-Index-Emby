using StrmAssistant.Common;

namespace FilmIndex.IntegrationTests;

internal static class DiscChecks
{
    public static async Task Run(string root, MediaBrowser.Controller.MediaEncoding.IMediaProbeManager probe, MediaBrowser.Controller.Library.ILibraryManager library,
        MediaBrowser.Controller.Persistence.IItemRepository repository, StrmAssistant.Options.PluginOptions options,
        Action<StrmAssistant.Options.PluginOptions> save, Action<bool,string> check)
    {
        var source = Path.Combine(root, "disc-fixture", "long.iso");
        if (!File.Exists(source)) return;
        var playlist = DiscPlaylist.Read(source, CancellationToken.None);
        check(playlist.Number == 0 && Math.Abs(playlist.DurationTicks / (double)TimeSpan.TicksPerSecond - 185) < 1,
            "Generated real UDF Blu-ray ISO exposes its exact main MPLS duration without extracting video data");
        check(playlist.Clips.SequenceEqual(new[] { "00000" }) && playlist.Chapters.Contains(TimeSpan.FromSeconds(2).Ticks),
            "Real MPLS clip references and chapter positions are decoded from the UDF image");
        check(playlist.Languages.Values.Contains("chi"), "Real MPLS audio stream table preserves the authored Chinese language tag");
        options.MediaInfoExtractOptions.EnableDiscProbe = true; save(options);
        check(DiscProbeApi.Enabled && !LibraryApi.ExcludeMediaExtensions.Contains("iso"), "Enabling the initialized disc adapter admits ISO media into extraction tasks");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var info = await probe.GetMediaInfo(new MediaBrowser.Controller.MediaEncoding.MediaInfoRequest {
            MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo { Path = source, Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.File, Container = "iso" },
            MediaType = MediaBrowser.Model.Dlna.DlnaProfileType.Video, ExtractChapters = true }, deadline.Token);
        check(info.Container == "iso" && info.RunTimeTicks == playlist.DurationTicks && info.Size == new FileInfo(source).Length,
            "Native media probe adapter returns MPLS-accurate ISO duration and actual disc size instead of MPEG-TS tail estimates");
        check(info.Chapters.Any(chapter => chapter.StartPositionTicks == TimeSpan.FromSeconds(2).Ticks) && info.MediaStreams.Any(stream => stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio && (stream.Language == "chi" || stream.Language == "zho")),
            "Real FFprobe streams gain MPLS chapters and PID-matched audio language through the native normalizer");
        var collection = library.AddVirtualFolder("Disc fixture", "disc-fixture", new MediaBrowser.Model.Configuration.LibraryOptions { EnableRealtimeMonitor = false,
            PathInfos = new[] { new MediaBrowser.Model.Configuration.MediaPathInfo { Path = Path.GetDirectoryName(source) } } }, false);
        var parent = collection.GetPhysicalFolderItems(new MediaBrowser.Controller.Entities.InternalItemsQuery()).First();
        var movie = new MediaBrowser.Controller.Entities.Movies.Movie { Id = Guid.NewGuid(), Name = "Disc fixture movie", Path = source, Container = "iso" };
        movie.SetParent(parent); library.CreateItem(movie, parent);
        options.MediaInfoExtractOptions.EnableImageCapture = true; options.MediaInfoExtractOptions.PersistMediaInfoMode = "Default"; save(options);
        using var refreshDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var extracted = await StrmAssistant.Plugin.LibraryApi.OrchestrateMediaInfoProcessAsync(movie, "disc fixture", refreshDeadline.Token);
        check(extracted == true && movie.RunTimeTicks == playlist.DurationTicks && movie.GetMediaStreams().Any(stream => stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio),
            "Production extraction refresh persists real ISO media streams and accurate runtime in the native database");
        check(movie.HasImage(MediaBrowser.Model.Entities.ImageType.Primary, 0) && File.Exists(MediaInfoApi.GetMediaInfoJsonPath(movie)),
            "Production ISO extraction captures its selected Blu-ray playlist cover and writes a media-info recovery sidecar");
        var folderPath = Path.Combine(root, "disc-fixture", "long-folder");
        var folderPlaylist = DiscPlaylist.Read(folderPath, CancellationToken.None);
        var folderInfo = await probe.GetMediaInfo(new MediaBrowser.Controller.MediaEncoding.MediaInfoRequest {
            MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo { Path = folderPath, Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.File, Container = "bluray" },
            MediaType = MediaBrowser.Model.Dlna.DlnaProfileType.Video, ExtractChapters = true }, deadline.Token);
        check(folderPlaylist.DurationTicks == playlist.DurationTicks && folderInfo.RunTimeTicks == playlist.DurationTicks && folderInfo.Chapters.Length == info.Chapters.Length,
            "Authored BDMV directory and UDF image yield matching native runtime and chapters");
        var malformed = Path.Combine(root, "disc-fixture", "broken.iso"); await File.WriteAllTextAsync(malformed, "not a disc");
        var damaged = new MediaBrowser.Controller.Entities.Movies.Movie { Id = Guid.NewGuid(), Name = "Damaged disc fixture", Path = malformed, Container = "iso", RunTimeTicks = TimeSpan.FromSeconds(99).Ticks };
        damaged.SetParent(parent); library.CreateItem(damaged, parent);
        repository.SaveMediaStreams(damaged.InternalId, new List<MediaBrowser.Model.Entities.MediaStream> { new() { Index = 0, Type = MediaBrowser.Model.Entities.MediaStreamType.Video, Codec = "h264", Width = 640, Height = 360 } }, CancellationToken.None);
        var rejected = false;
        try { await StrmAssistant.Plugin.LibraryApi.OrchestrateMediaInfoProcessAsync(damaged, "damaged disc fixture", CancellationToken.None); }
        catch (Exception error) when (error is IOException || error is InvalidDataException) { rejected = true; }
        check(rejected && damaged.RunTimeTicks == TimeSpan.FromSeconds(99).Ticks && damaged.GetMediaStreams().Single().Codec == "h264" && !File.Exists(MediaInfoApi.GetMediaInfoJsonPath(damaged)),
            "Malformed ISO preflight fails before replacing prior native media records or writing a misleading recovery sidecar");
        var remote = new MediaBrowser.Controller.Entities.Movies.Movie { Path = "https://127.0.0.1:9/disc.iso?fixture=1", Container = "iso" };
        check(await StrmAssistant.Plugin.LibraryApi.OrchestrateMediaInfoProcessAsync(remote, "remote disc fixture", CancellationToken.None) == null,
            "Unsupported HTTP ISO with a query string is explicitly skipped without starting a probe or claiming success");
        options.MediaInfoExtractOptions.EnableDiscProbe = false; save(options);
        check(LibraryApi.ExcludeMediaExtensions.Contains("iso"), "Disabling disc extraction restores the original ISO exclusion");
    }
}
