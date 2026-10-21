using System;
using MediaBrowser.Controller.Net;
using System.IO;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;
using StrmAssistant.Web.Helper;

namespace StrmAssistant.Web.Service
{
    [Unauthenticated]
    public class ShortcutMenuService : IService, IRequiresRequest
    {
        private readonly IHttpResultFactory _resultFactory;

        public ShortcutMenuService(IHttpResultFactory resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public IRequest Request { get; set; }

        public object Get(GetStrmAssistantJs request)
        {
            return _resultFactory.GetResult(Request,
                (ReadOnlyMemory<byte>)ShortcutMenuHelper.StrmAssistantJs.ToArray(), "application/x-javascript");
        }

        public object Get(GetShortcutMenu request)
        {
            return _resultFactory.GetResult(ShortcutMenuHelper.ModifiedShortcutsString.AsSpan(),
                "application/x-javascript");
        }
        private static readonly Lazy<string> NativeLists = new Lazy<string>(() => File.ReadAllText(Path.Combine(Plugin.Instance.ApplicationPaths.ProgramSystemPath,
            "dashboard-ui", "modules", "tabbedview", "listcontroller.js")));
        public object Get(GetListPresentation request)
        {
            var options = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions;
            return _resultFactory.GetResult(PresentationScript.Build(NativeLists.Value, options.SeasonViewStyle, options.EpisodeViewStyle).AsSpan(), "application/x-javascript");
        }
    }
}
