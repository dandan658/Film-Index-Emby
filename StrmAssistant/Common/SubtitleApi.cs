using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Dlna;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public class SubtitleApi
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly IFileSystem _fileSystem;
        private readonly IMediaProbeManager _mediaProbeManager;
        private readonly object _audioResolver;
        private readonly MethodInfo _getExternalAudioTracks;

        private readonly object _subtitleResolver;
        private readonly MethodInfo _getExternalSubtitleStreams;
        private readonly object _ffProbeSubtitleInfo;
        private readonly MethodInfo _updateExternalSubtitleStream;

        private static readonly HashSet<string> ProbeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".sub", ".smi", ".sami", ".mpl" };

        public SubtitleApi(ILibraryManager libraryManager, IFileSystem fileSystem, IMediaProbeManager mediaProbeManager,
            ILocalizationManager localizationManager, IItemRepository itemRepository)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _fileSystem = fileSystem;
            _mediaProbeManager = mediaProbeManager;

            try
            {
                var embyProviders = Assembly.Load("Emby.Providers");
                var subtitleResolverType = embyProviders.GetType("Emby.Providers.MediaInfo.SubtitleResolver");
                var subtitleResolverConstructor = subtitleResolverType.GetConstructor(new[]
                {
                    typeof(ILocalizationManager), typeof(IFileSystem), typeof(ILibraryManager)
                });
                _subtitleResolver = subtitleResolverConstructor?.Invoke(new object[]
                {
                    localizationManager, fileSystem, libraryManager
                });
                _getExternalSubtitleStreams = subtitleResolverType.GetMethod("GetExternalTracks");
                var audioResolverType = embyProviders.GetType("Emby.Providers.MediaInfo.AudioTrackResolver", true);
                _audioResolver = Activator.CreateInstance(audioResolverType, localizationManager, fileSystem, libraryManager);
                _getExternalAudioTracks = audioResolverType.GetMethod("GetExternalTracks");

                var ffProbeSubtitleInfoType = embyProviders.GetType("Emby.Providers.MediaInfo.FFProbeSubtitleInfo");
                var ffProbeSubtitleInfoConstructor = ffProbeSubtitleInfoType.GetConstructor(new[]
                {
                    typeof(IMediaProbeManager)
                });
                _ffProbeSubtitleInfo = ffProbeSubtitleInfoConstructor?.Invoke(new object[] { mediaProbeManager });
                _updateExternalSubtitleStream = ffProbeSubtitleInfoType.GetMethod("UpdateExternalSubtitleStream");
            }
            catch (Exception e)
            {
                if (Plugin.Instance.DebugMode)
                {
                    _logger.Debug(e.Message);
                    _logger.Debug(e.StackTrace);
                }
            }

            if (_subtitleResolver is null || _getExternalSubtitleStreams is null ||
                _ffProbeSubtitleInfo is null || _updateExternalSubtitleStream is null)
            {
                _logger.Warn($"{nameof(SubtitleApi)} Init Failed");
            }
        }

        private List<MediaStream> GetExternalSubtitleStreams(BaseItem item, int startIndex,
            IDirectoryService directoryService, bool clearCache)
        {
            var namingOptions = _libraryManager.GetNamingOptions();

            return (List<MediaStream>)_getExternalSubtitleStreams.Invoke(_subtitleResolver,
                new object[] { item, startIndex, directoryService, _libraryManager.GetLibraryOptions(item), namingOptions, clearCache });
        }

        private Task<bool> UpdateExternalSubtitleStream(BaseItem item,
            MediaStream subtitleStream, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);

            return (Task<bool>)_updateExternalSubtitleStream.Invoke(_ffProbeSubtitleInfo,
                new object[] { item, subtitleStream, options, libraryOptions, cancellationToken });
        }

        private List<MediaStream> GetExternalAudioTracks(BaseItem item, int startIndex,
            IDirectoryService directoryService, bool clearCache)
        {
            if (_audioResolver == null || _getExternalAudioTracks == null)
                throw new NotSupportedException("External audio resolver is unavailable in this server build.");
            return (List<MediaStream>)_getExternalAudioTracks.Invoke(_audioResolver,
                new object[] { item, startIndex, directoryService, _libraryManager.GetLibraryOptions(item),
                    _libraryManager.GetNamingOptions(), clearCache });
        }

        public MetadataRefreshOptions GetExternalSubtitleRefreshOptions()
        {
            return new MetadataRefreshOptions(new DirectoryService(_logger, _fileSystem))
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllMetadata = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false
            };
        }

        public bool HasExternalSubtitleChanged(BaseItem item, IDirectoryService directoryService, bool clearCache)
        {
            var includeAudio = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.ScanExternalAudioTracks;
            var currentExternalSubtitleFiles = item.GetMediaStreams()
                .Where(stream => (stream.Type == MediaStreamType.Subtitle || includeAudio && stream.Type == MediaStreamType.Audio) && stream.IsExternal)
                .Select(stream => stream.Path).Where(path => !string.IsNullOrEmpty(path));
            var currentSet = new HashSet<string>(currentExternalSubtitleFiles, StringComparer.Ordinal);

            try
            {
                var newExternalSubtitleFiles = GetExternalSubtitleStreams(item, 0, directoryService, clearCache)
                    .Select(i => i.Path)
                    .ToArray();
                if (includeAudio) newExternalSubtitleFiles = newExternalSubtitleFiles
                    .Concat(GetExternalAudioTracks(item, 0, directoryService, clearCache).Select(i => i.Path)).ToArray();
                var newSet = new HashSet<string>(newExternalSubtitleFiles, StringComparer.Ordinal);

                return !currentSet.SetEquals(newSet);
            }
            catch
            {
                // ignored
            }

            return false;
        }

        public async Task UpdateExternalSubtitles(BaseItem item, MetadataRefreshOptions refreshOptions, bool clearCache,
            bool persistMediaInfo)
        {
            var directoryService = refreshOptions.DirectoryService;
            var includeAudio = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.ScanExternalAudioTracks;
            var currentStreams = item.GetMediaStreams()
                .FindAll(i =>
                    !(i.IsExternal && (i.Type == MediaStreamType.Subtitle || includeAudio && i.Type == MediaStreamType.Audio) && i.Protocol == MediaProtocol.File));
            var startIndex = currentStreams.Count == 0 ? 0 : currentStreams.Max(i => i.Index) + 1;

            if (GetExternalSubtitleStreams(item, startIndex, directoryService, clearCache) is
                { } externalSubtitleStreams)
            {
                foreach (var subtitleStream in externalSubtitleStreams)
                {
                    var extension = Path.GetExtension(subtitleStream.Path);
                    if (!string.IsNullOrEmpty(extension) && ProbeExtensions.Contains(extension))
                    {
                        var result =
                            await UpdateExternalSubtitleStream(item, subtitleStream, refreshOptions,
                                CancellationToken.None).ConfigureAwait(false);

                        if (!result)
                            _logger.Warn("No result when probing external subtitle file: {0}", subtitleStream.Path);
                    }

                    _logger.Info("ExternalSubtitle - Subtitle Processed: " + subtitleStream.Path);
                }

                currentStreams.AddRange(externalSubtitleStreams);
                if (includeAudio)
                {
                    startIndex = currentStreams.Count == 0 ? 0 : currentStreams.Max(i => i.Index) + 1;
                    var audioTracks = GetExternalAudioTracks(item, startIndex, directoryService, clearCache);
                    foreach (var audioTrack in audioTracks)
                    {
                        var info = await _mediaProbeManager.GetMediaInfo(new MediaInfoRequest
                        {
                            MediaType = DlnaProfileType.Audio,
                            ExtractChapters = false,
                            MediaSource = new MediaSourceInfo { Path = audioTrack.Path, Protocol = MediaProtocol.File }
                        }, CancellationToken.None).ConfigureAwait(false);
                        var probed = info?.MediaStreams?.FirstOrDefault(stream => stream.Type == MediaStreamType.Audio);
                        if (probed == null) throw new InvalidOperationException("External audio probe returned no audio stream; existing streams were preserved.");
                        probed.IsExternal = true;
                        probed.Path = audioTrack.Path;
                        probed.Protocol = MediaProtocol.File;
                        probed.Index = startIndex++;
                        if (string.IsNullOrEmpty(probed.Language)) probed.Language = audioTrack.Language;
                        currentStreams.Add(probed);
                        _logger.Info("ExternalAudio - Audio Processed: " + audioTrack.Path);
                    }
                }
                _itemRepository.SaveMediaStreams(item.InternalId, currentStreams, CancellationToken.None);

                if (persistMediaInfo && Plugin.LibraryApi.IsLibraryInScope(item))
                {
                    _ = Plugin.MediaInfoApi.SerializeMediaInfo(item.InternalId, directoryService, true,
                        "External Subtitle Update").ConfigureAwait(false);
                }
            }
        }
    }
}
