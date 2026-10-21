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
    }
}
