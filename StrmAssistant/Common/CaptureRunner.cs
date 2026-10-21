using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public static class CaptureRunner
    {
        public static async Task<bool> Capture(string encoder, string input, string output, double seconds,
            bool portrait, TimeSpan timeout, CancellationToken cancellation)
        {
            if (!File.Exists(encoder)) throw new FileNotFoundException("FFmpeg executable does not exist.");
            if (seconds < 0 || !double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            var temporary = output + "." + Guid.NewGuid().ToString("N") + ".jpg";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(timeout);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(encoder)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-ss",
                seconds.ToString("0.###", CultureInfo.InvariantCulture), "-threads", "1", "-i", input,
                "-map", "0:v:0", "-frames:v", "1", "-an", "-sn", "-vf",
                portrait ? "scale=426:640:force_original_aspect_ratio=increase,crop=426:640" :
                    "scale=640:360:force_original_aspect_ratio=increase,crop=640:360",
                "-threads", "1", "-q:v", "3", "-f", "image2", temporary })
                process.StartInfo.ArgumentList.Add(argument);
            try
            {
                process.Start();
                var drain = Drain(process.StandardError);
                try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    await drain.ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    return false;
                }
                await drain.ConfigureAwait(false);
                if (process.ExitCode != 0 || !File.Exists(temporary)) return false;
                await using (var file = File.OpenRead(temporary))
                {
                    if (file.Length < 4 || file.ReadByte() != 0xff || file.ReadByte() != 0xd8) return false;
                    file.Seek(-2, SeekOrigin.End);
                    if (file.ReadByte() != 0xff || file.ReadByte() != 0xd9) return false;
                }
                File.Move(temporary, output, true);
                return true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static async Task Drain(StreamReader reader)
        {
            var buffer = new char[2048];
            while (await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) != 0) { }
        }
    }
}
