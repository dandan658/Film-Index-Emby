using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StrmAssistant.Common
{
    /// <summary>Managed longest-phrase conversion using attributed OpenCC data; no native DLLs.</summary>
    public static class ChineseText
    {
        private sealed class Table
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<char, int> MaxLengths = new Dictionary<char, int>();
            public Table(string prefix)
            {
                foreach (var suffix in new[] { "Characters", "Phrases" })
                {
                    using var stream = typeof(ChineseText).Assembly.GetManifestResourceStream(
                        "StrmAssistant.Data.OpenCC." + prefix + suffix + ".txt")
                        ?? throw new InvalidOperationException("Missing conversion dictionary");
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        var split = line.IndexOf('\t');
                        if (split <= 0 || split == line.Length - 1) continue;
                        var key = line.Substring(0, split);
                        var value = line.Substring(split + 1).Split(' ')[0];
                        Values[key] = value;
                        MaxLengths[key[0]] = Math.Max(MaxLengths.TryGetValue(key[0], out var old) ? old : 0, key.Length);
                    }
                }
            }
        }

        private static readonly Lazy<Table> Simplified = new Lazy<Table>(() => new Table("TS"));
        private static readonly Lazy<Table> Traditional = new Lazy<Table>(() => new Table("ST"));
        public static string ToSimplified(string value) => Convert(value, Simplified);
        public static string ToTraditional(string value) => Convert(value, Traditional);

        private static string Convert(string value, Lazy<Table> lazy)
        {
            if (string.IsNullOrEmpty(value) || value.All(c => c < 128)) return value;
            var table = lazy.Value;
            var result = new StringBuilder(value.Length);
            for (var offset = 0; offset < value.Length;)
            {
                var matched = false;
                if (table.MaxLengths.TryGetValue(value[offset], out var max))
                {
                    for (var length = Math.Min(max, value.Length - offset); length > 0; length--)
                    {
                        if (!table.Values.TryGetValue(value.Substring(offset, length), out var replacement)) continue;
                        result.Append(replacement);
                        offset += length;
                        matched = true;
                        break;
                    }
                }
                if (!matched) result.Append(value[offset++]);
            }
            return result.ToString();
        }
    }
}
