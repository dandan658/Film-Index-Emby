using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using StrmAssistant.Common;
using StrmAssistant.Options;

namespace FilmIndex.IntegrationTests;

// No external image request is made by this fixture provider.
public sealed class ArtworkFixtureProvider : IRemoteImageProvider
{
    public string Name => "Film Index Artwork Fixture";
    public bool Supports(BaseItem item) => item.Name == "Artwork test";
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Logo };
    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken token) => throw new InvalidOperationException("Fixture image download is forbidden");
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions options, CancellationToken token) => Task.FromResult<IEnumerable<RemoteImageInfo>>(new[] {
        new RemoteImageInfo { Language = "zh-CN", Type = ImageType.Primary, Url = "https://fixture.invalid/cn.jpg", CommunityRating = 10, ProviderName = Name },
        new RemoteImageInfo { Language = "zh-TW", Type = ImageType.Primary, Url = "https://fixture.invalid/tw.jpg", CommunityRating = 1, ProviderName = Name },
        new RemoteImageInfo { Language = "zh", Type = ImageType.Primary, Url = "https://fixture.invalid/zh.jpg", CommunityRating = 5, ProviderName = Name }
    });
}
internal static class ArtworkChecks
{
    public static async Task Run(Movie movie, IProviderManager providers, PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        movie.Name = "Artwork test";
        var library = new LibraryOptions { PreferredImageLanguage = "zh-TW", MetadataCountryCode = "TW",
            TypeOptions = new[] { new TypeOptions { Type = "Movie", ImageFetchers = new[] { "Film Index Artwork Fixture" } } } };
        var query = new RemoteImageQuery { ProviderName = "Film Index Artwork Fixture", IncludeDisabledProviders = true, IncludeAllLanguages = true, ImageType = ImageType.Primary };
        options.MetadataEnhanceOptions.ChineseArtworkScripts = false; save(options);
        var baseline = (await providers.GetAvailableRemoteImages(movie, library, query, CancellationToken.None)).ToArray();
        check(baseline.Length == 3, "Native image manager discovers the fixture image provider without external downloads");
        options.MetadataEnhanceOptions.ChineseArtworkScripts = true; save(options);
        var traditional = (await providers.GetAvailableRemoteImages(movie, library, query, CancellationToken.None)).ToArray();
        check(traditional[0].Language == "zh-TW" && traditional[1].Language == "zh", "Native remote artwork query prioritizes traditional labels for a traditional image preference");
        query.IncludeAllLanguages = false;
        var filtered = (await providers.GetAvailableRemoteImages(movie, library, query, CancellationToken.None)).ToArray();
        check(filtered.Length > 0 && filtered[0].Language == "zh-TW", "Native preferred-language filtering retains the requested traditional artwork");
        query.IncludeAllLanguages = true;
        library.PreferredImageLanguage = "zh-CN"; library.MetadataCountryCode = "CN";
        var simplified = (await providers.GetAvailableRemoteImages(movie, library, query, CancellationToken.None)).ToArray();
        check(simplified[0].Language == "zh-CN" && traditional[0].Language == "zh-TW", "Simplified and traditional artwork requests retain independent ordering and original labels");
        library.PreferredImageLanguage = "en";
        var english = (await providers.GetAvailableRemoteImages(movie, library, query, CancellationToken.None)).ToArray();
        options.MetadataEnhanceOptions.ChineseArtworkScripts = false; save(options);
        var englishNative = (await providers.GetAvailableRemoteImages(movie, library, query, CancellationToken.None)).ToArray();
        check(english.Select(image => image.Url).SequenceEqual(englishNative.Select(image => image.Url)), "English artwork requests retain native ordering");
    }
}
