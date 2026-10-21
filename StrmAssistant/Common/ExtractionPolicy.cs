using MediaBrowser.Controller.Entities;

namespace StrmAssistant.Common
{
    internal static class ExtractionPolicy
    {
        public static bool IsExcluded(BaseItem item)
        {
            var options = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions;
            return ExtractionRules.IsExcluded(item.Name, item.Path, item.Tags,
                options.ExcludedExtractionKeywords, options.ExcludedExtractionTags);
        }
    }
}
