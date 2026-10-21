using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace StrmAssistant.Common
{
    internal static class IntroDbClient
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 256 * 1024 };
        private static readonly ConcurrentDictionary<string, (DateTimeOffset Until, string Json)> Cache = new();
        private static readonly SemaphoreSlim Slots = new(2, 2);
        private static long _retryAfterTicks;
        public static async Task<string> Get(string imdb, int? season, int? episode, CancellationToken token)
        {
            if (!IntroDbRules.ValidImdb(imdb) || DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _retryAfterTicks)) return null;
            var query = "imdb_id=" + imdb + (season.HasValue ? "&season=" + season + "&episode=" + episode : "&is_movie=true");
            return await GetUrl("https://api.introdb.app/segments?" + query, token).ConfigureAwait(false);
        }
        public static Task<string> GetTheIntroDb(string tvdb, int? season, int? episode, string episodeId, long runtime, CancellationToken token)
        {
            if (!IntroDbRules.ValidTvdb(tvdb) || runtime <= 0 || season.HasValue && (!IntroDbRules.ValidTvdb(episodeId) || season < 0 || !episode.HasValue || episode <= 0)) return Task.FromResult<string>(null);
            var query = "tvdb_id=" + tvdb + "&type=" + (season.HasValue ? "tv" : "movie") + "&details=true&merge_unknown=false&duration_ms=" + (runtime / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture);
            if (season.HasValue) query += "&season=" + season + "&episode=" + episode + "&mapping=official&tvdb_episode_id=" + episodeId;
            return GetUrl("https://api.theintrodb.org/v4/media?" + query, token);
        }
        private static async Task<string> GetUrl(string query, CancellationToken token)
        {
            if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _retryAfterTicks)) return null;
            if (Cache.TryGetValue(query, out var cached) && cached.Until > DateTimeOffset.UtcNow) return cached.Json;
            await Slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Cache.TryGetValue(query, out cached) && cached.Until > DateTimeOffset.UtcNow) return cached.Json;
                using var response = await Client.GetAsync(query, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                { Interlocked.Exchange(ref _retryAfterTicks, DateTimeOffset.UtcNow.AddMinutes(5).UtcTicks); return null; }
                if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                var json = response.StatusCode == HttpStatusCode.NotFound ? null : await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (Cache.Count >= 512) Cache.Clear();
                Cache[query] = (DateTimeOffset.UtcNow.AddHours(6), json);
                return json;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex IntroDB unavailable: " + error.GetType().Name); return null; }
            finally { Slots.Release(); }
        }
    }
}
