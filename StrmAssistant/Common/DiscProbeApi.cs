using Emby.Media.Model.ProbeModel;
using Emby.Server.MediaEncoding.Probing;
using HarmonyLib;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public sealed class DiscProbeApi : IServerEntryPoint, IDisposable
    {
        private readonly IMediaProbeManager _probe;
        private readonly IFfmpegManager _encoder;
        private readonly IJsonSerializer _json;
        private readonly IFileSystem _files;
        private readonly ILocalizationManager _localization;
        private readonly IStreamInfoManager _streamInfo;
        private static DiscProbeApi _instance;
        private Harmony _patch;
        private static readonly SemaphoreSlim Slot = new(1, 1);
        public static bool Enabled => _instance != null && Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.EnableDiscProbe;
        public DiscProbeApi(IMediaProbeManager probe, IFfmpegManager encoder, IJsonSerializer json, IFileSystem files, ILocalizationManager localization, IStreamInfoManager streamInfo)
        { _probe = probe; _encoder = encoder; _json = json; _files = files; _localization = localization; _streamInfo = streamInfo; }
        public void Run()
        {
            try
            {
                _patch = new Harmony("film-index.emby.disc-probe");
                var map = _probe.GetType().GetInterfaceMap(typeof(IMediaProbeManager));
                var method = map.TargetMethods[Array.FindIndex(map.InterfaceMethods, method => method.Name == "GetMediaInfo")];
                _patch.Patch(method, prefix: new HarmonyMethod(typeof(DiscProbeApi), nameof(Before)) { priority = Priority.Last });
                var provider = typeof(Emby.Providers.MediaInfo.FFProbeProvider);
                _patch.Patch(provider.GetMethod("IsSupported"), postfix: new HarmonyMethod(typeof(DiscProbeApi), nameof(Supported)));
                _patch.Patch(provider.GetMethod("SupportsProbing"), postfix: new HarmonyMethod(typeof(DiscProbeApi), nameof(Probing)));
                _instance = this;
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex disc probe adapter unavailable: " + error.GetType().Name); }
        }
        public static void Supported(string __0, MediaProtocol __1, ref bool __result)
        { if (!__result && Enabled && __1 == MediaProtocol.File && Source(__0) != null) __result = true; }
        public static void Probing(MediaBrowser.Model.Dto.MediaSourceInfo __0, ref bool __result)
        { if (!__result && Enabled && __0.Protocol == MediaProtocol.File && Source(__0.Path) != null) __result = true; }
        public static string Source(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri)) { if (!uri.IsFile) return null; input = uri.LocalPath; }
            var first = input.Split('|')[0];
            if (Path.GetExtension(first).Equals(".iso", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(first);
            if (Path.GetFileName(first).Equals("BDMV", StringComparison.OrdinalIgnoreCase) && Directory.Exists(first)) return Path.GetDirectoryName(Path.GetFullPath(first));
            var parent = Path.GetDirectoryName(first);
            if (parent != null && Path.GetFileName(parent).Equals("STREAM", StringComparison.OrdinalIgnoreCase))
            {
                var bdmv = Path.GetDirectoryName(parent);
                if (Path.GetFileName(bdmv).Equals("BDMV", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(bdmv);
            }
            return Directory.Exists(Path.Combine(first, "BDMV")) ? Path.GetFullPath(first) : null;
        }
        public static bool Before(MediaInfoRequest __0, CancellationToken __1, bool __runOriginal, ref Task<MediaInfo> __result)
        {
            if (!__runOriginal || !Enabled || __0?.MediaSource == null || __0.MediaType == DlnaProfileType.Audio) return true;
            var source = Source(__0.MediaSource.Path);
            if (source == null) return true;
            __result = _instance.Probe(__0, source, __1); return false;
        }
        public static Task<MediaInfo> Prepare(string path, CancellationToken token)
        {
            var source = Enabled ? Source(path) : null;
            if (source == null || !File.Exists(source)) return Task.FromResult<MediaInfo>(null);
            return _instance.Probe(new MediaInfoRequest { ExtractChapters = true, MediaType = DlnaProfileType.Video,
                MediaSource = new MediaBrowser.Model.Dto.MediaSourceInfo { Path = path, Container = "iso", Protocol = MediaProtocol.File } }, source, token);
        }
        private async Task<MediaInfo> Probe(MediaInfoRequest request, string source, CancellationToken token)
        {
            await Slot.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var playlist = DiscPlaylist.Read(source, token);
                var configured = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.DiscFfprobePath;
                var executable = string.IsNullOrWhiteSpace(configured) ? _encoder.FfmpegConfiguration.ProbePath : configured;
                if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new FileNotFoundException("Disc FFprobe executable missing");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
                using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
                _encoder.FfmpegConfiguration.ApplyVariables(process.StartInfo);
                foreach (var argument in new[] { "-hide_banner", "-v", "error", "-threads", "1", "-playlist", playlist.Number.ToString(CultureInfo.InvariantCulture),
                    "-i", "bluray:" + playlist.Source, "-show_streams", "-show_format", "-show_chapters", "-show_data", "-print_format", "json" }) process.StartInfo.ArgumentList.Add(argument);
                process.Start();
                var output = Read(process.StandardOutput, 8 * 1024 * 1024, timeout.Token);
                var errors = Read(process.StandardError, 0, timeout.Token);
                string text;
                try
                {
                    var exited = process.WaitForExitAsync(timeout.Token);
                    var first = await Task.WhenAny(exited, output).ConfigureAwait(false);
                    if (ReferenceEquals(first, output) && output.IsFaulted) await output.ConfigureAwait(false);
                    await exited.ConfigureAwait(false);
                    text = await output.ConfigureAwait(false); await errors.ConfigureAwait(false);
                    if (process.ExitCode != 0) throw new InvalidDataException("Disc FFprobe failed; existing media records preserved");
                }
                catch
                {
                    if (!process.HasExited) process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    try { await Task.WhenAll(output, errors).ConfigureAwait(false); } catch { }
                    token.ThrowIfCancellationRequested(); throw;
                }
                var json = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidDataException("Disc probe returned no data");
                if (json["streams"] is not JsonArray streams || streams.Count == 0) throw new InvalidDataException("Disc probe returned no streams");
                foreach (var stream in streams.OfType<JsonObject>())
                {
                    var id = stream["id"]?.ToString();
                    var pid = id?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true && int.TryParse(id.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex :
                        int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) ? numeric : -1;
                    if (playlist.Languages.TryGetValue(pid, out var language) && language != "und")
                    { var tags = stream["tags"] as JsonObject; if (tags == null) { tags = new JsonObject(); stream["tags"] = tags; } tags["language"] = language; }
                }
                var probe = _json.DeserializeFromString<ProbeResult>(json.ToJsonString());
                var info = new ProbeResultNormalizer(Plugin.Instance.Logger, _files, _localization).GetMediaInfo(probe, false, request.MediaSource.Path, request.MediaSource.Protocol);
                if (info.MediaStreams?.Any(stream => stream.Type == MediaBrowser.Model.Entities.MediaStreamType.Video) != true) throw new InvalidDataException("Disc probe has no video stream");
                info.RunTimeTicks = playlist.DurationTicks;
                info.Container = File.Exists(source) ? "iso" : "bluray";
                if (File.Exists(source)) info.Size = new FileInfo(source).Length;
                if (request.ExtractChapters) info.Chapters = playlist.Chapters.Select((ticks, index) => new MediaBrowser.Model.Entities.ChapterInfo { StartPositionTicks = ticks, Name = "Chapter " + (index + 1) }).ToArray();
                foreach (var stream in info.MediaStreams) _streamInfo.SetDynamicValues(stream);
                Plugin.Instance.Logger.Info("FilmIndex disc: playlist=" + playlist.Number + ", chapters=" + playlist.Chapters.Length + ", clips=" + playlist.Clips.Length);
                return info;
            }
            finally { Slot.Release(); }
        }
        private static async Task<string> Read(StreamReader input, int limit, CancellationToken token)
        {
            var result = new StringBuilder(); var buffer = new char[4096]; int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                if (limit == 0) continue;
                if (result.Length + read > limit) throw new InvalidDataException("Disc probe output exceeds limit");
                result.Append(buffer, 0, read);
            }
            return result.ToString();
        }
        public void Dispose() { if (ReferenceEquals(_instance, this)) _instance = null; _patch?.UnpatchAll(_patch.Id); }
    }
}
