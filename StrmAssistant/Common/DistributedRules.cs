using System;
using System.IO;
using System.Linq;

namespace StrmAssistant.Common
{
    public static class DistributedRules
    {
        public static bool IsWithin(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
            return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        public static bool AllowsFile(string path, string roots) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
            (roots ?? string.Empty).Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(root => IsWithin(path, root.Trim()));
        public static string Remap(string path, string masterRoot, string workerRoot)
        {
            if (string.IsNullOrWhiteSpace(masterRoot) && string.IsNullOrWhiteSpace(workerRoot)) return path;
            if (!IsWithin(path, masterRoot) || string.IsNullOrWhiteSpace(workerRoot)) throw new ArgumentException("Source does not match the configured worker path mapping");
            return Path.Combine(workerRoot, Path.GetRelativePath(masterRoot, path));
        }
    }
}
