using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace StrmAssistant.Common
{
    public sealed class MultiVersionApi : IServerEntryPoint, IDisposable
    {
        private readonly IMediaSourceManager _sources;
        private Harmony _patch;
        private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        public MultiVersionApi(IMediaSourceManager sources) { _sources = sources; }
        public void Run()
        {
            try
            {
                _patch = new Harmony("film-index.emby.versions");
                foreach (var key in new[] { typeof(Video), typeof(Episode) }.Select(type => type.GetMethod("GetUserDataKeyInternal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).Where(method => method != null).Distinct())
                    _patch.Patch(key, postfix: new HarmonyMethod(typeof(MultiVersionApi), nameof(UserDataKey)));
                foreach (var method in _sources.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == "GetStaticMediaSources" && m.GetParameters().Length == 8))
                    _patch.Patch(method, postfix: new HarmonyMethod(typeof(MultiVersionApi), nameof(Sources)));
                var resolver = Assembly.Load("Emby.Naming").GetType("Emby.Naming.Video.VideoListResolver");
                var group = resolver?.GetMethod("GetVideosGroupedByVersion", BindingFlags.Instance | BindingFlags.NonPublic);
                if (group != null) _patch.Patch(group, transpiler: new HarmonyMethod(typeof(MultiVersionApi), nameof(VersionLimit)));
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex version adapter unavailable: " + error.ToString()); }
        }
        public static int GetLimit() => Math.Clamp(Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.MaximumFolderVersions, 8, 128);
        public static IEnumerable<CodeInstruction> VersionLimit(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            // Exact 4.10.1.0 method has one literal 8: its folder-version count guard.
            if (code.Count(i => i.opcode == OpCodes.Ldc_I4_8) != 1) return code;
            foreach (var item in code.Where(i => i.opcode == OpCodes.Ldc_I4_8))
            { item.opcode = OpCodes.Call; item.operand = typeof(MultiVersionApi).GetMethod(nameof(GetLimit)); }
            return code;
        }
        public static void UserDataKey(BaseItem __instance, MediaBrowser.Model.Data.IDataContext __0, ref string __result)
        {
            if (!Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.SeparateLibraryProgress ||
                __instance is not Movie && __instance is not Episode || string.IsNullOrEmpty(__result)) return;
            if (__result.Contains("|filmindex-library:", StringComparison.Ordinal)) return;
            var library = BaseItem.LibraryManager.GetCollectionFolders(__instance, __0, System.Threading.CancellationToken.None)
                .OfType<CollectionFolder>().OrderBy(folder => folder.InternalId).FirstOrDefault();
            if (library != null) { __result += "|filmindex-library:" + library.Id.ToString("N"); return; }
            var parent = __instance.GetParent(__0);
            for (var depth = 0; parent != null && depth < 128; depth++, parent = parent.GetParent(__0))
                if (parent is CollectionFolder)
                { __result += "|filmindex-library:" + parent.Id.ToString("N"); return; }
        }
        public static void Sources(ref List<MediaSourceInfo> __result)
        {
            var options = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions;
            if (__result == null || __result.Count < 2 || !options.SortVersionsByQuality && options.VersionLabels == Options.VersionLabelMode.Native) return;
            var values = __result.Select(source => (MediaSourceInfo)Clone.Invoke(source, null)).ToList();
            if (options.VersionLabels != Options.VersionLabelMode.Native)
                foreach (var source in values)
                {
                    if (options.VersionLabels == Options.VersionLabelMode.FileName)
                    { var name = Path.GetFileNameWithoutExtension(source.Path); if (!string.IsNullOrWhiteSpace(name)) source.Name = name; }
                    else
                    {
                        var video = source.MediaStreams?.FirstOrDefault(stream => stream.Type == MediaStreamType.Video);
                        var audio = source.MediaStreams?.FirstOrDefault(stream => stream.Type == MediaStreamType.Audio);
                        var label = string.Join(" · ", new[] { video?.Height > 0 ? video.Height + "p" : null,
                            video?.Codec?.ToUpperInvariant(), audio?.Codec?.ToUpperInvariant(), source.Bitrate > 0 ? (source.Bitrate.Value / 1000000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " Mbps" : null }
                            .Where(part => !string.IsNullOrWhiteSpace(part)));
                        if (label.Length > 0) source.Name = label;
                    }
                }
            if (options.SortVersionsByQuality)
                values = values.OrderByDescending(source => source.MediaStreams?.Where(stream => stream.Type == MediaStreamType.Video)
                    .Select(stream => (long)(stream.Width ?? 0) * (stream.Height ?? 0)).DefaultIfEmpty(0).Max() ?? 0)
                    .ThenByDescending(source => source.Bitrate ?? 0).ToList();
            __result = values;
        }
        public void Dispose() { _patch?.UnpatchAll(_patch.Id); }
    }
}

