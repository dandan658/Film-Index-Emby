using HarmonyLib;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.MediaInfo;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    internal static class RemoteProbeBridge
    {
        private sealed class Entry { public string Path; public MediaInfo Info; }
        private static readonly AsyncLocal<Entry> Current = new AsyncLocal<Entry>();
        private static Harmony _patch;
        private static long _cacheHits;
        public static long CacheHits => Interlocked.Read(ref _cacheHits);
        public static bool Available { get; private set; }
        public static void Initialize(IMediaProbeManager probe)
        {
            try
            {
                _patch = new Harmony("film-index.emby.distributed");
                var map = probe.GetType().GetInterfaceMap(typeof(IMediaProbeManager));
                var target = map.TargetMethods[Array.FindIndex(map.InterfaceMethods, method => method.Name == "GetMediaInfo")];
                _patch.Patch(target, prefix: new HarmonyMethod(typeof(RemoteProbeBridge), nameof(BeforeProbe)));
                Available = true;
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex: distributed probe adapter unavailable: " + error.GetType().Name); }
        }
        private static string Key(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile) return Path.GetFullPath(uri.LocalPath);
            return value;
        }
        public static bool BeforeProbe(MediaInfoRequest __0, CancellationToken __1, ref Task<MediaInfo> __result)
        {
            var entry = Current.Value;
            if (entry == null || __0?.MediaSource == null || !string.Equals(entry.Path, Key(__0.MediaSource.Path),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
            __result = __1.IsCancellationRequested ? Task.FromCanceled<MediaInfo>(__1) : Task.FromResult(entry.Info);
            Interlocked.Increment(ref _cacheHits);
            return false;
        }
        public static IDisposable Enter(MediaInfo info, string path) => new Scope(info, path);
        private sealed class Scope : IDisposable
        {
            private readonly Entry _previous;
            public Scope(MediaInfo info, string path) { _previous = Current.Value; Current.Value = info == null ? null : new Entry { Info = info, Path = Key(path) }; }
            public void Dispose() { Current.Value = _previous; }
        }
        public static void Dispose() { Available = false; _patch?.UnpatchAll(_patch.Id); }
    }
}
