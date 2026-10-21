using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.LocalizationAttributes;
using MediaBrowser.Model.MediaInfo;
using StrmAssistant.Properties;
using System.Collections.Generic;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public class MediaInfoExtractOptions : EditableOptionsBase
    {
        [DisplayName("探测本地 Blu-ray ISO 和 BDMV")]
        [Description("读取 UDF/MPLS 的主播放列表、准确时长、章节和音轨语言，使用支持 bluray 的探测程序。仅限未加密本地或已挂载路径。")]
        public bool EnableDiscProbe { get; set; } = false;
        [DisplayName("光盘专用 FFprobe 路径")]
        [Description("留空使用 Emby 自带探测程序；可填写支持 Blu-ray 的可执行文件绝对路径，不填写命令参数。")]
        public string DiscFfprobePath { get; set; } = string.Empty;
        [DisplayName("STRM 字幕按 IMDb 和季集信息搜索")]
        [Description("避免把 STRM 文本当作视频计算哈希。远程视频使用 IMDb/季集信息匹配；原生字幕账号和下载权限保持有效。")]
        public bool OptimizeStrmSubtitleSearch { get; set; } = false;
        [DisplayNameL("PluginOptions_EditorTitle_Strm_Extract", typeof(Resources))]
        public override string EditorTitle => Resources.PluginOptions_EditorTitle_Strm_Extract;

        [DisplayName("提取排除关键词")]
        [Description("按名称或路径匹配，多个词用分号分隔。影响媒体信息、片头声纹和预览图提取；留空关闭。")]
        public string ExcludedExtractionKeywords { get; set; } = string.Empty;

        [DisplayName("提取排除标签")]
        [Description("标签完整匹配，多个标签用分号分隔。不区分大小写，留空关闭。")]
        public string ExcludedExtractionTags { get; set; } = string.Empty;

        [DisplayName("同时扫描外挂音轨")]
        [Description("外挂字幕扫描时识别并探测同名音轨；探测失败时保留当前媒体流记录。")]
        public bool ScanExternalAudioTracks { get; set; } = false;

        [DisplayNameL("PluginOptions_IncludeExtra_Include_Extra", typeof(Resources))]
        [DescriptionL("PluginOptions_IncludeExtra_Include_media_extras_to_extract__Default_is_False_", typeof(Resources))]
        [Required]
        public bool IncludeExtra { get; set; } = false;

        [DisplayNameL("PluginOptions_EnableImageCapture_Enable_Image_Capture", typeof(Resources))]
        [DescriptionL("PluginOptions_EnableImageCapture_Perform_image_capture_for_videos_without_primary_image__Default_is_False_", typeof(Resources))]
        [Required]
        public bool EnableImageCapture { get; set; } = false;

        [DisplayName("自定义 FFmpeg 路径")]
        [Description("留空使用 Emby 自带程序。填写容器内可执行文件的绝对路径，不填写命令参数。用于本插件生成封面图。")]
        [VisibleCondition(nameof(EnableImageCapture), SimpleCondition.IsTrue)]
        public string CustomFfmpegPath { get; set; } = string.Empty;

        [DisplayNameL("MediaInfoExtractOptions_ImageCaptureOffset_Image_Capture_Offset", typeof(Resources))]
        [DescriptionL("MediaInfoExtractOptions_ImageCaptureOffset_Image_capture_position_as_a_percentage_of_runtime__Default_is_10_", typeof(Resources))]
        [Required, MinValue(10), MaxValue(90)]
        [VisibleCondition(nameof(EnableImageCapture), SimpleCondition.IsTrue)]
        public int ImageCapturePosition { get; set; } = 10;

        [Browsable(false)]
        [Required]
        public string ImageCaptureExcludeMediaContainers { get; set; } =
            string.Join(",", new[] { MediaContainers.MpegTs, MediaContainers.Ts, MediaContainers.M2Ts });

        public enum PersistMediaInfoOption
        {
            None,
            Default,
            Restore
        }

        [Browsable(false)]
        public List<EditorRadioOption> PersistMediaInfoOptionList { get; set; } = new List<EditorRadioOption>();

        [DisplayName("")]
        [SelectItemsSource(nameof(PersistMediaInfoOptionList))]
        [SelectShowRadioGroup]
        public string PersistMediaInfoMode { get; set; } = PersistMediaInfoOption.None.ToString();

        [DisplayNameL("MediaInfoExtractOptions_MediaInfoJsonRootFolder_MediaInfo_Json_Root_Folder", typeof(Resources))]
        [DescriptionL("MediaInfoExtractOptions_MediaInfoJsonRootFolder_Store_or_load_media_info_JSON_files_under_this_root_folder__Default_is_EMPTY_", typeof(Resources))]
        [EditFolderPicker]
        [VisibleCondition(nameof(PersistMediaInfoMode), ValueCondition.IsNotEqual, PersistMediaInfoOption.None)]
        public string MediaInfoJsonRootFolder { get; set; } = string.Empty;

        [Browsable(false)]
        public IEnumerable<EditorSelectOption> LibraryList { get; set; }

        [DisplayNameL("PluginOptions_LibraryScope_Library_Scope", typeof(Resources))]
        [DescriptionL("PluginOptions_LibraryScope_Library_scope_to_extract__Blank_includes_all_", typeof(Resources))]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryList))]
        public string LibraryScope { get; set; } = string.Empty;
    }
}
