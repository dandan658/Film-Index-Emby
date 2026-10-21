using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StrmAssistant.Common
{
    public static class IntroDbRules
    {
        public static bool ValidImdb(string value) => value != null && Regex.IsMatch(value, @"^tt\d{5,12}$", RegexOptions.CultureInvariant);

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
            if (season.HasValue && (!root.TryGetProperty("season", out var s) || !s.TryGetInt32(out var seasonNumber) || seasonNumber != season ||
                !root.TryGetProperty("episode", out var e) || !e.TryGetInt32(out var episodeNumber) || episodeNumber != episode)) return chapters;
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
                !segment.TryGetProperty("start_ms", out var a) || !a.TryGetInt64(out var from) ||
                !segment.TryGetProperty("end_ms", out var b) || !b.TryGetInt64(out var to)) return false;
            if (from < 0 || to <= from || to > runtime / TimeSpan.TicksPerMillisecond || to - from < 5000 || to - from > maxSeconds * 1000) return false;
            start = from * TimeSpan.TicksPerMillisecond; end = to * TimeSpan.TicksPerMillisecond;
            return true;
        }
    }
}
