using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

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
            if (Cache.TryGetValue(query, out var cached) && cached.Until > DateTimeOffset.UtcNow) return cached.Json;
            await Slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Cache.TryGetValue(query, out cached) && cached.Until > DateTimeOffset.UtcNow) return cached.Json;
                using var response = await Client.GetAsync("https://api.introdb.app/segments?" + query, token).ConfigureAwait(false);
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
