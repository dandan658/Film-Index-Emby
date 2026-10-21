using HarmonyLib;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using StrmAssistant.Options;
using System.Net;
using System.Reflection;

namespace FilmIndex.IntegrationTests;

internal static class IntroDbChecks
{
    private static int _requests;
    // Fixture-only interception; never compiled into the production plugin.
    private static bool Response(HttpRequestMessage __0, ref Task<HttpResponseMessage> __result)
    {
        if (__0.RequestUri?.Host != "api.introdb.app" || !__0.RequestUri.Query.Contains("imdb_id=tt99999991")) return true;
        Interlocked.Increment(ref _requests);
        __result = Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
            """{"imdb_id":"tt99999991","is_movie":true,"intro":{"start_ms":5000,"end_ms":20000},"outro":{"start_ms":110000,"end_ms":120000}}""") });
        return false;
    }
    public static async Task Run(string root, ILibraryManager library, IItemRepository repository, IProviderManager providers,
        IFileSystem files, PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        var patch = new Harmony("film-index.fixture.introdb");
        var send = typeof(HttpClient).GetMethod(nameof(HttpClient.SendAsync), new[] { typeof(HttpRequestMessage), typeof(HttpCompletionOption), typeof(CancellationToken) })!;
        patch.Patch(send, prefix: new HarmonyMethod(typeof(IntroDbChecks).GetMethod(nameof(Response), BindingFlags.NonPublic | BindingFlags.Static)!));
        try
        {
            var directory = Path.Combine(root, "intro-movies");
            var collection = library.AddVirtualFolder("Intro marker fixture", "intro-marker-fixture", new MediaBrowser.Model.Configuration.LibraryOptions
            { EnableRealtimeMonitor = false, PathInfos = new[] { new MediaBrowser.Model.Configuration.MediaPathInfo { Path = directory } } }, false);
            var parent = collection.GetPhysicalFolderItems(new MediaBrowser.Controller.Entities.InternalItemsQuery()).First();
            var movie = new Movie { Id = Guid.NewGuid(), Name = "Intro marker fixture", Path = Path.Combine(directory, "intro.mp4"), Container = "mp4", RunTimeTicks = TimeSpan.FromSeconds(120).Ticks };
            movie.SetParent(parent); movie.SetProviderId(MetadataProviders.Imdb, "tt99999991"); library.CreateItem(movie, parent);
            options.IntroSkipOptions.EnableIntroDb = true; options.IntroSkipOptions.IntroDbOnAutomatedRefresh = false;
            options.IntroSkipOptions.IntroDbSource = PublicIntroSource.IntroDbApp; save(options);
            var native = StrmAssistant.Common.LibraryApi.CopyLibraryOptions(library.GetLibraryOptions(movie));
            native.DisabledLocalMetadataReaders = new[] { "Nfo" }; native.MetadataSavers = Array.Empty<string>();
            foreach (var type in native.TypeOptions) { type.MetadataFetchers = new[] { "Film Index IntroDB" }; type.ImageFetchers = Array.Empty<string>(); }
            await providers.RefreshSingleItem(movie, new MetadataRefreshOptions(files) { IsAutomated = false, MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly, ForceSave = true }, library.GetCollectionFolders(movie).ToArray(), native, CancellationToken.None);
            var chapters = repository.GetChapters(movie);
            check(_requests == 1 && chapters.Any(c => c.MarkerType == MarkerType.IntroStart && c.StartPositionTicks == TimeSpan.FromSeconds(5).Ticks) &&
                chapters.Any(c => c.MarkerType == MarkerType.IntroEnd && c.StartPositionTicks == TimeSpan.FromSeconds(20).Ticks) &&
                chapters.Any(c => c.MarkerType == MarkerType.CreditsStart && c.StartPositionTicks == TimeSpan.FromSeconds(110).Ticks),
                "Native metadata refresh discovers IntroDB provider and persists exact intro/credits markers in the chapter repository; requests=" + _requests + "; runtime=" + movie.RunTimeTicks + "; chapters=" + string.Join(",", chapters.Select(c => c.MarkerType + ":" + c.StartPositionTicks)));
            var fetch = new StrmAssistant.Provider.IntroDbMovieProvider(repository);
            var result = new MetadataResult<Movie> { Item = movie, Chapters = chapters };
            var status = await fetch.FetchAsync(result, new MetadataRefreshOptions(files) { IsAutomated = true }, native, CancellationToken.None);
            check(status == ItemUpdateType.None && _requests == 1, "IntroDB automated refresh remains disabled unless explicitly enabled");
            options.IntroSkipOptions.EnableIntroDb = false; save(options);
        }
        finally { patch.UnpatchAll(patch.Id); }
    }
}
