using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public sealed class NativeMetadataApi : IServerEntryPoint, IDisposable
    {
        private readonly ILibraryManager _library;
        private Harmony _patch;
        private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
        public NativeMetadataApi(ILibraryManager library) { _library = library; }
        public void Run()
        {
            try
            {
                _patch = new Harmony("film-index.emby.metadata");
                var add = _library.GetType().GetMethods().First(method => method.Name == "AddVirtualFolder" && method.GetParameters().Length == 4);
                _patch.Patch(add, prefix: new HarmonyMethod(typeof(NativeMetadataApi), nameof(NewLibrary)));
                var movieDb = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "MovieDb");
                if (movieDb == null) { Plugin.Instance.Logger.Warn("FilmIndex: native MovieDb plugin is absent; language adapter skipped"); return; }
                var languages = movieDb.GetType("MovieDb.MovieDbProviderBase")?.GetMethod("GetMovieDbMetadataLanguages");
                if (languages != null) _patch.Patch(languages, postfix: new HarmonyMethod(typeof(NativeMetadataApi), nameof(Languages)));
                foreach (var type in movieDb.GetTypes().Where(type => type.Name.EndsWith("Provider", StringComparison.Ordinal)))
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => method.Name == "GetMetadata"))
                    {
                        var task = method.ReturnType;
                        if (!task.IsGenericType || task.GetGenericTypeDefinition() != typeof(Task<>)) continue;
                        var result = task.GetGenericArguments()[0];
                        if (!result.IsGenericType || result.GetGenericTypeDefinition() != typeof(MetadataResult<>)) continue;
                        var hooks = typeof(MetadataResultHooks<>).MakeGenericType(result.GetGenericArguments()[0]);
                        _patch.Patch(method, postfix: new HarmonyMethod(hooks.GetMethod("Result")));
                    }
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex metadata adapter unavailable: " + error.GetType().Name); }
        }
        public static bool IsChinese(ItemLookupInfo info) => info?.MetadataLanguage?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
        public static bool IsTraditional(ItemLookupInfo info) => IsChinese(info) &&
            (info.MetadataLanguage.IndexOf("hant", StringComparison.OrdinalIgnoreCase) >= 0 ||
             new[] { "TW", "HK", "MO" }.Any(region => info.MetadataLanguage.EndsWith("-" + region, StringComparison.OrdinalIgnoreCase) || string.Equals(info.MetadataCountryCode, region, StringComparison.OrdinalIgnoreCase)));
        public static void Languages(ItemLookupInfo __0, string[] __1, ref string[] __result)
        {
            if (!Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.ChineseFallbackLanguages || !IsChinese(__0)) return;
            var current = __result ?? Array.Empty<string>();
            var first = current.Take(1);
            var fallback = new[] { "zh-TW", "zh-HK", "zh-CN" }.Where(language => __1.Contains(language, StringComparer.OrdinalIgnoreCase));
            __result = first.Concat(fallback).Concat(current.Skip(1)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static void NewLibrary(ref LibraryOptions __2)
        {
            if (__2 == null || !Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.DefaultTmdbForNewLibraries) return;
            var copy = (LibraryOptions)Clone.Invoke(__2, null);
            var entries = (copy.TypeOptions ?? Array.Empty<TypeOptions>()).Select(entry => (TypeOptions)Clone.Invoke(entry, null)).ToList();
            foreach (var type in new[] { "Movie", "Series", "Season", "Episode", "BoxSet", "Person" })
            {
                var entry = entries.FirstOrDefault(option => option.Type == type);
                if (entry == null) { entry = new TypeOptions { Type = type }; entries.Add(entry); }
                entry.MetadataFetchers = PreferTmdb(entry.MetadataFetchers);
                entry.MetadataFetcherOrder = PreferTmdb(entry.MetadataFetcherOrder);
                entry.ImageFetchers = PreferTmdb(entry.ImageFetchers);
                entry.ImageFetcherOrder = PreferTmdb(entry.ImageFetcherOrder);
            }
            copy.TypeOptions = entries.ToArray(); __2 = copy;
        }
        private static string[] PreferTmdb(string[] names) => new[] { "TheMovieDb" }.Concat(names ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        public static class MetadataResultHooks<T> where T : BaseItem
        {
            public static void Result(object __0, ref Task<MetadataResult<T>> __result)
            {
                if (!Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.ChineseFallbackLanguages) return;
                var info = __0 as ItemLookupInfo ?? __0?.GetType().GetProperty("SearchInfo")?.GetValue(__0) as ItemLookupInfo;
                if (IsChinese(info)) __result = Convert(__result, IsTraditional(info));
            }
            public static async Task<MetadataResult<T>> Convert(Task<MetadataResult<T>> task, bool traditional)
            {
                var result = await task.ConfigureAwait(false);
                if (result?.HasMetadata != true || result.Item == null) return result;
                string Text(string value) => traditional ? ChineseText.ToTraditional(value) : ChineseText.ToSimplified(value);
                result.Item.Name = Text(result.Item.Name); result.Item.Overview = Text(result.Item.Overview); result.Item.Tagline = Text(result.Item.Tagline);
                if (result.People != null) foreach (var person in result.People) { person.Name = Text(person.Name); person.Role = Text(person.Role); }
                return result;
            }
        }
        public void Dispose() { _patch?.UnpatchAll(_patch.Id); }
    }
}
