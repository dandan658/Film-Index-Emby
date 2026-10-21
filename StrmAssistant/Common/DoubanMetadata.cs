using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public sealed class DoubanRecord
    {
        public int Schema { get; set; } = 1;
        public string Subject { get; set; }
        public string Imdb { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Overview { get; set; }
        public float? Rating { get; set; }
        public int? Year { get; set; }
    }
    public static class DoubanMetadata
    {
        public static bool ValidSubject(string id) => id != null && Regex.IsMatch(id, @"^[1-9]\d{3,11}$", RegexOptions.CultureInvariant);
        private static readonly Regex Scripts = new Regex(@"<script\b[^>]*\btype\s*=\s*['"" ]*application/ld\+json['"" ]*[^>]*>(.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex Cjk = new Regex(@"[\u3400-\u9fff]", RegexOptions.CultureInvariant);
        private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        public static DoubanRecord Parse(string html, string subject)
        {
            if (!ValidSubject(subject) || html == null || html.Length > 2 * 1024 * 1024) return null;
            foreach (Match script in Scripts.Matches(html))
            {
                using var document = JsonDocument.Parse(script.Groups[1].Value);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var kind = Text(root, "@type");
                if (kind != "Movie" && kind != "TVSeries") continue;
                var url = Text(root, "url") ?? Text(root, "@id");
                if (!Uri.TryCreate(new Uri("https://movie.douban.com/"), url, out var uri) || uri.Host != "movie.douban.com" || uri.AbsolutePath.TrimEnd('/') != "/subject/" + subject) continue;
                var name = Text(root, "name"); if (string.IsNullOrWhiteSpace(name)) continue;
                var imdbMatches = Regex.Matches(html, @"\btt\d{5,12}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>().Select(m => m.Value).Distinct().ToArray();
                var record = new DoubanRecord { Subject = subject, Kind = kind, Imdb = imdbMatches.Length == 1 ? imdbMatches[0] : null,
                    Name = WebUtility.HtmlDecode(name).Trim(), Overview = WebUtility.HtmlDecode(Text(root, "description"))?.Trim() };
                if (DateTime.TryParse(Text(root, "datePublished"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) record.Year = date.Year;
                if (root.TryGetProperty("aggregateRating", out var aggregate) && aggregate.ValueKind == JsonValueKind.Object && aggregate.TryGetProperty("ratingValue", out var rating) &&
                    float.TryParse(rating.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var score) && score >= 0 && score <= 10) record.Rating = score;
                return record;
            }
            return null;
        }
        public static bool Apply(BaseItem item, DoubanRecord record, bool rating)
        {
            if (record?.Schema != 1 || !ValidSubject(record.Subject) || item == null || item.IsLocked ||
                item is Movie && record.Kind != "Movie" || (item is Series || item is Season) && record.Kind != "TVSeries") return false;
            var subject = item.GetProviderId("Douban"); var imdb = item.GetProviderId(MetadataProviders.Imdb);
            // Shared IMDb caches are accepted only with a corroborating IMDb identity. A manually linked subject remains authoritative.
            if (ValidSubject(subject) ? subject != record.Subject : !IntroDbRules.ValidImdb(imdb) || imdb != record.Imdb) return false;
            if (IntroDbRules.ValidImdb(imdb) && IntroDbRules.ValidImdb(record.Imdb) && imdb != record.Imdb) return false;
            bool Can(MetadataFields field) => item.LockedFields?.Contains(field) != true;
            var changed = subject != record.Subject;
            if (Can(MetadataFields.Name) && !Cjk.IsMatch(item.Name ?? string.Empty) && Cjk.IsMatch(record.Name ?? string.Empty)) { item.Name = record.Name; changed = true; }
            if (Can(MetadataFields.Overview) && !Cjk.IsMatch(item.Overview ?? string.Empty) && Cjk.IsMatch(record.Overview ?? string.Empty)) { item.Overview = record.Overview; changed = true; }
            if (rating && Can(MetadataFields.CommunityRating) && record.Rating >= 0 && record.Rating <= 10 && item.CommunityRating != record.Rating) { item.CommunityRating = record.Rating; changed = true; }
            if (changed) item.SetProviderId("Douban", record.Subject);
            return changed;
        }
        public static string CachePath(string root, string identity) => Path.Combine(root, "douban", identity + ".json");
        public static async Task<bool> ApplyCache(BaseItem item, CancellationToken token)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            if (!options.DoubanAssist || !Path.IsPathFullyQualified(options.LocalMetadataRoot)) return false;
            var subject = item.GetProviderId("Douban"); var imdb = item.GetProviderId(MetadataProviders.Imdb);
            var identity = ValidSubject(subject) ? "subject-" + subject : IntroDbRules.ValidImdb(imdb) ? "imdb-" + imdb : null;
            if (identity == null) return false;
            try
            {
                var path = CachePath(options.LocalMetadataRoot, identity);
                if (!File.Exists(path)) return false;
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (input.Length > 2 * 1024 * 1024) return false;
                var record = await JsonSerializer.DeserializeAsync<DoubanRecord>(input, cancellationToken: token).ConfigureAwait(false);
                return Apply(item, record, options.DoubanRating);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex Douban cache skipped: " + error.GetType().Name); return false; }
        }
        public static async Task SaveCache(string root, DoubanRecord record, CancellationToken token)
        {
            if (!ValidSubject(record.Subject) || record.Schema != 1) throw new InvalidDataException("Invalid Douban cache identity");
            foreach (var identity in new[] { "subject-" + record.Subject, IntroDbRules.ValidImdb(record.Imdb) ? "imdb-" + record.Imdb : null }.Where(value => value != null))
            {
                var path = CachePath(root, identity); Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(record), token).ConfigureAwait(false); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
}
