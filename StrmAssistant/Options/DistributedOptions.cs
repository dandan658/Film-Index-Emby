using Emby.Web.GenericEdit;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public sealed class DistributedOptions : EditableOptionsBase
    {
        public override string EditorTitle => "分布式媒体信息提取";
        [DisplayName("使用远端工作节点")]
        public bool UseWorker { get; set; } = false;
        [DisplayName("工作节点 Emby 地址")]
        public string WorkerUrl { get; set; } = string.Empty;
        [DisplayName("工作节点 Emby API Key")]
        [Description("使用工作节点自身的 Emby 管理 API Key；不是插件授权码。")]
        [MediaBrowser.Model.Attributes.IsPassword]
        public string WorkerApiKey { get; set; } = string.Empty;
        [DisplayName("本机文件根目录（可选映射）")]
        public string MasterRoot { get; set; } = string.Empty;
        [DisplayName("工作节点对应目录")]
        public string WorkerRoot { get; set; } = string.Empty;
        [DisplayName("允许本机作为工作节点")]
        [Description("仅接受已通过 Emby 管理员认证的请求，同时限制源目录和远程源主机。最多同时探测两个文件。")]
        public bool ServeWorker { get; set; } = false;
        [DisplayName("工作节点可读取的文件根目录")]
        [Description("分号或换行分隔。留空不允许读取本地文件。")]
        public string AllowedRoots { get; set; } = string.Empty;
        [DisplayName("工作节点可探测的 HTTP/HTTPS 源主机")]
        [Description("分号或换行分隔，支持 *.example.com。留空不允许远程 URL。")]
        public string AllowedSourceHosts { get; set; } = string.Empty;
    }
}
