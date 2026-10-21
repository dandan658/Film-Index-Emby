using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.MediaInfo;
using StrmAssistant.Web.Api;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    internal static class RemoteProbeClient
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(100) };
        private static readonly JsonSerializerOptions Json = CreateJson();
        private static JsonSerializerOptions CreateJson()
        {
            var value = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            value.Converters.Add(new JsonStringEnumConverter());
            value.Converters.Add(new EmbyGuidConverter());
            return value;
        }
        private sealed class EmbyGuidConverter : JsonConverter<Guid>
        {
            public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
                => Guid.TryParse(reader.GetString(), out var id) ? id : throw new JsonException("Invalid request ID");
            public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);
        }
        public static async Task<MediaInfo> TryGet(BaseItem item, string source, CancellationToken token)
        {
            var options = Plugin.Instance.GetPluginOptions().DistributedOptions;
            if (!options.UseWorker || !RemoteProbeBridge.Available || string.IsNullOrWhiteSpace(options.WorkerApiKey)) return null;
            try
            {
                if (!Uri.TryCreate(options.WorkerUrl, UriKind.Absolute, out var endpoint) ||
                    !(endpoint.Scheme == "http" || endpoint.Scheme == "https") || !string.IsNullOrEmpty(endpoint.UserInfo) ||
                    !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment)) throw new ArgumentException("Invalid worker URL");
                long? size = null;
                var target = source;
                if (Path.IsPathFullyQualified(source)) { size = new FileInfo(source).Length; target = DistributedRules.Remap(source, options.MasterRoot, options.WorkerRoot); }
                var request = new ProbeWorker { RequestId = Guid.NewGuid(), SourcePath = target, ExpectedSize = size, IsAudio = item is Audio };
                if (!size.HasValue) request.Headers = item.GetMediaSources(false, false, null)
                    .FirstOrDefault(media => media.Path == source)?.RequiredHttpHeaders;
                using var message = new HttpRequestMessage(HttpMethod.Post, options.WorkerUrl.TrimEnd('/') + "/emby/FilmIndex/Worker/Probe");
                message.Headers.TryAddWithoutValidation("X-Emby-Token", options.WorkerApiKey);
                message.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(100));
                using var response = await Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                const int limit = 4 * 1024 * 1024;
                if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Worker response is too large");
                using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;
                while ((count = await input.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + count > limit) throw new InvalidDataException("Worker response is too large");
                    buffer.Write(chunk, 0, count);
                }
                var result = JsonSerializer.Deserialize<ProbeWorkerResult>(buffer.ToArray(), Json);
                if (result == null || result.Schema != 1 || result.RequestId != request.RequestId ||
                    result.ServerVersion != Plugin.Instance.ApplicationHost.ApplicationVersion.ToString() ||
                    size.HasValue && result.SourceSize != size || result.MediaInfo?.MediaStreams == null || result.MediaInfo.MediaStreams.Count == 0)
                    throw new InvalidDataException("Worker response does not match this file or server version");
                result.MediaInfo.Path = source;
                Plugin.Instance.Logger.Info("FilmIndex: worker media information verified for item {0}", item.InternalId);
                return result.MediaInfo;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            { Plugin.Instance.Logger.Warn("FilmIndex: worker failed ({0}); using local probe for item {1}", error.GetType().Name, item.InternalId); return null; }
        }
    }
}
