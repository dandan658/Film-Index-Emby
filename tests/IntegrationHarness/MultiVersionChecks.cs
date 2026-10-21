using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Users;
using StrmAssistant.Common;
using StrmAssistant.Options;
using System.Reflection;

namespace FilmIndex.IntegrationTests;

internal static class MultiVersionChecks
{
    public static async Task Run(string root, ILibraryManager library, IUserManager users, IUserDataManager data, IFileSystem files,
        PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        var libraries = new List<MediaBrowser.Controller.Entities.CollectionFolder>();
        var movies = new List<Movie>();
        for (var index = 0; index < 2; index++)
        {
            var folder = Path.Combine(root, "versions-" + index); Directory.CreateDirectory(folder);
            var collection = library.AddVirtualFolder("Version fixture " + index, "movies", new LibraryOptions
                { EnableRealtimeMonitor = false, PathInfos = new[] { new MediaPathInfo { Path = folder } } }, false);
            libraries.Add(collection);
            for (var version = 0; version < (index == 0 ? 2 : 1); version++)
            {
                var path = Path.Combine(folder, "fixture-" + version + ".mp4"); File.Copy(Path.Combine(root, "movies", "fixture.mp4"), path);
                var movie = new Movie { Id = Guid.NewGuid(), Name = "Version fixture", Path = path, RunTimeTicks = TimeSpan.FromSeconds(3).Ticks };
                movie.SetParent(collection); movie.SetProviderId(MetadataProviders.Tmdb, "999999999"); library.CreateItem(movie, collection); movies.Add(movie);
            }
        }
        var user = await users.CreateUser("film-index-version-fixture", new UserPolicy { EnableAllFolders = true, EnableAllDevices = true });
        try
        {
            var legacy = data.GetUserData(user, movies[0]); legacy.PlaybackPositionTicks = 1000000; legacy.IsFavorite = true;
            data.SaveUserData(user, movies[0], legacy, UserDataSaveReason.PlaybackProgress, CancellationToken.None);
            check(data.GetUserData(user, movies[2]).PlaybackPositionTicks == 1000000, "Baseline versions share native provider-based playback progress");
            options.ExperienceEnhanceOptions.SeparateLibraryProgress = true; save(options);
            foreach (var movie in movies) library.UpdateItem(movie, movie.Parent, ItemUpdateType.MetadataImport);
            var first = data.GetUserData(user, movies[0]); first.PlaybackPositionTicks = 2000000; first.IsFavorite = true;
            data.SaveUserData(user, movies[0], first, UserDataSaveReason.PlaybackProgress, CancellationToken.None);
            check(data.GetUserData(user, movies[1]).PlaybackPositionTicks == 2000000, "Versions within one library keep shared playback progress");
            check(data.GetUserData(user, movies[2]).PlaybackPositionTicks == 0 && !data.GetUserData(user, movies[2]).IsFavorite,
                "Versions in different libraries have separate playback progress and favorites");
            options.ExperienceEnhanceOptions.SeparateLibraryProgress = false; save(options);
            foreach (var movie in movies) library.UpdateItem(movie, movie.Parent, ItemUpdateType.MetadataImport);
            check(data.GetUserData(user, movies[0]).PlaybackPositionTicks == 1000000 && data.GetUserData(user, movies[2]).IsFavorite,
                "Disabling library isolation restores the original native progress and favorites");

            options.ExperienceEnhanceOptions.SortVersionsByQuality = true;
            options.ExperienceEnhanceOptions.VersionLabels = VersionLabelMode.FileName; save(options);
            var low = new MediaSourceInfo { Id = "low", Path = "/media/low-version.mp4", Name = "original low", Bitrate = 500000,
                MediaStreams = new List<MediaStream> { new MediaStream { Type = MediaStreamType.Video, Width = 1280, Height = 720 } } };
            var high = new MediaSourceInfo { Id = "high", Path = "/media/high-version.mp4", Name = "original high", Bitrate = 10000000,
                MediaStreams = new List<MediaStream> { new MediaStream { Type = MediaStreamType.Video, Width = 3840, Height = 2160 } } };
            var sources = new List<MediaSourceInfo> { low, high }; MultiVersionApi.Sources(ref sources);
            check(sources[0].Id == "high" && sources[0].Name == "high-version" && high.Name == "original high",
                "Version presentation sorts by quality without mutating native cached media sources");
            options.ExperienceEnhanceOptions.MaximumFolderVersions = 16; save(options);
            var naming = Assembly.Load("Emby.Naming");
            var resolver = naming.GetType("Emby.Naming.Video.VideoListResolver")!;
            var namingOptions = Activator.CreateInstance(naming.GetType("Emby.Naming.Common.NamingOptions")!);
            var instance = Activator.CreateInstance(resolver, new object[] { namingOptions!, files, StringComparer.OrdinalIgnoreCase });
            var versionFolder = Path.Combine(root, "FilmFixture"); Directory.CreateDirectory(versionFolder);
            var paths = Enumerable.Range(0,10).Select(index => {
                var path = Path.Combine(versionFolder, "FilmFixture - Version" + index + ".mp4");
                File.Copy(Path.Combine(root, "movies", "fixture.mp4"), path);
                return files.GetFileInfo(path);
            }).ToList();
            var resolved = (System.Collections.IList)resolver.GetMethod("Resolve")!.Invoke(instance, new object[] { paths, true, true, false })!;
            check(resolved.Count == 1, "Native 4.10 folder resolver groups ten versions when the configured limit is sixteen");
            options.ExperienceEnhanceOptions.MaximumFolderVersions = 8; save(options);
            resolved = (System.Collections.IList)resolver.GetMethod("Resolve")!.Invoke(instance, new object[] { paths, true, true, false })!;
            check(resolved.Count == 10, "Restoring the default version limit restores native grouping behavior");
        }
        finally
        {
            options.ExperienceEnhanceOptions.SeparateLibraryProgress = false;
            options.ExperienceEnhanceOptions.SortVersionsByQuality = false;
            options.ExperienceEnhanceOptions.VersionLabels = VersionLabelMode.Native;
            options.ExperienceEnhanceOptions.MaximumFolderVersions = 8; save(options);
            await users.DeleteUser(user);
        }
    }
}





