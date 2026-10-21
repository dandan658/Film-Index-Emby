using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    internal sealed class ImageCaptureApi
    {
        private readonly IFfmpegManager _encoder;
        private readonly IProviderManager _providers;
        private readonly ILibraryManager _library;
        public ImageCaptureApi(IFfmpegManager encoder, IProviderManager providers, ILibraryManager library)
        { _encoder = encoder; _providers = providers; _library = library; }

        public async Task CaptureIfMissing(Video video, IDirectoryService directory, CancellationToken token)
        {
            var options = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions;
            if (!options.EnableImageCapture || video.HasImage(ImageType.Primary) || ExtractionPolicy.IsExcluded(video)) return;
            var input = video.IsShortcut ? await Plugin.LibraryApi.GetStrmMountPath(video.Path).ConfigureAwait(false) : video.Path;
            if (string.IsNullOrEmpty(input)) return;
            var encoder = string.IsNullOrWhiteSpace(options.CustomFfmpegPath) ?
                _encoder.FfmpegConfiguration.EncoderPath : options.CustomFfmpegPath;
            Directory.CreateDirectory(Plugin.Instance.ApplicationPaths.TempDirectory);
            var output = Path.Combine(Plugin.Instance.ApplicationPaths.TempDirectory, "film-index-capture-" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                var seconds = Math.Max(0, video.RunTimeTicks.GetValueOrDefault() / (double)TimeSpan.TicksPerSecond * options.ImageCapturePosition / 100);
                var discSource = DiscProbeApi.Enabled ? DiscProbeApi.Source(input) : null;
                var disc = discSource == null ? null : DiscPlaylist.Read(discSource, token);
                var portrait = video.Width > 0 && video.Height > video.Width;
                var success = disc == null ? await CaptureRunner.Capture(encoder, input, output, seconds,
                    portrait, TimeSpan.FromSeconds(30), token).ConfigureAwait(false) :
                    await CaptureRunner.CaptureDisc(encoder, disc.Source, disc.Number, output, seconds,
                        portrait, TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                if (!success) { Plugin.Instance.Logger.Warn("FilmIndex: image capture failed or timed out for item {0}", video.InternalId); return; }
                await _providers.SaveImage(video, _library.GetLibraryOptions(video), output, ImageType.Primary,
                    null, Array.Empty<long>(), directory, false, token).ConfigureAwait(false);
                _library.UpdateItems(new List<BaseItem> { video }, null, ItemUpdateType.ImageUpdate,
                    false, false, null, token);
                ChangeNotificationMonitor.Publish(video, ItemUpdateType.ImageUpdate);
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }
    }
}
