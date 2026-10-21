using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace StrmAssistant.Common
{
    public static class IntroDbRules
    {
        public static bool ValidImdb(string value) => value != null && Regex.IsMatch(value, @"^tt\d{5,12}$", RegexOptions.CultureInvariant);
        public static bool ValidTvdb(string value) => int.TryParse(value, out var id) && id > 0 && id <= 100000000 && id.ToString(System.Globalization.CultureInfo.InvariantCulture) == value;
        private static bool Integer(JsonElement value, out int number) { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number); }
        private static bool Long(JsonElement value, out long number) { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number); }

        public static List<ChapterInfo> MergeTheIntroDb(string json, string tvdb, int? season, int? episode, long runtime, IEnumerable<ChapterInfo> existing, bool replace)
        {
            var previous = existing?.ToList() ?? new List<ChapterInfo>();
            if (!ValidTvdb(tvdb) || runtime <= 0) return previous;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tvdb_id", out var id) || !Integer(id, out var number) || number.ToString(System.Globalization.CultureInfo.InvariantCulture) != tvdb ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != (season.HasValue ? "tv" : "movie")) return previous;
            if (season.HasValue && (!root.TryGetProperty("season", out var s) || !Integer(s, out var sn) || sn != season ||
                !root.TryGetProperty("episode", out var e) || !Integer(e, out var en) || en != episode)) return previous;
            // A different release duration can move every marker. Require a corroborated release group.
            if (!root.TryGetProperty("group_duration_ms", out var duration) || !Long(duration, out var milliseconds) || milliseconds <= 0 ||
                Math.Abs((double)milliseconds - runtime / TimeSpan.TicksPerMillisecond) > 2000) return previous;
            var normalized = new JsonObject { ["imdb_id"] = "tt0000000", ["is_movie"] = !season.HasValue, ["season"] = season, ["episode"] = episode };
            foreach (var pair in new[] { ("intro", "intro"), ("credits", "outro") })
            {
                // Emby's single intro/credits markers cannot represent separated segments safely.
                if (!root.TryGetProperty(pair.Item1, out var segments) || segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() != 1) continue;
                var segment = segments[0];
                if (segment.ValueKind != JsonValueKind.Object || !segment.TryGetProperty("start_ms", out var start) || !segment.TryGetProperty("end_ms", out var end)) continue;
                var from = start.ValueKind == JsonValueKind.Null ? 0 : Long(start, out var a) ? a : -1;
                var to = end.ValueKind == JsonValueKind.Null ? runtime / TimeSpan.TicksPerMillisecond : Long(end, out var b) ? b : -1;
                normalized[pair.Item2] = new JsonObject { ["start_ms"] = from, ["end_ms"] = to };
            }
            return Merge(normalized.ToJsonString(), "tt0000000", season, episode, runtime, previous, replace);
        }

        public static List<ChapterInfo> Merge(string json, string imdb, int? season, int? episode, long runtime,
            IEnumerable<ChapterInfo> existing, bool replace)
        {
            var chapters = existing?.ToList() ?? new List<ChapterInfo>();
            if (!ValidImdb(imdb) || runtime <= 0) return chapters;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("imdb_id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != imdb ||
                !root.TryGetProperty("is_movie", out var movie) || movie.ValueKind != (season.HasValue ? JsonValueKind.False : JsonValueKind.True))
                return chapters;
            if (season.HasValue && (!root.TryGetProperty("season", out var s) || !Integer(s, out var seasonNumber) || seasonNumber != season ||
                !root.TryGetProperty("episode", out var e) || !Integer(e, out var episodeNumber) || episodeNumber != episode)) return chapters;
            if (TrySegment(root, "intro", runtime, 300, out var start, out var end) &&
                (replace || !chapters.Any(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd)))
            {
                chapters.RemoveAll(c => c.MarkerType == MarkerType.IntroStart || c.MarkerType == MarkerType.IntroEnd);
                chapters.Add(new ChapterInfo { MarkerType = MarkerType.IntroStart, StartPositionTicks = start, Name = "IntroDB intro start" });
                chapters.Add(new ChapterInfo { MarkerType = MarkerType.IntroEnd, StartPositionTicks = end, Name = "IntroDB intro end" });
            }
            if (TrySegment(root, "outro", runtime, 900, out start, out end) &&
                (replace || !chapters.Any(c => c.MarkerType == MarkerType.CreditsStart)))
            {
                chapters.RemoveAll(c => c.MarkerType == MarkerType.CreditsStart);
                chapters.Add(new ChapterInfo { MarkerType = MarkerType.CreditsStart, StartPositionTicks = start, Name = "IntroDB credits start" });
            }
            return chapters.OrderBy(c => c.StartPositionTicks).ToList();
        }

        private static bool TrySegment(JsonElement root, string name, long runtime, int maxSeconds, out long start, out long end)
        {
            start = end = 0;
            if (root.TryGetProperty("no_" + name, out var absent) && absent.ValueKind == JsonValueKind.True) return false;
            if (!root.TryGetProperty(name, out var segment) || segment.ValueKind != JsonValueKind.Object ||
                !segment.TryGetProperty("start_ms", out var a) || !Long(a, out var from) ||
                !segment.TryGetProperty("end_ms", out var b) || !Long(b, out var to)) return false;
            if (from < 0 || to <= from || to > runtime / TimeSpan.TicksPerMillisecond || to - from < 5000 || to - from > maxSeconds * 1000) return false;
            start = from * TimeSpan.TicksPerMillisecond; end = to * TimeSpan.TicksPerMillisecond;
            return true;
        }
    }
}
