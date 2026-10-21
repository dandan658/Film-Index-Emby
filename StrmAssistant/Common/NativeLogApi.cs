using HarmonyLib;
using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Sanitation;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public sealed class NativeLogApi : IServerEntryPoint, IDisposable
    {
        private readonly ISanitationManager _sanitizer;
        private Harmony _patch;
        private static NativeLogApi _instance;
        public NativeLogApi(ISanitationManager sanitizer) { _sanitizer = sanitizer; }
        public void Run()
        {
            _instance = this;
            try
            {
                _patch = new Harmony("film-index.emby.logs");
                var service = Assembly.Load("Emby.Api").GetType("Emby.Api.System.SystemService");
                var method = service?.GetMethods().FirstOrDefault(method => method.Name == "Get" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.Name == "GetLogLines");
                if (method != null) _patch.Patch(method, prefix: new HarmonyMethod(typeof(NativeLogApi), nameof(Before)));
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex log adapter unavailable: " + error.GetType().Name); }
        }
        public static bool Before(BaseApiService __instance, object __0, ref Task<object> __result)
        {
            if (_instance == null || !Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.NewestLogsFirst) return true;
            __result = _instance.Read(__instance, __0); return false;
        }
        private async Task<object> Read(BaseApiService service, object request)
        {
            var type = request.GetType();
            T Value<T>(string property) => (T)type.GetProperty(property).GetValue(request);
            var name = Value<string>("Name");
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name) throw new ArgumentException("Invalid log filename");
            var folder = Plugin.Instance.ApplicationPaths.LogDirectoryPath;
            var path = Directory.EnumerateFiles(folder).First(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase));
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sanitized = Value<bool>("Sanitize") ? _sanitizer.SanitizeStream(input) : null;
            return await ReverseLogReader.Read(sanitized ?? input, Value<int>("StartIndex"), Value<int>("Limit"), Value<bool>("EnableTotalRecordCount"), service.Request.CancellationToken).ConfigureAwait(false);
        }
        public void Dispose() { _patch?.UnpatchAll(_patch.Id); if (ReferenceEquals(_instance, this)) _instance = null; }
    }
}
