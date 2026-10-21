using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using StrmAssistant.Common;
using StrmAssistant.Options;

namespace FilmIndex.IntegrationTests;

internal static class MetadataLanguageChecks
{
    public static async Task Run(string root, ILibraryManager library, IProviderManager providers, PluginOptions options,
        Action<PluginOptions> save, Action<bool,string> check)
    {
        options.MetadataEnhanceOptions.DefaultTmdbForNewLibraries = true;
        options.MetadataEnhanceOptions.ChineseFallbackLanguages = true; save(options);
        var path = Path.Combine(root, "metadata-language"); Directory.CreateDirectory(path);
        var original = new LibraryOptions { EnableRealtimeMonitor = false, PathInfos = new[] { new MediaPathInfo { Path = path } },
            TypeOptions = new[] { new TypeOptions { Type = "Movie", MetadataFetchers = new[] { "TheTVDB" }, ImageFetchers = new[] { "TheTVDB" } } } };
        var added = library.AddVirtualFolder("Native metadata defaults", "movies", original, false);
        try
        {
            var persisted = library.GetLibraryOptions(added);
            check(persisted.TypeOptions.First(entry => entry.Type == "Movie").MetadataFetchers.First() == "TheMovieDb" &&
                persisted.TypeOptions.First(entry => entry.Type == "Series").ImageFetchers.First() == "TheMovieDb",
                "New libraries persist TMDB as the first metadata and image provider");
            check(original.TypeOptions[0].MetadataFetchers.SequenceEqual(new[] { "TheTVDB" }) && original.PathInfos.Length == 1,
                "Native library default adaptation preserves the caller's options and paths");
            var native = providers.GetEnabledMetadataProviders(new Movie(), persisted).First(provider => provider.Name == "TheMovieDb");
            var languageMethod = native.GetType().GetMethod("GetMovieDbMetadataLanguages")!;
            var languages = (string[])languageMethod.Invoke(native, new object[] { new MovieInfo { MetadataLanguage = "zh-CN", MetadataCountryCode = "CN" }, new[] { "zh-CN", "zh-TW", "zh-HK", "en-US" } })!;
            check(languages[0] == "zh-CN" && Array.IndexOf(languages, "zh-TW") < Array.IndexOf(languages,"en-US") && languages.Distinct().Count() == languages.Length,
                "Native TMDB language selection retains the requested language and tries Chinese alternatives before English");
            var result = new MetadataResult<Movie> { HasMetadata = true, Item = new Movie { Name = "电视剧", Overview = "电影简介", OriginalTitle = "Original Title" } };
            var converted = await NativeMetadataApi.MetadataResultHooks<Movie>.Convert(Task.FromResult(result), true);
            check(converted.Item.Name == "電視劇" && converted.Item.Overview == "電影簡介" && converted.Item.OriginalTitle == "Original Title",
                "Traditional Chinese fallback converts displayed metadata while preserving original titles");
            var english = new MovieInfo { MetadataLanguage = "en-US" };
            check(!NativeMetadataApi.IsChinese(english) && NativeMetadataApi.IsTraditional(new MovieInfo { MetadataLanguage = "zh", MetadataCountryCode = "TW" }),
                "Chinese fallback respects English requests and traditional-Chinese region settings");
        }
        finally { options.MetadataEnhanceOptions.DefaultTmdbForNewLibraries = false; options.MetadataEnhanceOptions.ChineseFallbackLanguages = false; save(options); }
    }
}
