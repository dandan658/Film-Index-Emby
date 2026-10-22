using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;
using System;
using System.IO;
using SecurityException = MediaBrowser.Controller.Net.SecurityException;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Web.Service
{
    public sealed class ProbeWorkerService : BaseApiService
    {
        private static readonly SemaphoreSlim Slots = new SemaphoreSlim(2, 2);
        private readonly IMediaProbeManager _probe;
        public ProbeWorkerService(IMediaProbeManager probe) { _probe = probe; }
        public async Task<object> Post(ProbeWorker request)
        {
            var options = Plugin.Instance.GetPluginOptions().DistributedOptions;
            if (!options.ServeWorker) throw new SecurityException("Worker mode is disabled");
            if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.SourcePath)) throw new ArgumentException("A request ID and source are required");
            var source = request.SourcePath;
            var protocol = MediaProtocol.File;
            long? size = null;
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                if (!ProxyRules.HostMatches(uri.DnsSafeHost, options.AllowedSourceHosts)) throw new SecurityException("Remote source host is not permitted");
                protocol = MediaProtocol.Http;
            }
            else
            {
                if (!DistributedRules.AllowsFile(source, options.AllowedRoots)) throw new SecurityException("Source is outside worker roots");
                source = Path.GetFullPath(source);
                size = new FileInfo(source).Length;
                if (request.ExpectedSize.HasValue && size != request.ExpectedSize) throw new InvalidOperationException("Source file size does not match the master");
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await Slots.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                var result = await _probe.GetMediaInfo(new MediaInfoRequest
                {
                    ExtractChapters = true, MediaType = request.IsAudio ? DlnaProfileType.Audio : DlnaProfileType.Video,
                    MediaSource = new MediaSourceInfo { Path = source, Protocol = protocol, RequiredHttpHeaders = request.Headers }
                }, deadline.Token).ConfigureAwait(false);
                if (result?.MediaStreams == null || result.MediaStreams.Count == 0) throw new InvalidOperationException("Worker returned no media streams");
                return new ProbeWorkerResult { RequestId = request.RequestId, MediaInfo = result, SourceSize = size,
                    ServerVersion = Plugin.Instance.ApplicationHost.ApplicationVersion.ToString() };
            }
            finally { Slots.Release(); }
        }
    }
}

