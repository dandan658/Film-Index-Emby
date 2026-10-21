using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    [Route("/{Web}/modules/tabbedview/listcontroller.js", "GET", IsHidden = true)]
    [Unauthenticated]
    public class GetListPresentation { public string Web { get; set; } }
}
