using HarmonyLib;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public sealed class StrmSubtitleApi : IServerEntryPoint, IDisposable
    {
        private Harmony _patch;
        private static readonly AsyncLocal<bool> CallingNative = new();
        private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
        public void Run()
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "OpenSubtitles");
                var type = assembly?.GetType("OpenSubtitles.OpenSubtitleDownloader");
                var search = type?.GetMethod("Search", new[] { typeof(SubtitleSearchRequest), typeof(CancellationToken) });
                if (search == null) return;
                _patch = new Harmony("film-index.emby.strm-subtitles");
                _patch.Patch(search, prefix: new HarmonyMethod(typeof(StrmSubtitleApi), nameof(Before)));
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex STRM subtitle adapter unavailable: " + error.GetType().Name); }
        }
        public static SubtitleSearchRequest Prepare(SubtitleSearchRequest request, string resolvedPath)
        {
            var copy = (SubtitleSearchRequest)Clone.Invoke(request, null);
            copy.MediaPath = resolvedPath;
            if (Uri.TryCreate(resolvedPath, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                if (HasIdentity(request)) copy.IsPerfectMatch = false;
            }
            return copy;
        }
        public static bool HasIdentity(SubtitleSearchRequest request)
        {
            var imdb = request.ContentType == VideoContentType.Movie ? request.GetProviderId(MetadataProviders.Imdb) :
                request.SeriesProviderIds?.FirstOrDefault(pair => pair.Key.Equals("Imdb", StringComparison.OrdinalIgnoreCase)).Value;
            return IntroDbRules.ValidImdb(imdb) && (request.ContentType == VideoContentType.Movie ||
                request.ParentIndexNumber >= 0 && request.IndexNumber > 0 && !(request.IndexNumberEnd > request.IndexNumber));
        }
        public static bool Before(object __instance, MethodBase __originalMethod, SubtitleSearchRequest __0, CancellationToken __1,
            ref Task<IEnumerable<RemoteSubtitleInfo>> __result)
        {
            if (CallingNative.Value || __0 == null || !Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.OptimizeStrmSubtitleSearch ||
                !string.Equals(Path.GetExtension(__0.MediaPath), ".strm", StringComparison.OrdinalIgnoreCase)) return true;
            __result = Search(__instance, (MethodInfo)__originalMethod, __0, __1); return false;
        }
        private static async Task<IEnumerable<RemoteSubtitleInfo>> Search(object provider, MethodInfo method, SubtitleSearchRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = await Plugin.LibraryApi.GetStrmMountPath(request.MediaPath).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) return Array.Empty<RemoteSubtitleInfo>();
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && !HasIdentity(request))
            { Plugin.Instance.Logger.Warn("FilmIndex: remote STRM subtitles need an IMDb ID and exact TV numbering; an unfiltered search was skipped"); return Array.Empty<RemoteSubtitleInfo>(); }
            var prepared = Prepare(request, path);
            var previous = CallingNative.Value; CallingNative.Value = true;
            try { return await ((Task<IEnumerable<RemoteSubtitleInfo>>)method.Invoke(provider, new object[] { prepared, token })).ConfigureAwait(false); }
            finally { CallingNative.Value = previous; }
        }
        public void Dispose() { _patch?.UnpatchAll(_patch.Id); }
    }
}
