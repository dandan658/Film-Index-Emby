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
        private static async Task<ItemUpdateType> Fetch<T>(MetadataResult<T> result, CancellationToken token) where T : BaseItem
        {
            var people = result.People ?? new System.Collections.Generic.List<PersonInfo>();
            if (!await LocalMetadataStore.Process(result.Item, token, people).ConfigureAwait(false)) return ItemUpdateType.None;
            result.People = people; return ItemUpdateType.MetadataImport;
        }
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Movie> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Series> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Season> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Episode> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result, token);
        public Task<ItemUpdateType> FetchAsync(MetadataResult<BoxSet> result, MetadataRefreshOptions options, LibraryOptions library, CancellationToken token) => Fetch(result, token);
    }
}
