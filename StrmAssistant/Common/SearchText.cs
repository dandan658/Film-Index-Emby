using System;
using System.Linq;
using System.Text;

namespace StrmAssistant.Common
{
    public static class SearchText
    {
        public static string Normalize(string input) => ChineseText.ToSimplified(input ?? string.Empty)
            .Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        public static bool ContainsChinese(string input) => input != null && input.Any(c => c >= 0x3400 && c <= 0x9fff);
        public static string[] Terms(string search) => Normalize(search).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        public static bool MatchesTerms(string normalizedTitle, string[] terms) => terms.Length > 0 && terms.All(term => normalizedTitle.Contains(term, StringComparison.Ordinal));
        public static bool Matches(string normalizedTitle, string search) => MatchesTerms(normalizedTitle, Terms(search));
    }
}
