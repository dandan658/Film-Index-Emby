using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant;
using StrmAssistant.Options;

namespace FilmIndex.IntegrationTests;

internal static class FingerprintChecks
{
    public static async Task Run(string root, ILibraryManager library, IItemRepository repository, PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        options.IntroSkipOptions.PreserveLibraryFingerprintLength = true; save(options);
        var episodes = new List<Episode>();
        foreach (var minutes in new[] { 2, 3 })
        {
            var path = Path.Combine(root, "fingerprint-" + minutes); Directory.CreateDirectory(path);
            var file = Path.Combine(path, "fixture.S01E01.mp4"); File.Copy(Path.Combine(root, "fingerprint-source.mp4"), file);
            var folder = library.AddVirtualFolder("Fingerprint fixture " + minutes, "fingerprint-" + minutes,
                new LibraryOptions { EnableRealtimeMonitor = false, IntroDetectionFingerprintLength = minutes, PathInfos = new[] { new MediaPathInfo { Path = path } } }, false);
            var physical = folder.GetPhysicalFolderItems(new InternalItemsQuery()).First();
            var series = new Series { Id = Guid.NewGuid(), Name = "Fingerprint series " + minutes, Path = Path.Combine(path, "show") };
            series.SetParent(physical); library.CreateItem(series, physical);
            var season = new Season { Id = Guid.NewGuid(), Name = "Season 1", IndexNumber = 1, SeriesId = series.InternalId };
            season.SetParent(series); library.CreateItem(season, series);
            var episode = new Episode { Id = Guid.NewGuid(), Name = "Fingerprint fixture episode " + minutes, IndexNumber = 1, ParentIndexNumber = 1,
                SeriesId = series.InternalId, Path = file, Container = "mp4", RunTimeTicks = TimeSpan.FromSeconds(160).Ticks, Size = new FileInfo(file).Length };
            episode.SetParent(season); library.CreateItem(episode, season);
            repository.SaveMediaStreams(episode.InternalId, new List<MediaStream> {
                new() { Index = 0, Type = MediaStreamType.Video, Codec = "mpeg4", Width = 64, Height = 64 },
                new() { Index = 1, Type = MediaStreamType.Audio, Codec = "aac", Channels = 1, SampleRate = 16000 }
            }, CancellationToken.None);
            check(library.GetLibraryOptions(episode).IntroDetectionFingerprintLength == minutes,
                "Native physical library inheritance preserves the " + minutes + " minute fingerprint setting");
            episodes.Add(episode);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var results = await Task.WhenAll(episodes.Select(episode => Plugin.FingerprintApi.CreateTitleFingerprint(episode, timeout.Token)));
        check(results.All(result => result.Item2 && File.Exists(result.Item1) && new FileInfo(result.Item1).Length > 0),
            "Two real generated audio fixtures produce nonempty native fingerprints through concurrent production API calls");
        check(Path.GetFileName(results[0].Item1).Contains("_2_") && Path.GetFileName(results[1].Item1).Contains("_3_"),
            "Native fingerprint outputs independently retain each library's configured extraction duration");
    }
}
