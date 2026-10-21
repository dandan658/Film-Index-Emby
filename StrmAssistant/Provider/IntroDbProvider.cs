using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using StrmAssistant.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Provider
{
    public sealed class IntroDbEpisodeProvider : ICustomMetadataProvider<Episode>
    {
        private readonly IItemRepository _repository;
        public IntroDbEpisodeProvider(IItemRepository repository) { _repository = repository; }
        public string Name => "Film Index IntroDB";
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Episode> result, MetadataRefreshOptions options, LibraryOptions libraryOptions, CancellationToken token)
            => IntroDbProvider.Fetch(result, options, _repository, token);
    }
    public sealed class IntroDbMovieProvider : ICustomMetadataProvider<Movie>
    {
        private readonly IItemRepository _repository;
        public IntroDbMovieProvider(IItemRepository repository) { _repository = repository; }
        public string Name => "Film Index IntroDB";
        public Task<ItemUpdateType> FetchAsync(MetadataResult<Movie> result, MetadataRefreshOptions options, LibraryOptions libraryOptions, CancellationToken token)
            => IntroDbProvider.Fetch(result, options, _repository, token);
    }
    internal static class IntroDbProvider
    {
        public static async Task<ItemUpdateType> Fetch<T>(MetadataResult<T> result, MetadataRefreshOptions options, IItemRepository repository, CancellationToken token) where T : BaseItem
        {
            var settings = Plugin.Instance.GetPluginOptions().IntroSkipOptions;
            if (!settings.EnableIntroDb || options.IsAutomated && !settings.IntroDbOnAutomatedRefresh) return ItemUpdateType.None;
            var item = result.Item;
            if (item == null || !item.RunTimeTicks.HasValue) return ItemUpdateType.None;
            int? season = null, episode = null;
            var imdb = item.GetProviderId(MetadataProviders.Imdb);
            if (item is Episode tv)
            {
                season = tv.ParentIndexNumber; episode = tv.IndexNumber;
                if (season < 0 || episode <= 0 || !season.HasValue || !episode.HasValue || tv.IndexNumberEnd > episode) return ItemUpdateType.None;
                imdb = tv.Series?.GetProviderId(MetadataProviders.Imdb);
            }
            if (!IntroDbRules.ValidImdb(imdb)) return ItemUpdateType.None;
            var json = await IntroDbClient.Get(imdb, season, episode, token).ConfigureAwait(false);
            if (json == null) return ItemUpdateType.None;
            var chapters = result.Chapters ?? repository.GetChapters(item);
            try
            {
                var merged = IntroDbRules.Merge(json, imdb, season, episode, item.RunTimeTicks.Value, chapters, options.ReplaceAllMetadata);
                if (merged.Count == chapters.Count && merged.Zip(chapters, (a,b) => a.MarkerType == b.MarkerType && a.StartPositionTicks == b.StartPositionTicks).All(same => same)) return ItemUpdateType.None;
                result.Chapters = merged;
                return ItemUpdateType.MetadataDownload;
            }
            catch (System.Text.Json.JsonException) { Plugin.Instance.Logger.Warn("FilmIndex: invalid IntroDB response; existing chapters preserved"); return ItemUpdateType.None; }
        }
    }
}
