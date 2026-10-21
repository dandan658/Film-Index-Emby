using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public static class ReverseLogReader
    {
        private static readonly Regex Entry = new Regex(@"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        public static async Task<QueryResult<string>> Read(Stream input, int skip, int take, bool total, CancellationToken token)
        {
            if (skip < 0 || skip > 10000 || take < 0 || take > 2000) throw new ArgumentOutOfRangeException("Log page exceeds the supported size");
            var entries = new Queue<string>(); var current = new StringBuilder(); var count = 0;
            void Add()
            {
                if (current.Length == 0) return;
                count++;
                if (take > 0) { entries.Enqueue(current.ToString().TrimEnd('\r','\n')); if (entries.Count > skip + take) entries.Dequeue(); }
                current.Clear();
            }
            using var reader = new StreamReader(input, Encoding.UTF8, true, 8192, true);
            string line;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
            {
                if (Entry.IsMatch(line)) Add();
                // Bound an individual diagnostic record while retaining the rest of the log stream.
                if (current.Length < 65536) current.AppendLine(line.Length > 65536 ? line.Substring(0,65536) : line);
            }
            Add();
            return new QueryResult<string> { Items = entries.Reverse().Skip(skip).Take(take).ToArray(), TotalRecordCount = total ? count : 0 };
        }
    }
}
