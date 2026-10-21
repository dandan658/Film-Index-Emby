using System;
using System.IO;
using System.Linq;

namespace StrmAssistant.Common
{
    public static class ArtifactRules
    {
        public static bool IsBifForVideo(string videoPath, string bifPath)
        {
            if (string.IsNullOrEmpty(videoPath) || string.IsNullOrEmpty(bifPath) ||
                !string.Equals(Path.GetExtension(bifPath), ".bif", StringComparison.OrdinalIgnoreCase)) return false;
            var name = Path.GetFileNameWithoutExtension(videoPath);
            var candidate = Path.GetFileNameWithoutExtension(bifPath);
            if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            if (!candidate.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase)) return false;
            var parts = candidate.Substring(name.Length + 1).Split('-');
            return parts.Length >= 1 && parts.Length <= 2 && parts.All(part => int.TryParse(part, out var value) && value > 0);
        }
    }
}
