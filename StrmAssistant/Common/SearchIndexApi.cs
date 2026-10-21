using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public sealed class SearchIndexApi : IServerEntryPoint, IDisposable
    {
        private readonly ILibraryManager _library;
        private readonly ILogger _logger;
        private readonly Harmony _patch = new Harmony("film-index.emby.search");
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private ConcurrentDictionary<long, string> _titles = new ConcurrentDictionary<long, string>();
        private ConcurrentDictionary<long, string> _building;
        private int _rebuilding;
        private bool _ready;
        private bool _available;
        private static SearchIndexApi _instance;
        private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        public SearchIndexApi(ILibraryManager library, MediaBrowser.Model.Logging.ILogManager logs)
        { _library = library; _logger = logs.GetLogger("FilmIndexSearch"); }

        public void Run()
        {
            _instance = this;
            _library.ItemAdded += Changed;
            _library.ItemUpdated += Changed;
            _library.ItemRemoved += Removed;
            try
            {
                var targets = _library.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method => (method.Name == "GetItemList" || method.Name == "GetItemsResult") &&
                        method.GetParameters().FirstOrDefault()?.ParameterType == typeof(InternalItemsQuery)).ToArray();
                if (targets.Length == 0) throw new NotSupportedException("Native library query entry points are missing");
                foreach (var target in targets) _patch.Patch(target, prefix: new HarmonyMethod(typeof(SearchIndexApi), nameof(BeforeQuery)));
                _available = true;
                if (Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.EnhancedChineseSearch) RequestRebuild();
            }
            catch (Exception error)
            {
                _patch.UnpatchAll(_patch.Id);
                _logger.Error("Chinese search is unavailable; native search is preserved: " + error.Message);
            }
        }

        public static void BeforeQuery(ref InternalItemsQuery __0)
        {
            var index = _instance;
            if (index == null || __0 == null || !SearchText.ContainsChinese(__0.SearchTerm) ||
                !Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.EnhancedChineseSearch) return;
            if (!Volatile.Read(ref index._ready)) { index.RequestRebuild(); return; }
            var original = __0;
            var terms = SearchText.Terms(original.SearchTerm);
            var matches = index._titles.Where(pair => SearchText.MatchesTerms(pair.Value, terms)).Select(pair => pair.Key);
            if (original.ItemIds?.Length > 0) matches = matches.Intersect(original.ItemIds);
            var ids = matches.ToArray();
            var copy = (InternalItemsQuery)Clone.Invoke(original, null);
            copy.ItemIds = ids.Length == 0 ? new[] { -1L } : ids;
            copy.SearchTerm = null;
            // User, permissions, ancestor restrictions and all pagination remain native.
            __0 = copy;
        }

        private void RequestRebuild()
        {
            if (!_available || _stop.IsCancellationRequested || Interlocked.Exchange(ref _rebuilding, 1) != 0) return;
            _ = Task.Run(() =>
            {
                var next = new ConcurrentDictionary<long, string>();
                Volatile.Write(ref _building, next);
                try
                {
                    for (var offset = 0; ; offset += 1000)
                    {
                        _stop.Token.ThrowIfCancellationRequested();
                        var items = _library.GetItemList(new InternalItemsQuery
                        { Recursive = true, StartIndex = offset, Limit = 1000 }, _stop.Token);
                        foreach (var item in items) next[item.InternalId] = Title(item);
                        if (items.Length < 1000) break;
                        if (offset >= 200000) throw new InvalidOperationException("Index size exceeds the memory limit; native search remains active");
                    }
                    Interlocked.Exchange(ref _titles, next);
                    Volatile.Write(ref _ready, true);
                    _logger.Info("Chinese title index ready: {0} items", next.Count);
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { _logger.Error("Chinese title index was not replaced: " + error.Message); }
                finally { Volatile.Write(ref _building, null); Volatile.Write(ref _rebuilding, 0); }
            });
        }

        private static string Title(BaseItem item) => SearchText.Normalize(item.Name + "\n" + item.OriginalTitle + "\n" + item.SortName);
        public static void OptionsChanged()
        {
            var current = _instance;
            if (current == null) return;
            Volatile.Write(ref current._ready, false);
            if (Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.EnhancedChineseSearch) current.RequestRebuild();
            else current._titles.Clear();
        }
        private void Changed(object sender, ItemChangeEventArgs args)
        {
            if (!Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.EnhancedChineseSearch) return;
            var title = Title(args.Item);
            _titles[args.Item.InternalId] = title;
            var building = Volatile.Read(ref _building);
            if (building != null) building[args.Item.InternalId] = title;
        }
        private void Removed(object sender, ItemChangeEventArgs args)
        {
            _titles.TryRemove(args.Item.InternalId, out _);
            Volatile.Read(ref _building)?.TryRemove(args.Item.InternalId, out _);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _library.ItemAdded -= Changed; _library.ItemUpdated -= Changed; _library.ItemRemoved -= Removed;
            _patch.UnpatchAll(_patch.Id);
            if (_instance == this) _instance = null;
        }
    }
}
