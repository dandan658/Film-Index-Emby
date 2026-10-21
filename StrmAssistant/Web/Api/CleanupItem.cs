using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    public enum CleanupAction { MediaInfo, Thumbnails, Person }
    [Route("/FilmIndex/Items/{Id}/Cleanup", "POST")]
    [Authenticated(Roles = "Admin")]
    public sealed class CleanupItem : IReturnVoid
    {
        public string Id { get; set; }
        public CleanupAction Action { get; set; }
        public bool DeleteSidecar { get; set; } = false;
    }
}
