using System.Reflection;
using System.Runtime.Loader;

if (args.Length < 2 || args.Length > 3) throw new ArgumentException("Usage: Compatibility <Emby 4.10.1.0 system> <plugin output> [ffmpeg executable]");
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
Check(built.GetReferencedAssemblies().All(a => a.Name != "ChineseConverter"), "Plugin no longer needs the legacy conversion DLL");
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
var chinese = built.GetType("StrmAssistant.Common.ChineseText", true)!;
string? Simplify(string? value) => (string?)chinese.GetMethod("ToSimplified")!.Invoke(null, new object?[] { value });
string? Traditional(string? value) => (string?)chinese.GetMethod("ToTraditional")!.Invoke(null, new object?[] { value });
Check(Simplify("電視劇與電影") == "电视剧与电影", "Traditional titles convert to simplified");
Check(Traditional("电视剧与电影") == "電視劇與電影", "Simplified titles convert to traditional");
Check(Simplify("一目瞭然") == "一目了然", "Conversion supports phrase overrides");
Check(Simplify("Emby 4.10 😀") == "Emby 4.10 😀" && Simplify(null) == null, "Conversion preserves symbols, ASCII and null");
Check(Enumerable.Range(0, 32).AsParallel().All(_ => Simplify("電視劇") == "电视剧"), "Conversion initialization and reads are thread safe");
var earlier = built.GetType("StrmAssistant.Common.CompletionRules", true)!.GetMethod("IsEarlierEpisode")!;
bool Earlier(int? season, int? ep, int? doneSeason, int? doneEp) => (bool)earlier.Invoke(null, new object?[] { season, ep, doneSeason, doneEp })!;
Check(Earlier(1, 3, 1, 5), "Completion selects earlier episodes in the same season");
Check(Earlier(1, 10, 2, 1), "Completion selects preceding seasons");
Check(!Earlier(1, 5, 1, 5) && !Earlier(2, 1, 1, 5), "Completion excludes current and future episodes");
Check(!Earlier(0, 1, 1, 5) && !Earlier(1, 1, 0, 5), "Completion does not process specials");
Check(!Earlier(null, 1, 1, 5) && !Earlier(1, null, 1, 5), "Completion ignores missing numbering");
var audioResolver = Load("Emby.Providers", "Emby.Providers.MediaInfo.AudioTrackResolver");
Check(audioResolver.GetMethod("GetExternalTracks")?.GetParameters().Length == 6, "External audio resolver matches target server");
IntroDbTests.Run(built, Check);
if (args.Length == 3) await CaptureTests.Run(built, args[2], Check);
Console.WriteLine($"{count} checks passed. These are assembly compatibility checks, not media-library integration tests.");
