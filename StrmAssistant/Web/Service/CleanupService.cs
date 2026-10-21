using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Web.Service
{
    public sealed class CleanupService : BaseApiService
    {
        private readonly ILibraryManager _library;
        private readonly IItemRepository _repository;
        private readonly IFileSystem _files;
        public CleanupService(ILibraryManager library, IItemRepository repository, IFileSystem files)
        { _library = library; _repository = repository; _files = files; }

        public async Task Post(CleanupItem request)
        {
            if (string.IsNullOrWhiteSpace(request.Id)) throw new ArgumentException("Item ID is required");
            var item = _library.GetItemById(request.Id) ?? throw new ArgumentException("Item was not found");
            var directory = new DirectoryService(Plugin.Instance.Logger, _files);
            switch (request.Action)
            {
                case CleanupAction.MediaInfo:
                    if (item is not Video && item is not Audio) throw new ArgumentException("A video or audio item is required");
                    _repository.SaveMediaStreams(item.InternalId, new List<MediaStream>(), CancellationToken.None);
                    item.RunTimeTicks = null; item.TotalBitrate = 0; item.Width = 0; item.Height = 0; item.Container = null;
                    if (request.DeleteSidecar) Plugin.MediaInfoApi.DeleteMediaInfoJson(item, directory, "Manual media-info cleanup");
                    break;
                case CleanupAction.Thumbnails:
                    if (item is not Video video) throw new ArgumentException("A video item is required");
                    var thumbnails = Video.GetLocalThumbnailSetInfos(video.Path, video.Id, false, directory)
                        .Concat(Video.GetLocalThumbnailSetInfos(video.Path, video.Id, true, directory))
                        .Concat(Video.GetMetadataFolderThumbnailSetInfos(video.Path, video.Id, directory))
                        .Select(info => info.Path).Distinct(StringComparer.Ordinal);
                    foreach (var path in thumbnails.Where(path => ArtifactRules.IsBifForVideo(video.Path, path))) DeleteArtifact(path, ".bif");
                    var chapters = _repository.GetChapters(video);
                    foreach (var chapter in chapters)
                    {
                        DeleteArtifact(chapter.ImagePath, ".jpg", ".jpeg", ".png", ".webp");
                        chapter.ImagePath = null; chapter.ImageTag = null;
                    }
                    _repository.SaveChapters(video.InternalId, chapters);
                    foreach (var image in video.ImageInfos.Where(image => image.Type == ImageType.Chapter).ToArray()) video.RemoveImage(image);
                    break;
                case CleanupAction.Person:
                    if (item is not Person) throw new ArgumentException("An individual person record is required");
                    _library.DeleteItem(item, new DeleteOptions { DeleteFileLocation = false });
                    Plugin.Instance.Logger.Info("FilmIndex: deleted person metadata record {0}", item.InternalId);
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(request.Action));
            }
            _library.UpdateItems(new List<BaseItem> { item }, null, ItemUpdateType.MetadataEdit, true, false, null, CancellationToken.None);
            ChangeNotificationMonitor.Publish(item, request.Action == CleanupAction.Thumbnails ? ItemUpdateType.ImageUpdate : ItemUpdateType.MetadataEdit);
            if (request.Action == CleanupAction.Thumbnails && Plugin.LibraryApi.HasMediaInfo(item) &&
                Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.PersistMediaInfoMode == "Default")
                await Plugin.MediaInfoApi.SerializeMediaInfo(item.InternalId, directory, true, "Thumbnail cleanup").ConfigureAwait(false);
            Plugin.Instance.Logger.Info("FilmIndex: completed {0} cleanup for item {1}", request.Action, item.InternalId);
        }

        private static void DeleteArtifact(string path, params string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
                !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || !File.Exists(path)) return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
            File.Delete(path);
        }
    }
}
