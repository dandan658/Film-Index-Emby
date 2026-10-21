using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Users;
using StrmAssistant;
using StrmAssistant.Options;
using System.Reflection;

namespace FilmIndex.IntegrationTests;

internal static class CompletionChecks
{
    public static async Task Run(string root, ILibraryManager library, IUserManager users, IUserDataManager data,
        PluginOptions options, Action<PluginOptions> save, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "television");
        Directory.CreateDirectory(folder);
        var tv = library.AddVirtualFolder("FilmIndex Test TV", "tvshows", new LibraryOptions
        { EnableRealtimeMonitor = false, PathInfos = new[] { new MediaPathInfo { Path = folder } } }, false);
        var seriesPath = Path.Combine(folder, "Fixture Series"); Directory.CreateDirectory(seriesPath);
        var series = new Series { Id = Guid.NewGuid(), Name = "Fixture Series", Path = seriesPath };
        library.CreateItem(series, tv);
        var seasonPath = Path.Combine(seriesPath, "Season 1"); Directory.CreateDirectory(seasonPath);
        var season = new Season { Id = Guid.NewGuid(), Name = "Season 1", IndexNumber = 1, Path = seasonPath };
        library.CreateItem(season, series);
        var episodes = new List<Episode>();
        for (var index = 1; index <= 4; index++)
        {
            var path = Path.Combine(seasonPath, "fixture-S01E0" + index + ".mp4");
            File.Copy(Path.Combine(root, "movies", "fixture.mp4"), path);
            var episode = new Episode { Id = Guid.NewGuid(), Name = "Episode " + index, Path = path,
                ParentIndexNumber = 1, IndexNumber = index, RunTimeTicks = TimeSpan.FromSeconds(3).Ticks };
            episode.SetSeriesItem(series);
            library.CreateItem(episode, season);
            episodes.Add(episode);
        }
        var first = await users.CreateUser("film-index-playback-fixture", new UserPolicy { EnableAllFolders = true, EnableAllDevices = true });
        var second = await users.CreateUser("film-index-second-fixture", new UserPolicy { EnableAllFolders = true, EnableAllDevices = true });
        try
        {
            foreach (var episode in new[] { episodes[0], episodes[3] })
            {
                var value = data.GetUserData(first, episode);
                value.PlaybackPositionTicks = TimeSpan.FromSeconds(1).Ticks; value.Played = false; value.IsFavorite = true;
                data.SaveUserData(first, episode, value, UserDataSaveReason.PlaybackProgress, CancellationToken.None);
            }
            var other = data.GetUserData(second, episodes[0]);
            other.PlaybackPositionTicks = TimeSpan.FromSeconds(1).Ticks;
            data.SaveUserData(second, episodes[0], other, UserDataSaveReason.PlaybackProgress, CancellationToken.None);
            options.ExperienceEnhanceOptions.MarkPreviousInProgressPlayed = true;
            save(options);
            var monitor = typeof(Plugin).GetField("_completionMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Plugin.Instance)!;
            var callback = monitor.GetType().GetMethod("Stopped", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var args = new PlaybackStopEventArgs { Item = episodes[2], PlayedToCompletion = false,
                Session = new SessionInfo { UserInternalId = first.InternalId }, PlaybackPositionTicks = TimeSpan.FromSeconds(3).Ticks };
            callback.Invoke(monitor, new object?[] { null, args });
            check(!data.GetUserData(first, episodes[0]).Played, "Partial playback does not mark earlier episodes played");
            args.PlayedToCompletion = true;
            callback.Invoke(monitor, new object?[] { null, args });
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var busy = monitor.GetType().GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
                var count = (int)busy.GetType().GetProperty("Count")!.GetValue(busy)!;
                if (count == 0) break;
                await Task.Delay(50);
            }
            var completed = data.GetUserData(first, episodes[0]);
            check(completed.Played && completed.PlaybackPositionTicks == 0 && completed.IsFavorite, "Completion updates earlier progress and preserves its favorite flag");
            check(!data.GetUserData(first, episodes[1]).Played, "Completion does not mark never-started episodes played");
            check(!data.GetUserData(first, episodes[3]).Played && data.GetUserData(first, episodes[3]).PlaybackPositionTicks > 0, "Completion preserves future episode progress");
            check(!data.GetUserData(second, episodes[0]).Played && data.GetUserData(second, episodes[0]).PlaybackPositionTicks > 0, "Completion does not alter another user's playback progress");
        }
        finally
        {
            options.ExperienceEnhanceOptions.MarkPreviousInProgressPlayed = false; save(options);
            await users.DeleteUser(first); await users.DeleteUser(second);
        }
    }
}
