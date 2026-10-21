using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public sealed class ArtworkLanguageApi : IServerEntryPoint, IDisposable
    {
        private readonly IProviderManager _providers;
        private Harmony _patch;
        public ArtworkLanguageApi(IProviderManager providers) { _providers = providers; }
        public void Run()
        {
            try
            {
                _patch = new Harmony("film-index.emby.artwork-language");
                var method = _providers.GetType().GetMethods().Single(method => method.Name == "GetAvailableRemoteImages" && method.GetParameters().Length == 5);
                _patch.Patch(method, postfix: new HarmonyMethod(typeof(ArtworkLanguageApi), nameof(Result)));
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex artwork language adapter unavailable: " + error.GetType().Name); }
        }
        private static int Script(string language)
        {
            if (string.IsNullOrEmpty(language) || !language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return 0;
            var pieces = language.Split('-', '_');
            if (pieces.Any(part => new[] { "Hant", "TW", "HK", "MO" }.Contains(part, StringComparer.OrdinalIgnoreCase))) return 2;
            if (pieces.Any(part => new[] { "Hans", "CN", "SG" }.Contains(part, StringComparer.OrdinalIgnoreCase))) return 1;
            return 0;
        }
        public static IEnumerable<RemoteImageInfo> Sort(IEnumerable<RemoteImageInfo> images, string preferred, string country)
        {
            if (preferred?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) != true) return images;
            var target = Script(preferred);
            if (target == 0) target = new[] { "TW", "HK", "MO" }.Contains(country, StringComparer.OrdinalIgnoreCase) ? 2 : 1;
            int Rank(RemoteImageInfo image)
            {
                if (image.Type != ImageType.Primary && image.Type != ImageType.Logo) return 0;
                var script = Script(image.Language);
                if (script == target) return 4;
                if (image.Language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true && script == 0) return 3;
                if (string.IsNullOrEmpty(image.Language)) return 2;
                return script != 0 ? 1 : 0;
            }
            return images.OrderByDescending(Rank).ToArray();
        }
        public static void Result(BaseItem __0, LibraryOptions __1, ref Task<IEnumerable<RemoteImageInfo>> __result)
        {
            if (!Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions.ChineseArtworkScripts || __0 == null || __result == null) return;
            var language = __0.GetPreferredImageLanguage(__1);
            if (language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true)
                __result = Ordered(__result, language, __0.GetPreferredMetadataCountryCode(__1));
        }
        private static async Task<IEnumerable<RemoteImageInfo>> Ordered(Task<IEnumerable<RemoteImageInfo>> result, string language, string country) =>
            Sort(await result.ConfigureAwait(false), language, country);
        public void Dispose() => _patch?.UnpatchAll(_patch.Id);
    }
}
