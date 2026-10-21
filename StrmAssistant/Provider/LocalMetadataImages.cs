using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using StrmAssistant.Common;
using StrmAssistant.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace StrmAssistant.Provider
{
    public sealed class LocalMetadataImages : ILocalImageFileProvider
    {
        private readonly IFileSystem _files;
        public LocalMetadataImages(IFileSystem files) { _files = files; }
        public string Name => "Film Index Local Metadata";
        public bool Supports(BaseItem item) => LocalMetadataStore.RelativePath(item) != null;
        public List<LocalImageInfo> GetImages(BaseItem item, LibraryOptions libraryOptions, IDirectoryService directoryService, CancellationToken token)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var relative = LocalMetadataStore.RelativePath(item);
            var result = new List<LocalImageInfo>();
            if (relative == null || options.LocalMetadataMode == LocalMetadataMode.Off || !Path.IsPathFullyQualified(options.LocalMetadataRoot)) return result;
            var images = Path.Combine(options.LocalMetadataRoot, "override", Path.GetDirectoryName(relative), "images");
            var name = Path.GetFileNameWithoutExtension(relative);
            var mapping = item is Person ? new[] { ("profile", ImageType.Primary) } : item is Season || item is Episode
                ? new[] { (name, ImageType.Primary) }
                : new[] { ("poster", ImageType.Primary), ("fanart", ImageType.Backdrop), ("landscape", ImageType.Thumb),
                    ("clearlogo", ImageType.Logo), ("banner", ImageType.Banner), ("clearart", ImageType.Art), ("discart", ImageType.Disc) };
            foreach (var (stem, type) in mapping)
            {
                token.ThrowIfCancellationRequested();
                var file = new[] { ".jpg", ".png", ".webp", ".jpeg" }.Select(extension => Path.Combine(images, stem + extension))
                    .Select(path => _files.GetFileInfo(path)).FirstOrDefault(info => info.Exists);
                if (file != null) result.Add(new LocalImageInfo { Type = type, FileInfo = file });
            }
            return result;
        }
    }
}
