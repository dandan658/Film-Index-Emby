using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using StrmAssistant;
using StrmAssistant.Common;

namespace FilmIndex.IntegrationTests;

internal static class SubtitleChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var pointer = Path.Combine(root, "fixture.strm");
        var video = Path.Combine(root, "movies", "fixture.mp4");
        await File.WriteAllTextAsync(pointer, video);
        var resolved = await Plugin.LibraryApi.GetStrmMountPath(pointer);
        check(Path.GetFullPath(resolved) == Path.GetFullPath(video), "Native STRM mounting resolves the referenced video rather than its pointer text");
        var request = new SubtitleSearchRequest { MediaPath = pointer, ContentType = VideoContentType.Movie,
            IsPerfectMatch = true, IsHearingImpaired = false, Language = "chi", ProductionYear = 2024 };
        request.SetProviderId(MetadataProviders.Imdb, "tt0903747");
        var local = StrmSubtitleApi.Prepare(request, resolved);
        check(local.MediaPath == video && local.IsPerfectMatch && request.MediaPath == pointer,
            "Local STRM subtitles hash the referenced video and preserve the caller request");
        var remote = StrmSubtitleApi.Prepare(request, "https://example.invalid/video.mp4");
        check(!remote.IsPerfectMatch && remote.GetProviderId(MetadataProviders.Imdb) == "tt0903747" && remote.IsHearingImpaired == false,
            "Remote STRM subtitle matching retains identity, language and accessibility filters without hashing the pointer");
        var episode = new SubtitleSearchRequest { ContentType = VideoContentType.Episode, ParentIndexNumber = 2, IndexNumber = 3 };
        episode.SeriesProviderIds["Imdb"] = "tt0903747";
        check(StrmSubtitleApi.HasIdentity(episode), "TV subtitle matching requires the series IMDb identity and explicit season and episode");
        episode.IndexNumberEnd = 4;
        check(!StrmSubtitleApi.HasIdentity(episode), "Multi-episode STRM files are not silently matched to a single episode subtitle");
    }
}
