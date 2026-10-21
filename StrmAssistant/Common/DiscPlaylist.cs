using DiscUtils;
using DiscUtils.Udf;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace StrmAssistant.Common
{
    public sealed class DiscPlaylist
    {
        public string Source { get; private set; }
        public int Number { get; private set; }
        public long DurationTicks { get; private set; }
        public long[] Chapters { get; private set; }
        public string[] Clips { get; private set; }
        public Dictionary<int, string> Languages { get; private set; } = new();
        private static long Ticks(long timestamp) => checked(timestamp * TimeSpan.TicksPerSecond / 45000);
        private static string Name(string path) => path.Replace('\\', '/').Split('/').Last();
        private static void Need(byte[] bytes, int start, int length) { if (start < 0 || length < 0 || start > bytes.Length - length) throw new InvalidDataException("Truncated MPLS record"); }
        private static int U16(byte[] bytes, int offset) { Need(bytes, offset, 2); return BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2)); }
        private static int U32(byte[] bytes, int offset) { Need(bytes, offset, 4); var value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)); if (value > int.MaxValue) throw new InvalidDataException("MPLS field exceeds range"); return (int)value; }
        public static DiscPlaylist Parse(byte[] bytes, int number)
        {
            Need(bytes, 0, 20);
            if (Encoding.ASCII.GetString(bytes, 0, 4) != "MPLS") throw new InvalidDataException("Invalid MPLS signature");
            var version = Encoding.ASCII.GetString(bytes, 4, 4);
            if (version != "0100" && version != "0200" && version != "0300") throw new InvalidDataException("Unsupported MPLS version");
            var playlist = U32(bytes, 8); var marks = U32(bytes, 12);
            var playlistEnd = checked(playlist + 4 + U32(bytes, playlist)); Need(bytes, playlist, playlistEnd - playlist);
            var count = U16(bytes, playlist + 6);
            if (count == 0 || count > 1000) throw new InvalidDataException("Invalid MPLS play-item count");
            var position = playlist + 10; var starts = new List<long>(); var inTimes = new List<long>(); var lengths = new List<long>(); var clips = new List<string>();
            var result = new DiscPlaylist { Number = number };
            for (var i = 0; i < count; i++)
            {
                var size = U16(bytes, position); var body = position + 2; var end = checked(body + size);
                if (size < 32 || end > playlistEnd) throw new InvalidDataException("Invalid MPLS play-item length");
                Need(bytes, body, size);
                var clip = Encoding.ASCII.GetString(bytes, body, 5);
                if (!Regex.IsMatch(clip, @"^\d{5}$") || Encoding.ASCII.GetString(bytes, body + 5, 4) != "M2TS") throw new InvalidDataException("Invalid MPLS clip reference");
                var from = U32(bytes, body + 12); var to = U32(bytes, body + 16);
                if (to <= from) throw new InvalidDataException("Invalid MPLS clip duration");
                starts.Add(result.DurationTicks); inTimes.Add(from); lengths.Add(to - from); clips.Add(clip);
                result.DurationTicks = checked(result.DurationTicks + Ticks(to - from));
                if (result.DurationTicks > TimeSpan.FromHours(24).Ticks) throw new InvalidDataException("MPLS duration exceeds limit");
                var table = body + 32;
                if ((U16(bytes, body + 9) & 0x10) != 0)
                { Need(bytes, table, 2); var angles = bytes[table]; if (angles == 0 || angles > 9) throw new InvalidDataException("Invalid MPLS angle count"); table += 2 + (angles - 1) * 10; }
                ReadLanguages(bytes, table, end, result.Languages);
                position = end;
            }
            var chapters = new SortedSet<long> { 0 };
            if (marks > 0)
            {
                var markEnd = checked(marks + 4 + U32(bytes, marks)); Need(bytes, marks, markEnd - marks);
                var markCount = U16(bytes, marks + 4); if (markCount > 10000) throw new InvalidDataException("MPLS chapter count exceeds limit");
                for (var i = 0; i < markCount; i++)
                {
                    var mark = marks + 6 + i * 14; Need(bytes, mark, 14); if (mark + 14 > markEnd) throw new InvalidDataException("Truncated MPLS chapter table");
                    var item = U16(bytes, mark + 2); if (bytes[mark + 1] != 1 || item >= count) continue;
                    var relative = (long)U32(bytes, mark + 4) - inTimes[item];
                    if (relative >= 0 && relative < lengths[item]) chapters.Add(starts[item] + Ticks(relative));
                }
            }
            result.Clips = clips.ToArray(); result.Chapters = chapters.ToArray(); return result;
        }
        private static void ReadLanguages(byte[] bytes, int table, int itemEnd, Dictionary<int, string> languages)
        {
            var length = U16(bytes, table); var end = checked(table + 2 + length);
            if (length < 14 || end > itemEnd) throw new InvalidDataException("Invalid MPLS stream table");
            var count = bytes[table + 4] + bytes[table + 5] + bytes[table + 6]; var at = table + 16;
            for (var i = 0; i < count; i++)
            {
                Need(bytes, at, 1); var entryEnd = at + 1 + bytes[at]; if (entryEnd > end || bytes[at] < 3) throw new InvalidDataException("Invalid MPLS stream entry");
                var type = bytes[at + 1]; var pid = type == 1 ? U16(bytes, at + 2) : -1;
                at = entryEnd; Need(bytes, at, 2); var attributeEnd = at + 1 + bytes[at];
                if (bytes[at] < 1 || attributeEnd > end) throw new InvalidDataException("Invalid MPLS stream attributes");
                var coding = bytes[at + 1]; var languageOffset = coding == 0x90 || coding == 0x91 ? at + 2 :
                    new[] { 3, 4, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0xa1, 0xa2, 0x92 }.Contains((int)coding) ? at + 3 : -1;
                if (pid >= 0 && languageOffset >= 0 && languageOffset + 3 <= attributeEnd)
                {
                    var language = Encoding.ASCII.GetString(bytes, languageOffset, 3);
                    if (Regex.IsMatch(language, "^[a-z]{3}$", RegexOptions.CultureInvariant))
                    { if (!languages.TryGetValue(pid, out var old)) languages[pid] = language; else if (old != language) languages[pid] = "und"; }
                }
                at = attributeEnd;
            }
        }
        public static DiscPlaylist Read(string source, CancellationToken token)
        {
            source = Path.GetFullPath(source);
            FileStream input = null; DiscFileSystem filesystem = null;
            try
            {
                Func<string, bool> exists; Func<string, string[]> files; Func<string, Stream> open;
                if (File.Exists(source))
                {
                    input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
                    if (!UdfReader.Detect(input)) throw new InvalidDataException("Disc image is not UDF");
                    input.Position = 0; filesystem = new UdfReader(input);
                    exists = filesystem.DirectoryExists; files = path => filesystem.GetFiles(path); open = path => filesystem.OpenFile(path, FileMode.Open, FileAccess.Read);
                }
                else
                {
                    if (Path.GetFileName(source).Equals("BDMV", StringComparison.OrdinalIgnoreCase)) source = Path.GetDirectoryName(source);
                    string Local(string path) => path.Replace('\\', Path.DirectorySeparatorChar);
                    exists = path => Directory.Exists(Path.Combine(source, Local(path))); files = path => Directory.GetFiles(Path.Combine(source, Local(path))); open = path => File.OpenRead(Path.IsPathRooted(Local(path)) ? Local(path) : Path.Combine(source, Local(path)));
                }
                if (exists("AACS")) throw new NotSupportedException("Encrypted discs are not supported");
                if (exists("VIDEO_TS")) throw new NotSupportedException("DVD metadata extraction is not yet supported");
                var playlistDirectory = "BDMV\\PLAYLIST";
                if (!exists(playlistDirectory)) throw new InvalidDataException("Blu-ray playlist directory missing");
                DiscPlaylist selected = null;
                var candidates = files(playlistDirectory).Where(path => Regex.IsMatch(Name(path), @"^\d{5}\.mpls$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).OrderBy(path => path, StringComparer.Ordinal).Take(1001).ToArray();
                if (candidates.Length > 1000) throw new InvalidDataException("Disc playlist count exceeds limit");
                foreach (var path in candidates)
                {
                    token.ThrowIfCancellationRequested(); using var stream = open(path);
                    if (stream.Length > 1024 * 1024) throw new InvalidDataException("MPLS exceeds size limit");
                    var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
                    var playlist = Parse(bytes, int.Parse(Path.GetFileNameWithoutExtension(Name(path)), System.Globalization.CultureInfo.InvariantCulture));
                    if (selected == null || playlist.DurationTicks > selected.DurationTicks) selected = playlist;
                }
                if (selected == null) throw new InvalidDataException("No usable Blu-ray playlist found");
                selected.Source = source; return selected;
            }
            finally { filesystem?.Dispose(); input?.Dispose(); }
        }
    }
}
