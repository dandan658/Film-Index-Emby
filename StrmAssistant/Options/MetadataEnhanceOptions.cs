using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public enum LocalMetadataMode { Off, PreferCache, OnlineAndCache, OverrideOnly }
    public enum RefreshPersonOption
    {
        Default,
        FullRefresh,
        NoAdult
    }

    public class MetadataEnhanceOptions : EditableOptionsBase
    {
        [DisplayName("豆瓣本地缓存辅助刮削")]
        [Description("读取缓存补充非中文标题、简介和评分；缓存位于本地元数据根目录的 douban 子目录。保留锁定字段。")]
        public bool DoubanAssist { get; set; } = false;
        [DisplayName("允许在线构建豆瓣缓存")]
        [Description("计划任务仅访问明确填写了 Douban 编号的项目；单线程限速，遇到访问限制立即冷却，不绕过验证。")]
        public bool DoubanNetwork { get; set; } = false;
        [DisplayName("应用豆瓣评分")]
        public bool DoubanRating { get; set; } = true;
        [DisplayName("按简繁中文优选海报和徽标")]
        [Description("依据媒体库图片语言和图片自身的 zh-CN、zh-TW、zh-HK 等语言标签排序；没有标签的图片不会被猜测为简中或繁中。")]
        public bool ChineseArtworkScripts { get; set; } = false;
        [DisplayName("中文元数据备选语言")]
        [Description("中文资料缺失时，依次尝试繁中、简中、原生备选语言，并将结果转换为请求的中文字体。")]
        public bool ChineseFallbackLanguages { get; set; } = false;
        [DisplayName("新建影视媒体库优先启用 TMDB")]
        [Description("仅应用于新建媒体库，不重写现有媒体库配置。")]
        public bool DefaultTmdbForNewLibraries { get; set; } = false;
        [DisplayName("本地元数据模式")]
        public LocalMetadataMode LocalMetadataMode { get; set; } = LocalMetadataMode.Off;
        [DisplayName("本地元数据根目录")]
        [Description("缓存与自定义覆盖按 TMDB ID 保存；覆盖目录只读。适用于电影、节目、季、集和合集。")]
        public string LocalMetadataRoot { get; set; } = string.Empty;
        [DisplayNameL("PluginOptions_MetadataEnhanceOptions_Metadata_Enhance", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_MetadataEnhanceOptions_Metadata_Enhance;
        
        [Browsable(false)]
        [Required]
        public string RefreshPersonMode { get; set; } = RefreshPersonOption.Default.ToString();

        public enum EpisodeRefreshOption
        {
            [DescriptionL("EpisodeRefreshOption_NoOverview_No_Overview", typeof(Resources))]
            NoOverview,
            [DescriptionL("EpisodeRefreshOption_NoImage_No_Image", typeof(Resources))]
            NoImage,
            [DescriptionL("EpisodeRefreshOption_NonChineseOverview_Non_Chinese_Overview", typeof(Resources))]
            NonChineseOverview,
            [DescriptionL("EpisodeRefreshOption_DefaultEpisodeName_Default_Episode_Name", typeof(Resources))]
            DefaultEpisodeName,
            [DescriptionL("EpisodeRefreshOption_ReplaceCapturedImage_Replace_Captured_Image", typeof(Resources))]
            ReplaceCapturedImage
        }

        [Browsable(false)]
        public List<EditorSelectOption> EpisodeRefreshOptionList { get; set; } = new List<EditorSelectOption>();

        [DisplayNameL("MetadataEnhanceOptions_EpisodeRefreshScope_Episode_Metadata_Refresh_Scope", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_EpisodeRefreshScope_Episode_refresh_scope_for_scheduled_task_and_catch_up__Default_is_no_overview_and_no_image_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(EpisodeRefreshOptionList))]
        public string EpisodeRefreshScope { get; set; } = string.Join(",", EpisodeRefreshOption.NoOverview.ToString(),
            EpisodeRefreshOption.NoImage.ToString());
        
        [DisplayNameL("MetadataEnhanceOptions_EpisodeRefreshLookBackDays_Episode_Refresh_Lookback_Days", typeof(Resources))]
        [DescriptionL("MetadataEnhanceOptions_EpisodeRefreshLookbackDays_Episode_metadata_refresh_lookback_days__Default_is_365_", typeof(Resources))]
        [Required, MinValue(1)]
        public int EpisodeRefreshLookbackDays { get; set; } = 365;
    }
}
