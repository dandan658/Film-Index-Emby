using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using System;
using System.ComponentModel;

namespace StrmAssistant.Options
{
    public enum ProxyMode { Off, MetadataOnly, Global }
    public sealed class NetworkOptions : EditableOptionsBase
    {
        public override string EditorTitle => "网络";
        [DisplayName("代理模式")]
        [Description("关闭、仅代理刮削白名单或全局代理。回环和私网 IP 始终直连。切换模式对已接管的连接生效，首次安装后重启 Emby。")]
        public ProxyMode ProxyMode { get; set; } = ProxyMode.Off;
        [DisplayName("代理地址")]
        [Description("例如 http://192.168.1.10:7890 或 socks5://192.168.1.10:1080。可使用 URL 编码的用户名与密码。")]
        public string ProxyUrl { get; set; } = string.Empty;
        [DisplayName("刮削白名单主机")]
        [Description("分号或换行分隔。支持 *.example.com，域名完整匹配。")]
        public string MetadataHosts { get; set; } = "api.themoviedb.org;api.tmdb.org;image.tmdb.org;webservice.fanart.tv;api.thetvdb.com;api.opensubtitles.com";
        [DisplayName("本地发现地址")]
        [Description("留空使用 Emby 自动地址。可填写局域网反向代理的完整 HTTP/HTTPS 地址，不包含账号密码。")]
        public string LocalDiscoveryAddress { get; set; } = string.Empty;
        protected override void Validate(ValidationContext context)
        {
            if (ProxyMode != ProxyMode.Off && (!Uri.TryCreate(ProxyUrl, UriKind.Absolute, out var uri) ||
                !(uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "socks5" || uri.Scheme == "socks4")))
                context.AddValidationError("代理地址须为完整的 HTTP、HTTPS、SOCKS4 或 SOCKS5 URL。");
            if (!string.IsNullOrWhiteSpace(LocalDiscoveryAddress) &&
                (!Uri.TryCreate(LocalDiscoveryAddress, UriKind.Absolute, out var address) ||
                !(address.Scheme == "http" || address.Scheme == "https") || !string.IsNullOrEmpty(address.UserInfo) ||
                !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment)))
                context.AddValidationError("本地发现地址须为不含凭据、查询参数和片段的完整 HTTP/HTTPS URL。");
        }
    }
}
