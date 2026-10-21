using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.ScheduledTask
{
    public sealed class BuildDoubanCacheTask : IScheduledTask
    {
        private readonly ILibraryManager _library;
        private static readonly SemaphoreSlim Slot = new(1, 1);
        private static DateTimeOffset _nextRequest;
        private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        public BuildDoubanCacheTask(ILibraryManager library) { _library = library; }
        public string Name => "构建并应用豆瓣缓存";
        public string Key => "FilmIndexBuildDoubanCache";
        public string Category => "Film Index Emby";
        public string Description => "为已关联 Douban 编号的电影、节目和季构建独立缓存；仅缓存读取不发起在线请求。";
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
        public async Task Execute(CancellationToken token, IProgress<double> progress)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            if (!Path.IsPathFullyQualified(options.LocalMetadataRoot)) { Plugin.Instance.Logger.Warn("FilmIndex Douban: configure an absolute metadata cache root first"); return; }
            var start = 0; var completed = 0; var applied = 0; var failed = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var page = _library.GetItemsResult(new InternalItemsQuery { Recursive = true, IncludeItemTypes = new[] { "Movie", "Series", "Season" },
                    OrderBy = new[] { ("DateCreated", SortOrder.Descending), ("Id", SortOrder.Ascending) }, StartIndex = start, Limit = 200, EnableTotalRecordCount = true });
                if (page.Items.Length == 0) break;
                foreach (var item in page.Items)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var subject = item.GetProviderId("Douban");
                        if (options.DoubanNetwork && DoubanMetadata.ValidSubject(subject))
                        {
                            var cached = DoubanMetadata.CachePath(options.LocalMetadataRoot, "subject-" + subject);
                            var fresh = File.Exists(cached) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(cached)).TotalDays < 30;
                            if (!fresh)
                            {
                                var record = await Fetch(subject, token).ConfigureAwait(false);
                                var imdb = item.GetProviderId(MetadataProviders.Imdb);
                                if (record != null && (!IntroDbRules.ValidImdb(imdb) || !IntroDbRules.ValidImdb(record.Imdb) || imdb == record.Imdb) &&
                                    (item is Movie ? record.Kind == "Movie" : record.Kind == "TVSeries"))
                                    await DoubanMetadata.SaveCache(options.LocalMetadataRoot, record, token).ConfigureAwait(false);
                            }
                        }
                        if (await DoubanMetadata.ApplyCache(item, token).ConfigureAwait(false))
                        { _library.UpdateItem(item, item.Parent, ItemUpdateType.MetadataImport); applied++; }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error) { failed++; Plugin.Instance.Logger.Warn("FilmIndex Douban item skipped: " + item.InternalId + " " + error.GetType().Name); }
                    completed++; progress.Report(page.TotalRecordCount == 0 ? 100 : Math.Min(100, 100d * completed / page.TotalRecordCount));
                }
                start += page.Items.Length;
                if (start >= page.TotalRecordCount) break;
            }
            progress.Report(100);
            Plugin.Instance.Logger.Info("FilmIndex Douban cache: processed=" + completed + ", applied=" + applied + ", errors=" + failed);
        }
        private static async Task<DoubanRecord> Fetch(string subject, CancellationToken token)
        {
            await Slot.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var delay = _nextRequest - DateTimeOffset.UtcNow;
                // A blocked host should not tie up a scheduled task for minutes.
                if (delay > TimeSpan.FromSeconds(3)) return null;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
                _nextRequest = DateTimeOffset.UtcNow.AddSeconds(3);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://movie.douban.com/subject/" + subject + "/");
                request.Headers.UserAgent.ParseAdd("FilmIndex-Emby/0.1 (metadata-cache)");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
                { _nextRequest = DateTimeOffset.UtcNow.AddMinutes(10); Plugin.Instance.Logger.Warn("FilmIndex Douban: access limited, retaining existing cache"); return null; }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 2 * 1024 * 1024) return null;
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                { if (output.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Douban page exceeds limit"); output.Write(buffer, 0, read); }
                return DoubanMetadata.Parse(Encoding.UTF8.GetString(output.ToArray()), subject);
            }
            finally { Slot.Release(); }
        }
    }
}
