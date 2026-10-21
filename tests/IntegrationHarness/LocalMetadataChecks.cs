using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using StrmAssistant.Common;
using StrmAssistant.Options;
using StrmAssistant.Provider;
using System.Text.Json.Nodes;

namespace FilmIndex.IntegrationTests;

internal static class LocalMetadataChecks
{
    public static async Task Run(string root, Movie movie, ILibraryManager library, IFileSystem files, MediaBrowser.Controller.Providers.IProviderManager providers,
        PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        options.MetadataEnhanceOptions.LocalMetadataRoot = Path.Combine(root, "local-metadata");
        options.MetadataEnhanceOptions.LocalMetadataMode = LocalMetadataMode.OnlineAndCache; save(options);
        movie.SetProviderId(MetadataProviders.Tmdb, "999999998");
        movie.Name = "Online fixture"; movie.Overview = "Online overview"; movie.ProductionYear = 2024;
        library.UpdateItem(movie, movie.Parent, ItemUpdateType.MetadataImport);
        await LocalMetadataStore.Process(movie, CancellationToken.None);
        var relative = LocalMetadataStore.RelativePath(movie);
        var cache = Path.Combine(options.MetadataEnhanceOptions.LocalMetadataRoot, "cache", LocalMetadataStore.LanguageKey(movie), relative);
        check(File.Exists(cache) && JsonNode.Parse(await File.ReadAllTextAsync(cache))!["name"]!.GetValue<string>() == "Online fixture",
            "Local metadata writes an atomic language-specific cache under the exact TMDB ID");
        var overridden = Path.Combine(options.MetadataEnhanceOptions.LocalMetadataRoot, "override", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(overridden)!);
        const string text = """{"id":999999998,"title":"本地覆盖标题","overview":"自定义简介","release_date":"2024-01-02","genres":[{"name":"动画"}]}""";
        await File.WriteAllTextAsync(overridden, text);
        check(await LocalMetadataStore.Process(movie, CancellationToken.None) && movie.Name == "本地覆盖标题" && movie.Genres.Contains("动画"),
            "Local TMDB-compatible override applies title, overview, release date and genres");
        library.UpdateItem(movie, movie.Parent, ItemUpdateType.MetadataImport);
        check(library.GetItemById(movie.Id).Name == "本地覆盖标题" && await File.ReadAllTextAsync(overridden) == text,
            "Override survives the database round trip and its source file remains unchanged");
        movie.LockedFields = new[] { MetadataFields.Name }; movie.Name = "Locked title";
        await LocalMetadataStore.Process(movie, CancellationToken.None);
        check(movie.Name == "Locked title", "Local metadata preserves individually locked fields");
        movie.LockedFields = Array.Empty<MetadataFields>();
        await File.WriteAllTextAsync(overridden, text.Replace("999999998", "123456"));
        check(!await LocalMetadataStore.Process(movie, CancellationToken.None) && movie.Name == "Locked title",
            "Local metadata rejects an override belonging to another TMDB title");
        await File.WriteAllTextAsync(overridden, "{broken");
        check(!await LocalMetadataStore.Process(movie, CancellationToken.None) && movie.Name == "Locked title",
            "Invalid local JSON leaves the current metadata intact");
        File.Delete(overridden);
        options.MetadataEnhanceOptions.LocalMetadataMode = LocalMetadataMode.PreferCache; save(options);
        movie.Name = "Lost title";
        check(await LocalMetadataStore.Process(movie, CancellationToken.None) && movie.Name == "Locked title",
            "Prefer-cache mode restores metadata without a network request");
        var patches = Path.Combine(options.MetadataEnhanceOptions.LocalMetadataRoot, "patches", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(patches)!);
        await File.WriteAllTextAsync(patches, """{"overview":"只覆盖简介"}""");
        await LocalMetadataStore.Process(movie, CancellationToken.None);
        check(movie.Name == "Locked title" && movie.Overview == "只覆盖简介", "Partial metadata patches preserve unrelated fields");
        var images = Path.Combine(Path.GetDirectoryName(overridden)!, "images"); Directory.CreateDirectory(images);
        File.Copy(Path.Combine(root, "cover.jpg"), Path.Combine(images, "poster.jpg"));
        var provider = new LocalMetadataImages(files);
        var localImages = provider.GetImages(movie, library.GetLibraryOptions(movie), new MediaBrowser.Controller.Providers.DirectoryService(StrmAssistant.Plugin.Instance.Logger, files), CancellationToken.None);
        check(localImages.Any(image => image.Type == ImageType.Primary && image.FileInfo.Exists),
            "Local image provider discovers the per-title override poster");
        File.Delete(patches);
        await File.WriteAllTextAsync(overridden, text.Replace("本地覆盖标题", "Native pipeline title"));
        options.MetadataEnhanceOptions.LocalMetadataMode = LocalMetadataMode.OverrideOnly; save(options);
        File.Delete(Path.Combine(root, "movies", "fixture.en.mka")); // Corrupt fixture from the external-track failure test.
        var nativeOptions = LibraryApi.CopyLibraryOptions(library.GetLibraryOptions(movie));
        nativeOptions.DisabledLocalMetadataReaders = new[] { "Nfo" }; nativeOptions.MetadataSavers = Array.Empty<string>();
        foreach (var entry in nativeOptions.TypeOptions)
        { entry.MetadataFetchers = new[] { "Film Index Local Metadata" }; entry.ImageFetchers = Array.Empty<string>(); }
        await providers.RefreshSingleItem(movie, new MediaBrowser.Controller.Providers.MetadataRefreshOptions(files)
            { MetadataRefreshMode = MediaBrowser.Controller.Providers.MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MediaBrowser.Controller.Providers.MetadataRefreshMode.ValidationOnly, ForceSave = true },
            library.GetCollectionFolders(movie).ToArray(), nativeOptions, CancellationToken.None);
        check(library.GetItemById(movie.Id).Name == "Native pipeline title", "Native Emby metadata refresh discovers and applies the local metadata provider");
        options.MetadataEnhanceOptions.LocalMetadataMode = LocalMetadataMode.Off; save(options);
        movie.Name = "Off title";
        check(!await LocalMetadataStore.Process(movie, CancellationToken.None) && movie.Name == "Off title", "Disabling local metadata leaves native metadata processing unchanged");
        var extended = JsonNode.Parse("""{"provider_ids":{"Imdb":"tt1234567","Douban":"12345678","Unknown":"untrusted"},"official_rating":"PG-13","critic_rating":92,"studios":["Fixture studio"],"production_locations":["China"]}""")!.AsObject();
        LocalMetadataStore.Apply(movie, extended);
        library.UpdateItem(movie, movie.Parent, ItemUpdateType.MetadataImport);
        var persisted = (Movie)library.GetItemById(movie.Id);
        check(persisted.GetProviderId(MetadataProviders.Imdb) == "tt1234567" && persisted.GetProviderId("Douban") == "12345678" &&
            persisted.GetProviderId("Unknown") == null && persisted.OfficialRating == "PG-13" && persisted.CriticRating == 92 && persisted.Studios.Contains("Fixture studio") && persisted.ProductionLocations.Contains("China"),
            "Local cache restores known external IDs, ratings, studios and production locations through the native database");
        var beforeConflict = movie.Name;
        var rejected = false;
        try { LocalMetadataStore.Apply(movie, JsonNode.Parse("""{"name":"Wrong title","provider_ids":{"Imdb":"tt7654321"}}""")!.AsObject()); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && movie.Name == beforeConflict && movie.GetProviderId(MetadataProviders.Imdb) == "tt1234567",
            "Conflicting cached external identity is rejected before any metadata is changed");
        movie.LockedFields = new[] { MetadataFields.OfficialRating, MetadataFields.CriticRating, MetadataFields.Studios, MetadataFields.ProductionLocations };
        LocalMetadataStore.Apply(movie, JsonNode.Parse("""{"official_rating":"R","critic_rating":2,"studios":["Wrong"],"production_locations":["Wrong"]}""")!.AsObject());
        check(movie.OfficialRating == "PG-13" && movie.CriticRating == 92 && movie.Studios.Contains("Fixture studio") && movie.ProductionLocations.Contains("China"),
            "Extended local metadata respects each native rating, studio and location field lock");
        movie.LockedFields = Array.Empty<MetadataFields>();
        var special = new MediaBrowser.Controller.Entities.TV.Episode { ParentIndexNumber = 0, IndexNumber = 2 };
        LocalMetadataStore.Apply(special, JsonNode.Parse("""{"season_number":0,"episode_number":2,"sort_episode_number":3,"sort_season_number":1}""")!.AsObject());
        check(special.SortIndexNumber == 3 && special.SortParentIndexNumber == 1 && special.ParentIndexNumber == 0 && special.IndexNumber == 2,
            "Special-episode display ordering restores without changing actual season or episode identity");
        rejected = false;
        try { LocalMetadataStore.Apply(special, JsonNode.Parse("""{"name":"Wrong episode","season_number":1,"episode_number":2}""")!.AsObject()); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && special.Name != "Wrong episode", "Local metadata rejects a season mismatch before changing episode fields");
    }
}
