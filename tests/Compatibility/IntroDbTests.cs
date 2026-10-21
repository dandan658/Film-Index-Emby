using System.Collections;
using System.Reflection;

internal static class IntroDbTests
{
    public static void Run(Assembly built, Action<bool,string> check)
    {
        var rules = built.GetType("StrmAssistant.Common.IntroDbRules", true)!;
        var merge = rules.GetMethod("Merge")!;
        const string json = """{"imdb_id":"tt0903747","is_movie":false,"season":1,"episode":2,"intro":{"start_ms":2500,"end_ms":58000},"outro":{"start_ms":110000,"end_ms":119000}}""";
        var chapter = Assembly.Load("MediaBrowser.Model").GetType("MediaBrowser.Model.Entities.ChapterInfo", true)!;
        var marker = chapter.GetProperty("MarkerType")!;
        var ticks = chapter.GetProperty("StartPositionTicks")!;
        var empty = Array.CreateInstance(chapter, 0);
        IList Merge(string text, string imdb = "tt0903747", int season = 1, int episode = 2, object? existing = null, bool replace = false) =>
            (IList)merge.Invoke(null, new object[] { text, imdb, season, episode, TimeSpan.FromSeconds(120).Ticks, existing ?? empty, replace })!;
        var valid = Merge(json);
        check(valid.Count == 3 && (long)ticks.GetValue(valid[0])! == TimeSpan.FromMilliseconds(2500).Ticks,
            "IntroDB parses exact episode markers with millisecond precision");
        check(Merge(json, episode: 3).Count == 0 && Merge(json, imdb: "tt1234567").Count == 0,
            "IntroDB rejects another episode or title");
        check(Merge(json.Replace("58000", "580000")).Count == 1,
            "IntroDB rejects intro timestamps outside the video duration");
        check(Merge(json.Replace("2500", "-1")).Count == 1,
            "IntroDB rejects negative timestamps while accepting a valid independent credits marker");
        check(Merge(json, existing: valid).Cast<object>().Select(value => ticks.GetValue(value))
            .SequenceEqual(valid.Cast<object>().Select(value => ticks.GetValue(value))),
            "IntroDB preserves existing markers by default");
        var changed = json.Replace("2500", "3500");
        var preserved = Merge(changed, existing: valid);
        var replaced = Merge(changed, existing: valid, replace: true);
        check((long)ticks.GetValue(preserved[0])! == 2500 * TimeSpan.TicksPerMillisecond &&
            (long)ticks.GetValue(replaced[0])! == 3500 * TimeSpan.TicksPerMillisecond,
            "IntroDB replaces existing markers only on explicit replacement");
        check(Merge(json.Replace("\"intro\":", "\"no_intro\":true,\"intro\":")).Count == 1,
            "IntroDB respects a confirmed no-intro result");
        var distributed = built.GetType("StrmAssistant.Common.DistributedRules", true)!;
        var allows = distributed.GetMethod("AllowsFile")!;
        var root = Path.Combine(Path.GetTempPath(), "film-index-rules");
        check((bool)allows.Invoke(null, new object[] { Path.Combine(root, "video.mp4"), root })! &&
            !(bool)allows.Invoke(null, new object[] { Path.Combine(root + "-other", "video.mp4"), root })! &&
            !(bool)allows.Invoke(null, new object[] { Path.Combine(root, "..", "outside.mp4"), root })!,
            "Distributed root checks reject sibling prefixes and parent traversal");
    }
}
