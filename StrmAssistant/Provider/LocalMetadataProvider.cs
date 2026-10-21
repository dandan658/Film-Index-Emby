using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using StrmAssistant.Common;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Provider
{
    public sealed class LocalMetadataProvider : ICustomMetadataProvider<Movie>, ICustomMetadataProvider<Series>,
        ICustomMetadataProvider<Season>, ICustomMetadataProvider<Episode>, ICustomMetadataProvider<BoxSet>
    {
        public string Name => "Film Index Local Metadata";
        private static async Task<ItemUpdateType> Fetch(BaseItem item, CancellationToken token)
            => await LocalMetadataStore.Process(item, token).ConfigureAwait(false) ? ItemUpdateType.MetadataImport : ItemUpdateType.None;
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Movie> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Series> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Season> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Episode> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<BoxSet> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result.Item, token);
    }
}
