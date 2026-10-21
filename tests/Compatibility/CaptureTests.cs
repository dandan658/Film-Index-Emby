using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

internal static class CaptureTests
{
    public static async Task Run(Assembly built, string encoder, Action<bool, string> check)
    {
        encoder = Path.GetFullPath(encoder);
        var root = Path.Combine(Path.GetTempPath(), "film-index-capture-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "test video's input.mp4");
            using (var generate = new Process())
            {
                generate.StartInfo = new ProcessStartInfo(encoder) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=720x480:r=10", "-t", "3", "-c:v", "mpeg4", "-threads", "1", input }) generate.StartInfo.ArgumentList.Add(arg);
                generate.Start();
                var error = generate.StandardError.ReadToEndAsync();
                await generate.WaitForExitAsync();
                var details = await error;
                if (generate.ExitCode != 0) throw new InvalidOperationException("Fixture generation failed: " + details);
            }
            var capture = built.GetType("StrmAssistant.Common.CaptureRunner", true)!.GetMethod("Capture")!;
            Task<bool> Capture(string source, string output, bool portrait, TimeSpan? timeout = null, CancellationToken token = default) =>
                (Task<bool>)capture.Invoke(null, new object[] { encoder, source, output, 1d, portrait, timeout ?? TimeSpan.FromSeconds(15), token })!;
            var landscape = Path.Combine(root, "landscape.jpg");
            check(await Capture(input, landscape, false), "FFmpeg captures real video using quoted file paths");
            var portrait = Path.Combine(root, "portrait.jpg");
            check(await Capture(input, portrait, true), "FFmpeg captures portrait framing");
            var original = await File.ReadAllBytesAsync(landscape);
            check(!await Capture(Path.Combine(root, "missing.mp4"), landscape, false) &&
                original.SequenceEqual(await File.ReadAllBytesAsync(landscape)), "Failed capture preserves the previous image");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancelledCorrectly = false;
            try { await Capture(input, landscape, false, token: cancelled.Token); }
            catch (OperationCanceledException) { cancelledCorrectly = true; }
            check(cancelledCorrectly && original.SequenceEqual(await File.ReadAllBytesAsync(landscape)), "Capture cancellation propagates without replacing the image");
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var address = (IPEndPoint)listener.LocalEndpoint;
            var stalled = "http://127.0.0.1:" + address.Port + "/no-response";
            var watch = Stopwatch.StartNew();
            var timeoutResult = await Capture(stalled, landscape, false, TimeSpan.FromMilliseconds(800));
            check(!timeoutResult && watch.Elapsed < TimeSpan.FromSeconds(6), "Stalled remote media times out and its FFmpeg process exits");
            check(!Directory.EnumerateFiles(root).Any(file => Path.GetFileName(file).Count(c => c == '.') > 1 && file.EndsWith(".jpg")), "Capture temporary JPEG files are removed");
        }
        finally { Directory.Delete(root, true); }
    }
}
