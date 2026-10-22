using HarmonyLib;
using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace StrmAssistant.Common
{
    public sealed class PresentationApi : IServerEntryPoint, IDisposable
    {
        private readonly ILibraryManager _library;
        private readonly IDtoService _dto;
        private Harmony _patch;
        private static PresentationApi _instance;
        private readonly ConcurrentDictionary<(long User, long Item, bool Library), (DateTimeOffset Until, int Count)> _counts = new();
        private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
        public PresentationApi(ILibraryManager library, IDtoService dto) { _library = library; _dto = dto; }
        public void Run()
        {
            _instance = this;
            try
            {
                _patch = new Harmony("film-index.emby.presentation");
                foreach (var method in _library.GetType().GetMethods().Where(method =>
                    (method.Name == "GetItemList" || method.Name == "GetItemsResult") && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(InternalItemsQuery)))
                    _patch.Patch(method, prefix: new HarmonyMethod(typeof(PresentationApi), nameof(Query)));
                var dto = _dto.GetType().GetMethod("GetBaseItemDtoInternal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (dto != null) _patch.Patch(dto, postfix: new HarmonyMethod(typeof(PresentationApi), nameof(ItemDto)));
                var requestQuery = typeof(BaseApiService).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(method => method.Name == "GetItemsQuery" && method.GetParameters().Length == 5);
                _patch.Patch(requestQuery, postfix: new HarmonyMethod(typeof(PresentationApi), nameof(RequestQuery)));
            }
            catch (Exception error) { _patch?.UnpatchAll(_patch.Id); Plugin.Instance.Logger.Warn("FilmIndex presentation unavailable: " + error.GetType().Name); }
        }
        public static void RequestQuery(BaseItemsRequest __1, ref InternalItemsQuery __result)
        {
            // Folder.GetItems supplies its own ascending order before calling the library manager.
            // Apply the requested display default while the original HTTP request is still available.
            if (__1 == null || !string.IsNullOrWhiteSpace(__1.SortBy) || !string.IsNullOrWhiteSpace(__1.SortOrder)) return;
            Query(ref __result);
        }
        public static void Query(ref InternalItemsQuery __0)
        {
            if (__0 == null) return;
            var settings = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions;
            if (settings.HideSearchRecommendations && __0.WasSearched == true && string.IsNullOrWhiteSpace(__0.SearchTerm))
            {
                var empty = (InternalItemsQuery)Clone.Invoke(__0, null); empty.ItemIds = new[] { -1L }; __0 = empty; return;
            }
            var types = __0.IncludeItemTypes;
            // Defaults must not override an explicit sort requested by a client or task.
            if (types?.Length != 1 || __0.OrderBy?.Length > 0) return;
            var seasons = types[0] == nameof(Season) && settings.DescendingSeasons;
            var episodes = types[0] == nameof(Episode) && settings.DescendingEpisodes;
            if (!seasons && !episodes) return;
            var copy = (InternalItemsQuery)Clone.Invoke(__0, null);
            copy.OrderBy = episodes ? new[] { ("ParentIndexNumber", SortOrder.Descending), ("IndexNumber", SortOrder.Descending) } : new[] { ("IndexNumber", SortOrder.Descending) };
            __0 = copy;
        }
        private int Count(BaseItem item, User user, bool library)
        {
            var key = (user?.InternalId ?? 0, item.InternalId, library);
            if (_counts.TryGetValue(key, out var cached) && cached.Until > DateTimeOffset.UtcNow) return cached.Count;
            var query = new InternalItemsQuery { User = user, Recursive = true, IsVirtualItem = false,
                IncludeItemTypes = library ? new[] { "Movie", nameof(Series), nameof(MusicAlbum) } : new[] { nameof(Episode) } };
            query.ParentIds = new[] { item.InternalId };
            query.Limit = 0; query.EnableTotalRecordCount = true;
            var value = _library.GetItemsResult(query).TotalRecordCount;
            if (_counts.Count >= 1024) _counts.Clear();
            _counts[key] = (DateTimeOffset.UtcNow.AddSeconds(30), value);
            return value;
        }
        public static void ItemDto(BaseItem __0, User __2, ref BaseItemDto __result)
        {
            var instance = _instance;
            if (instance == null || __0 == null || __result == null) return;
            var options = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions;
            try
            {
                var total = options.ShowTotalEpisodes && (__0 is Series || __0 is Season);
                var library = options.ShowLibraryItemCounts && __0 is CollectionFolder;
                if (library || total && __result.UserData != null)
                {
                    var count = instance.Count(__0, __2, library);
                    var data = __result.UserData == null ? new UserItemDataDto() : (UserItemDataDto)Clone.Invoke(__result.UserData, null);
                    data.UnplayedItemCount = count;
                    __result.UserData = data;
                    if (library) __result.ChildCount = count;
                }
                if (options.IncludeSeasonCollections && __0 is Series)
                {
                    var seasons = instance._library.GetItemList(new InternalItemsQuery { User = __2, ParentIds = new[] { __0.InternalId }, IncludeItemTypes = new[] { nameof(Season) }, Recursive = false });
                    var ids = seasons.Select(season => season.InternalId).Append(__0.InternalId).ToArray();
                    var collections = instance._library.GetItemList(new InternalItemsQuery { User = __2, Recursive = true, IncludeItemTypes = new[] { nameof(BoxSet) }, ListItemIds = ids });
                    __result.Collections = (__result.Collections ?? Array.Empty<NameLongIdPair>()).Concat(collections.Select(collection => new NameLongIdPair { Id = collection.InternalId, Name = collection.Name })).GroupBy(pair => pair.Id).Select(group => group.First()).ToArray();
                }
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex presentation skipped: " + error.GetType().Name); }
        }
        public void Dispose() { _patch?.UnpatchAll(_patch.Id); if (ReferenceEquals(_instance, this)) _instance = null; }
    }
}
