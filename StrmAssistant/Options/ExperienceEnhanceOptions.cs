using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using StrmAssistant.Properties;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public enum VersionLabelMode { Native, MediaInfo, FileName }
    public enum EpisodeViewStyle { Native, Primary, Thumb, List, Table }
    public class ExperienceEnhanceOptions : EditableOptionsBase
    {
        [DisplayNameL("ExperienceEnhanceOptions_EditorTitle_Experience_Enhance", typeof(Resources))]
        public override string EditorTitle => Resources.ExperienceEnhanceOptions_EditorTitle_Experience_Enhance;

        [DisplayName("完成剧集时整理之前的观看进度")]
        [Description("仅将同一节目中较早、已有播放进度且尚未看完的正片标为已播放；不会标记从未播放的剧集或特别篇。")]
        public bool MarkPreviousInProgressPlayed { get; set; } = false;

        [DisplayName("中文标题搜索增强")]
        [Description("使用独立的内存标题索引支持简繁混搜和中文子串搜索；不更换 Emby 数据库分词器。索引建立期间使用原生搜索。")]
        public bool EnhancedChineseSearch { get; set; } = false;

        [DisplayName("隐藏搜索页的最近搜索列表")]
        public bool HideSearchRecommendations { get; set; } = false;
        [DisplayName("节目卡片显示总集数")]
        [Description("仅改变卡片上的数量，不修改任何观看记录。")]
        public bool ShowTotalEpisodes { get; set; } = false;
        [DisplayName("主屏幕显示媒体库数量标记")]
        public bool ShowLibraryItemCounts { get; set; } = false;
        [DisplayName("季按倒序显示")]
        public bool DescendingSeasons { get; set; } = false;
        [DisplayName("集按倒序显示")]
        public bool DescendingEpisodes { get; set; } = false;
        [DisplayName("网页端季显示样式")]
        public EpisodeViewStyle SeasonViewStyle { get; set; } = EpisodeViewStyle.Native;
        [DisplayName("网页端剧集显示样式")]
        [Description("仅影响 Emby 网页端；修改后重新加载网页生效，不改写用户保存的显示偏好。")]
        public EpisodeViewStyle EpisodeViewStyle { get; set; } = EpisodeViewStyle.Native;
        [DisplayName("节目页面显示其季所属的合集")]
        public bool IncludeSeasonCollections { get; set; } = false;
        [DisplayName("日志按从新到旧显示")]
        [Description("日志查看接口按日志条目倒序显示，错误堆栈内部保持原顺序；原始日志下载不变。")]
        public bool NewestLogsFirst { get; set; } = false;

        [DisplayName("合集变更通知")]
        public bool NotifyCollectionChanges { get; set; } = false;
        [DisplayName("元数据更新通知")]
        public bool NotifyMetadataChanges { get; set; } = false;
        [DisplayName("媒体图像更新通知")]
        public bool NotifyImageChanges { get; set; } = false;

        [DisplayName("各媒体库分别记录进度和收藏")]
        [Description("按媒体库隔离电影和剧集的观看记录。切换后需刷新元数据；关闭并刷新后恢复原先的共享记录。")]
        public bool SeparateLibraryProgress { get; set; } = false;
        [DisplayName("多版本按分辨率和码率排序")]
        public bool SortVersionsByQuality { get; set; } = false;
        [DisplayName("多版本显示名称")]
        public VersionLabelMode VersionLabels { get; set; } = VersionLabelMode.Native;
        [DisplayName("同目录电影版本数量上限")]
        [MinValue(8), MaxValue(128)]
        public int MaximumFolderVersions { get; set; } = 8;
        
        [DisplayNameL("GeneralOptions_MergeMultiVersion_Merge_Multiple_Versions", typeof(Resources))]
        [DescriptionL("GeneralOptions_MergeMultiVersion_Auto_merge_multiple_versions_if_in_the_same_folder_", typeof(Resources))]
        [Required]
        public bool MergeMultiVersion { get; set; } = false;

        public enum MergeMoviesScopeOption
        {
            [DescriptionL("MergeScopeOption_LibraryScope_LibraryScope", typeof(Resources))]
            LibraryScope,
            [DescriptionL("MergeScopeOption_GlobalScope_GlobalScope", typeof(Resources))]
            GlobalScope
        }
        
        [DisplayName("")]
        [VisibleCondition(nameof(MergeMultiVersion), SimpleCondition.IsTrue)]
        public MergeMoviesScopeOption MergeMoviesPreference { get; set; } = MergeMoviesScopeOption.LibraryScope;

        public enum MergeSeriesScopeOption
        {
            [DescriptionL("MergeScopeOption_LibraryScope_LibraryScope", typeof(Resources))]
            LibraryScope,
            [DescriptionL("MergeScopeOption_GlobalScope_GlobalScope", typeof(Resources))]
            GlobalScope
        }

        [VisibleCondition(nameof(MergeMultiVersion), SimpleCondition.IsTrue)]
        [Browsable(false)]
        public MergeSeriesScopeOption MergeSeriesPreference => MergeSeriesScopeOption.LibraryScope;
    }
}
