using System;
using System.Collections.Generic;
using System.Linq;

namespace StrmAssistant.Common
{
    // Independent literal matching. No regex execution or directory traversal.
    public static class ExtractionRules
    {
        private static string[] Tokens(string value) => (value ?? string.Empty)
            .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim()).Where(token => token.Length > 0).ToArray();

        public static bool IsExcluded(string name, string path, IEnumerable<string> tags,
            string excludedKeywords, string excludedTags)
        {
            var keywords = Tokens(excludedKeywords);
            if (keywords.Any(keyword =>
                (name?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (path?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)) return true;
            var blockedTags = new HashSet<string>(Tokens(excludedTags), StringComparer.OrdinalIgnoreCase);
            return tags != null && tags.Any(tag => tag != null && blockedTags.Contains(tag.Trim()));
        }
    }
}
