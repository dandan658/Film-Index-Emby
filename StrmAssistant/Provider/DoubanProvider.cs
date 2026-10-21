using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant.Common;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Provider
{
    public sealed class DoubanProvider : ICustomMetadataProvider<Movie>, ICustomMetadataProvider<Series>, ICustomMetadataProvider<Season>
    {
        public string Name => "Film Index Douban Cache";
        private static async Task<ItemUpdateType> Fetch(BaseItem item, CancellationToken token) =>
            item != null && await DoubanMetadata.ApplyCache(item, token).ConfigureAwait(false) ? ItemUpdateType.MetadataImport : ItemUpdateType.None;
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Movie> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Series> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Season> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
    }
    public sealed class DoubanExternalId : IExternalId
    {
        public string Name => "Douban";
        public string Key => "Douban";
        public string UrlFormatString => "https://movie.douban.com/subject/{0}/";
        public bool Supports(IHasProviderIds item) => item is Movie || item is Series || item is Season;
    }
}
