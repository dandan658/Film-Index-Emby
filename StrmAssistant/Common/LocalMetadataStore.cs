using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using StrmAssistant.Options;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public static class LocalMetadataStore
    {
        public static string RelativePath(BaseItem item)
        {
            BaseItem owner = item is Episode episode ? episode.Series : item is Season season ? season.Series : item;
            var id = owner?.GetProviderId(MetadataProviders.Tmdb);
            if (id == null || !Regex.IsMatch(id, @"^\d{1,12}$", RegexOptions.CultureInvariant)) return null;
            var category = item is Movie ? "tmdb-movies2" : item is BoxSet ? "tmdb-collections" :
                item is Series || item is Season || item is Episode ? "tmdb-tv" : null;
            if (category == null) return null;
            var name = item is Series ? "series" : "all";
            if (item is Season s) { if (s.IndexNumber < 0 || !s.IndexNumber.HasValue) return null; name = "season-" + s.IndexNumber; }
            if (item is Episode e) { if (!e.ParentIndexNumber.HasValue || e.ParentIndexNumber < 0 || !e.IndexNumber.HasValue || e.IndexNumber <= 0 || e.IndexNumberEnd > e.IndexNumber) return null; name = "season-" + e.ParentIndexNumber + "-episode-" + e.IndexNumber; }
            return Path.Combine(category, id, name + ".json");
        }
        public static string LanguageKey(BaseItem item)
        {
            var language = item.GetPreferredMetadataLanguage() ?? "und";
            return Regex.IsMatch(language, @"^[a-zA-Z0-9-]{1,20}$") ? language.ToLowerInvariant() : "und";
        }
        private static async Task<JsonObject> Read(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return null;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (input.Length > 8 * 1024 * 1024) throw new InvalidDataException("Metadata JSON exceeds size limit");
            return await JsonNode.ParseAsync(input, cancellationToken: token).ConfigureAwait(false) as JsonObject
                ?? throw new InvalidDataException("Metadata root must be an object");
        }
        public static JsonObject Snapshot(BaseItem item) => new JsonObject
        {
            ["id"] = item.GetProviderId(MetadataProviders.Tmdb), ["name"] = item.Name,
            ["original_name"] = item.OriginalTitle, ["overview"] = item.Overview, ["tagline"] = item.Tagline,
            ["vote_average"] = item.CommunityRating, ["air_date"] = item.PremiereDate?.ToString("O"),
            ["production_year"] = item.ProductionYear,
            ["genres"] = JsonSerializer.SerializeToNode(item.Genres), ["tags"] = JsonSerializer.SerializeToNode(item.Tags)
        };
        public static JsonObject MergePatch(JsonObject original, JsonObject patch)
        {
            var result = (JsonObject)original.DeepClone();
            foreach (var field in patch)
            {
                if (field.Value == null) result.Remove(field.Key);
                else if (field.Value is JsonObject child) result[field.Key] = MergePatch(result[field.Key] as JsonObject ?? new JsonObject(), child);
                else result[field.Key] = field.Value.DeepClone();
            }
            return result;
        }
        private static string Text(JsonObject json, params string[] names)
        {
            foreach (var name in names) if (json[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
            return null;
        }
        private static string StringValue(JsonNode node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        public static void Apply(BaseItem item, JsonObject json)
        {
            if (item.IsLocked) return;
            bool Can(MetadataFields field) => item.LockedFields?.Contains(field) != true;
            if (Can(MetadataFields.Name) && Text(json, "title", "name") is string name && !string.IsNullOrWhiteSpace(name)) item.Name = name;
            if (Can(MetadataFields.OriginalTitle) && Text(json, "original_title", "original_name") is string original) item.OriginalTitle = original;
            if (Can(MetadataFields.Overview) && Text(json, "overview") is string overview) item.Overview = overview;
            if (Can(MetadataFields.Tagline) && Text(json, "tagline") is string tagline) item.Tagline = tagline;
            if (Can(MetadataFields.CommunityRating) && json["vote_average"] is JsonValue vote && vote.TryGetValue<float>(out var rating) && rating >= 0 && rating <= 10) item.CommunityRating = rating;
            if (DateTimeOffset.TryParse(Text(json, "release_date", "first_air_date", "air_date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            { item.PremiereDate = date; item.ProductionYear = date.Year; }
            if (json["production_year"] is JsonValue y && y.TryGetValue<int>(out var year) && year > 0 && year < 10000) item.ProductionYear = year;
            if (Can(MetadataFields.Genres) && json["genres"] is JsonArray genres)
                item.Genres = genres.Select(node => node is JsonObject genre ? Text(genre, "name") : StringValue(node)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
            if (Can(MetadataFields.Tags) && json["tags"] is JsonArray tags)
                item.Tags = tags.Select(StringValue).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
        }
        public static async Task<bool> Process(BaseItem item, CancellationToken token)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            if (options.LocalMetadataMode == LocalMetadataMode.Off || !Path.IsPathFullyQualified(options.LocalMetadataRoot)) return false;
            var relative = RelativePath(item); if (relative == null) return false;
            var applied = false;
            try
            {
                item.EnsureTaggedItemsLoaded();
                var root = options.LocalMetadataRoot;
                var cached = Path.Combine(root, "cache", LanguageKey(item), relative);
                var overridden = await Read(Path.Combine(root, "override", relative), token).ConfigureAwait(false);
                var json = overridden ?? (options.LocalMetadataMode == LocalMetadataMode.PreferCache ? await Read(cached, token).ConfigureAwait(false) : null);
                var patch = await Read(Path.Combine(root, "patches", relative), token).ConfigureAwait(false);
                if (patch != null) json = MergePatch(json ?? Snapshot(item), patch);
                if (json != null)
                {
                    if (item is Movie || item is Series || item is BoxSet)
                    {
                        var id = json["id"]?.ToString();
                        if (id != null && id != item.GetProviderId(MetadataProviders.Tmdb)) throw new InvalidDataException("Metadata ID does not match the item");
                    }
                    Apply(item, json);
                    applied = !item.IsLocked;
                }
                if (options.LocalMetadataMode == LocalMetadataMode.PreferCache || options.LocalMetadataMode == LocalMetadataMode.OnlineAndCache)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cached));
                    var temp = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { await File.WriteAllTextAsync(temp, Snapshot(item).ToJsonString(new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false); File.Move(temp, cached, true); }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                return applied;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex local metadata skipped: " + error.GetType().Name); return applied; }
        }
    }
}

