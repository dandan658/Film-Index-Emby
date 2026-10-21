using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2) throw new ArgumentException("Usage: Compatibility <Emby 4.10.1.0 system> <plugin output>");
var server = Path.GetFullPath(args[0]);
var plugin = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) => {
    foreach (var dir in new[] { server, plugin }) {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
var count = 0;
void Check(bool success, string description) {
    if (!success) throw new InvalidOperationException(description);
    Console.WriteLine("PASS: " + description);
    count++;
}
Type Load(string assembly, string name) => AssemblyLoadContext.Default
    .LoadFromAssemblyPath(Path.Combine(server, assembly + ".dll")).GetType(name, true)!;
var controller = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(server, "MediaBrowser.Controller.dll"));
Check(controller.GetName().Version == new Version(4, 10, 1, 0), "Exact Emby 4.10.1.0 reference version");
var subtitle = Load("Emby.Providers", "Emby.Providers.MediaInfo.SubtitleResolver");
Check(subtitle.GetMethod("GetExternalTracks")?.GetParameters().Length == 6, "External subtitle resolver has six parameters");
var thumbnail = Load("Emby.Providers", "Emby.Providers.MediaInfo.ThumbnailGenerator");
var parameters = thumbnail.GetMethod("RefreshThumbnailImages")!.GetParameters();
Check(parameters.Length == 10 && parameters[7].Name == "replaceExistingImages" && parameters[8].Name == "saveChapters", "Thumbnail replacement and persistence arguments are distinct");
var mount = Load("MediaBrowser.Model", "MediaBrowser.Model.IO.IMediaMount");
Check(mount.GetProperty("MountedPathInfo") != null, "Media mount exposes path metadata");
var sources = Load("MediaBrowser.Controller", "MediaBrowser.Controller.Library.IMediaSourceManager");
Check(sources.GetMethods().Any(m => m.Name == "GetStaticMediaSources" && m.GetParameters().Length == 8), "Media-source overload has eight parameters");
var fingerprint = Load("Emby.Providers", "Emby.Providers.Markers.AudioFingerprintManager");
Check(fingerprint.GetMethod("UpdateSequencesForSeason")?.GetParameters().Last().ParameterType == typeof(CancellationToken), "Fingerprint sequence update accepts cancellation");
Check(fingerprint.GetMethod("GetAllFingerprintFilesForSeason")!.ReturnType != typeof(Task<object>), "Fingerprint task uses a strongly typed result");
var built = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(plugin, "FilmIndex.Emby.dll"));
Check(built.GetTypes().All(t => t.Name != "UpdatePluginTask"), "Upstream automatic binary updater is excluded");
Check(built.GetType("StrmAssistant.Plugin", true) != null, "Plugin types load against exact server assemblies");
Check(File.Exists(Path.Combine(plugin, "ChineseConverter.dll")), "Chinese conversion dependency is present");
var rule = built.GetType("StrmAssistant.Common.ExtractionRules", true)!.GetMethod("IsExcluded")!;
bool Excluded(string? name, string? path, string[]? tags, string? keywords, string? blockedTags) =>
    (bool)rule.Invoke(null, new object?[] { name, path, tags, keywords, blockedTags })!;
Check(!Excluded(null, null, null, null, null), "Empty rules do not exclude media");
Check(!Excluded("Film", "/media/film", null, " ; \n ;", " ; "), "Whitespace tokens cannot match everything");
Check(Excluded("TRAILER", "/media/test", null, "trailer", null), "Keyword matching ignores case");
Check(Excluded("影片", "/media/不提取/影片", null, "不提取", null), "Chinese path keywords match");
Check(Excluded("Film", "/media/test", new[] { "Skip" }, null, "skip"), "Tag matching ignores case");
Check(!Excluded("Film", "/media/test", new[] { "skip-more" }, null, "skip"), "Tag matching does not exclude partial tags");
Check(Excluded("Film", "/media/test", new[] { "动画" }, null, "电影; 动画\n剧集"), "Multiple tags and newlines are supported");
Check(!Excluded("Film", "/media/test", null, ".*", null), "Keywords are literals, not regexes");
Console.WriteLine($"{count} checks passed. These are assembly compatibility checks, not media-library integration tests.");
