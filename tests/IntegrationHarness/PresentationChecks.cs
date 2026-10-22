using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Users;
using StrmAssistant.Options;

namespace FilmIndex.IntegrationTests;

internal static class PresentationChecks
{
    public static async Task Run(string root, ILibraryManager library, IUserManager users, IUserDataManager data,
        IDtoService dto, ICollectionManager collections, PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        var folder = Path.Combine(root, "presentation"); Directory.CreateDirectory(folder);
        var tv = library.AddVirtualFolder("Presentation fixtures", "tvshows", new LibraryOptions
            { EnableRealtimeMonitor = false, PathInfos = new[] { new MediaPathInfo { Path = folder } } }, false);
        var seriesPath = Path.Combine(folder, "Fixture Show"); Directory.CreateDirectory(seriesPath);
        var series = new Series { Id = Guid.NewGuid(), Name = "Presentation Fixture", Path = seriesPath };
        var physical = tv.GetPhysicalFolderItems(new MediaBrowser.Controller.Entities.InternalItemsQuery()).First();
        series.SetParent(physical); library.CreateItem(series, physical);
        var seasons = new List<Season>(); var episodes = new List<Episode>();
        for (var number = 1; number <= 2; number++)
        {
            var path = Path.Combine(seriesPath, "Season " + number); Directory.CreateDirectory(path);
            var season = new Season { Id = Guid.NewGuid(), Name = "Season " + number, Path = path, IndexNumber = number };
            season.SetParent(series); library.CreateItem(season, series); seasons.Add(season);
            for (var ep = 1; ep <= 2; ep++)
            {
                var target = Path.Combine(path, $"Fixture.S0{number}E0{ep}.mp4"); File.Copy(Path.Combine(root,"movies","fixture.mp4"), target);
                var episode = new Episode { Id = Guid.NewGuid(), Name = "Episode " + ep, Path = target,
                    ParentIndexNumber = number, IndexNumber = ep, RunTimeTicks = TimeSpan.FromSeconds(3).Ticks };
                episode.SetParent(season); episode.SetSeriesItem(series); library.CreateItem(episode, season); episodes.Add(episode);
            }
        }
        var user = await users.CreateUser("film-index-presentation-fixture", new UserPolicy { EnableAllFolders = true, EnableAllDevices = true });
        var restricted = await users.CreateUser("film-index-presentation-restricted", new UserPolicy { EnableAllFolders = false, EnabledFolders = Array.Empty<string>(), EnableAllDevices = true });
        try
        {
            options.ExperienceEnhanceOptions.DescendingEpisodes = true; options.ExperienceEnhanceOptions.DescendingSeasons = true;
            options.ExperienceEnhanceOptions.ShowTotalEpisodes = true; options.ExperienceEnhanceOptions.ShowLibraryItemCounts = true;
            options.ExperienceEnhanceOptions.IncludeSeasonCollections = true; options.ExperienceEnhanceOptions.HideSearchRecommendations = true; save(options);
            var query = new InternalItemsQuery { User = user, ParentIds = new[] { series.InternalId }, Recursive = false,
                IncludeItemTypes = new[] { nameof(Season) } };
            var orderedSeasons = library.GetItemList(query);
            check(orderedSeasons.Length == 2 && orderedSeasons[0].IndexNumber == 2 && (query.OrderBy == null || query.OrderBy.Length == 0),
                "Native season queries reverse the display order without mutating caller preferences");
            var orderedEpisodes = library.GetItemList(new InternalItemsQuery { User = user, ParentIds = new[] { seasons[0].InternalId }, Recursive = false,
                IncludeItemTypes = new[] { nameof(Episode) } });
            check(orderedEpisodes.Length == 2 && orderedEpisodes[0].IndexNumber == 2, "Native episode display queries support descending episode order");
            var ascending = library.GetItemList(new InternalItemsQuery { User = user, ParentIds = new[] { seasons[0].InternalId },
                IncludeItemTypes = new[] { nameof(Episode) }, OrderBy = new[] { ("IndexNumber", SortOrder.Ascending) } });
            check(ascending.Length == 2 && ascending[0].IndexNumber == 1, "Explicit ascending episode order remains authoritative");
            var recent = new InternalItemsQuery { User = user, WasSearched = true, Recursive = true };
            check(library.GetItemList(recent).Length == 0 && (recent.ItemIds == null || recent.ItemIds.Length == 0), "Search-history suppression preserves the original query object");
            var value = data.GetUserData(user, episodes[0]); value.Played = true; data.SaveUserData(user, episodes[0], value, UserDataSaveReason.TogglePlayed, CancellationToken.None);
            var scopeBefore = options.MediaInfoExtractOptions.LibraryScope;
            value.IsFavorite = true; data.SaveUserData(user, episodes[0], value, UserDataSaveReason.TogglePlayed, CancellationToken.None);
            try
            {
                options.MediaInfoExtractOptions.LibraryScope = "-1"; save(options);
                StrmAssistant.Common.LibraryApi.AllUsers[user] = false;
                check(StrmAssistant.Plugin.LibraryApi.FetchPreExtractTaskItems().Any(item => item.InternalId == episodes[0].InternalId),
                    "Favorites extraction discovers episodes nested beneath library, series and season folders");
            }
            finally
            {
                StrmAssistant.Common.LibraryApi.AllUsers.Remove(user);
                options.MediaInfoExtractOptions.LibraryScope = scopeBefore; save(options);
            }
            var dtoOptions = new DtoOptions { EnableUserData = true, EnableImages = false };
            var card = dto.GetBaseItemDto(series, dtoOptions, user);
            check(card.UserData.UnplayedItemCount == 4 && data.GetUserData(user, episodes[0]).Played,
                "Series cards show the total episode count while preserving native played state");
            var seasonCard = dto.GetBaseItemDto(seasons[0], dtoOptions, user);
            check(seasonCard.UserData.UnplayedItemCount == 2, "Season cards show their own total episode count");
            var libraryCard = dto.GetBaseItemDto(tv, dtoOptions, user);
            check(libraryCard.ChildCount == 1, "Library badges count shows rather than individual episodes: " + libraryCard.ChildCount + ";badge=" + libraryCard.UserData?.UnplayedItemCount);
            var blockedCard = dto.GetBaseItemDto(tv, dtoOptions, restricted);
            check(blockedCard.ChildCount == 0, "Library badge counts respect the requesting user's access restrictions");
            var collection = await collections.CreateCollection(new CollectionCreationOptions { Name = "Season-only fixture collection", ItemIdList = new[] { seasons[1].InternalId } });
            card = dto.GetBaseItemDto(series, dtoOptions, user);
            check(card.Collections?.Any(entry => entry.Id == collection.InternalId) == true, "Series presentation includes a collection linked only to one of its seasons");
        }
        finally
        {
            options.ExperienceEnhanceOptions.DescendingEpisodes = false; options.ExperienceEnhanceOptions.DescendingSeasons = false;
            options.ExperienceEnhanceOptions.ShowTotalEpisodes = false; options.ExperienceEnhanceOptions.ShowLibraryItemCounts = false;
            options.ExperienceEnhanceOptions.IncludeSeasonCollections = false; options.ExperienceEnhanceOptions.HideSearchRecommendations = false; save(options);
            await users.DeleteUser(user); await users.DeleteUser(restricted);
        }
    }
}
