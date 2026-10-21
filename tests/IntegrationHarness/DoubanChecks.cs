using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using StrmAssistant.Common;
using StrmAssistant.Options;

namespace FilmIndex.IntegrationTests;

internal static class DoubanChecks
{
    public static async Task Run(string root, Movie movie, ILibraryManager library, IProviderManager providers, IFileSystem files,
        PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        const string html = """<span>IMDb: tt1234567</span><script type="application/ld+json">{"@type":"Movie","url":"/subject/12345678/","name":"豆瓣测试标题","description":"豆瓣测试简介 &amp; 更多资料","datePublished":"2024-01-02","aggregateRating":{"ratingValue":"8.8"}}</script>""";
        var record = DoubanMetadata.Parse(html, "12345678")!;
        check(record.Subject == "12345678" && record.Imdb == "tt1234567" && record.Rating == 8.8f && record.Year == 2024 && record.Overview == "豆瓣测试简介 & 更多资料",
            "Douban JSON-LD parser validates canonical subject and reads decoded metadata and rating");
        check(DoubanMetadata.Parse(html, "87654321") == null && DoubanMetadata.Parse(html.Replace("/subject/12345678/", "https://untrusted.example/subject/12345678/"), "12345678") == null,
            "Douban rejects another subject and foreign canonical host");
        movie.Name = "Foreign title"; movie.Overview = "Foreign overview";
        movie.SetProviderId(MetadataProviders.Imdb, "tt1234567"); movie.SetProviderId("Douban", "12345678"); movie.CommunityRating = 1;
        var mismatch = new DoubanRecord { Subject = "12345678", Imdb = "tt7654321", Kind = "Movie", Name = "错误片名", Rating = 9 };
        check(!DoubanMetadata.Apply(movie, mismatch, true) && movie.Name == "Foreign title" && movie.CommunityRating == 1,
            "Douban assistance rejects conflicting IMDb identity before changing fields");
        movie.LockedFields = new[] { MetadataFields.Name, MetadataFields.CommunityRating };
        check(DoubanMetadata.Apply(movie, record, true) && movie.Name == "Foreign title" && movie.CommunityRating == 1 && movie.Overview == record.Overview,
            "Douban applies the unlocked synopsis while preserving locked title and rating");
        movie.LockedFields = Array.Empty<MetadataFields>(); movie.Name = "用户中文片名"; movie.Overview = "用户中文简介";
        DoubanMetadata.Apply(movie, record, true);
        check(movie.Name == "用户中文片名" && movie.Overview == "用户中文简介" && movie.CommunityRating == 8.8f,
            "Douban preserves existing Chinese title and synopsis while updating an unlocked rating");
        options.MetadataEnhanceOptions.DoubanAssist = true; options.MetadataEnhanceOptions.DoubanNetwork = false;
        options.MetadataEnhanceOptions.LocalMetadataRoot = Path.Combine(root, "local-metadata"); save(options);
        await DoubanMetadata.SaveCache(options.MetadataEnhanceOptions.LocalMetadataRoot, record, CancellationToken.None);
        movie.Name = "Cache title"; movie.Overview = "Cache overview"; movie.CommunityRating = 1;
        check(await DoubanMetadata.ApplyCache(movie, CancellationToken.None) && movie.Name == record.Name && movie.CommunityRating == 8.8f &&
            File.Exists(DoubanMetadata.CachePath(options.MetadataEnhanceOptions.LocalMetadataRoot, "imdb-tt1234567")),
            "Douban atomic subject and IMDb caches apply offline without online access");
        movie.Name = "Pipeline title"; movie.Overview = "Pipeline overview"; library.UpdateItem(movie, movie.Parent, ItemUpdateType.MetadataImport);
        var native = LibraryApi.CopyLibraryOptions(library.GetLibraryOptions(movie));
        native.DisabledLocalMetadataReaders = new[] { "Nfo" }; native.MetadataSavers = Array.Empty<string>();
        foreach (var type in native.TypeOptions) { type.MetadataFetchers = new[] { "Film Index Douban Cache" }; type.ImageFetchers = Array.Empty<string>(); }
        await providers.RefreshSingleItem(movie, new MetadataRefreshOptions(files) { MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.ValidationOnly, ForceSave = true }, library.GetCollectionFolders(movie).ToArray(), native, CancellationToken.None);
        check(library.GetItemById(movie.Id).Name == record.Name && library.GetItemById(movie.Id).GetProviderId("Douban") == record.Subject,
            "Native Emby metadata refresh discovers Douban cache provider and persists the assisted metadata");
        movie.Name = "Offline task title"; library.UpdateItem(movie, movie.Parent, ItemUpdateType.MetadataImport);
        var task = new StrmAssistant.ScheduledTask.BuildDoubanCacheTask(library);
        await task.Execute(CancellationToken.None, new Progress<double>());
        check(library.GetItemById(movie.Id).Name == record.Name && !task.GetDefaultTriggers().Any(),
            "Douban scheduled task pages the native library and applies offline caches without an automatic network schedule");
        options.MetadataEnhanceOptions.DoubanAssist = false; save(options);
    }
}
