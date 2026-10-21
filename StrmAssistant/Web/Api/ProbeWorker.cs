using MediaBrowser.Controller.Net;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;

namespace StrmAssistant.Web.Api
{
    [Route("/FilmIndex/Worker/Probe", "POST")]
    [Authenticated(Roles = "Admin")]
    public sealed class ProbeWorker : IReturn<ProbeWorkerResult>
    {
        public Guid RequestId { get; set; }
        public string SourcePath { get; set; }
        public bool IsAudio { get; set; }
        public long? ExpectedSize { get; set; }
        public Dictionary<string, string> Headers { get; set; }
    }
    public sealed class ProbeWorkerResult
    {
        public int Schema { get; set; } = 1;
        public Guid RequestId { get; set; }
        public string ServerVersion { get; set; }
        public long? SourceSize { get; set; }
        public MediaInfo MediaInfo { get; set; }
    }
}
