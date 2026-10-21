# Film Index Emby

面向 **Emby 4.10.1.0** 的独立 GPL 社区源码派生插件。

当前发布 **v0.1.0-preview.1**，供用户安装测试；不代表全部目标功能完成或飞牛 Docker 已验证。插件不修改任何授权状态。

## 下载和安装

[直接下载安装包](https://github.com/dandan658/Film-Index-Emby/releases/download/v0.1.0-preview.1/FilmIndex.Emby-v0.1.0-preview.1-emby4.10.1.0.zip)

[安装包和安装教程](https://github.com/dandan658/Film-Index-Emby/releases/tag/v0.1.0-preview.1)

飞牛 Docker 无需终端：停止 Emby 容器，完整备份 Emby 数据目录，将旧 StrmAssistant 插件移到 plugins 目录外，再把包中的 **FilmIndex.Emby.dll** 放进 Emby 数据目录的 **plugins** 子目录，最后启动原容器。不需要更换 Emby 镜像或新建容器。

详细安装、升级和回退步骤见 [安装说明](packaging/INSTALL.zh-CN.md)。压缩包内只保留插件 DLL 与安装教程；教程附插件校验值、完整许可证和对应源码下载链接。不要复制测试 DLL 或服务器 DLL。

## 本版范围

已实现媒体信息提取及持久化、字幕/外挂音轨扫描、中文搜索、元数据缓存与覆盖、人物简介和头像缓存、本地未加密蓝光 ISO/BDMV 提取、截图、片头数据导入、指纹提取、版本管理和部分客户端显示增强等。实际覆盖以 [开发记录](DEVELOPMENT.md)、[验证清单](VERIFICATION.json) 和 tests/results 为准。

136 项隔离 Windows Emby 集成/契约检查、49 项兼容/规则/FFmpeg 检查、17 项 Windows BusyBox 安装回退检查和 8 项 JavaScript 断言通过。测试数量不是功能完成度；飞牛/Linux 实机兼容性由后续测试确认。

远程 HTTP ISO、DVD、复杂 DV P7、部分豆瓣高级刮削、剧集组及部分高级行为尚未完成；在线字幕下载、外部通知和部分客户端效果仍待实际账号或部署环境验收。请先在备份或测试媒体库中使用。

## 源码和构建

本仓库保留社区上游历史与许可证，基线为 `beb65cf8e4d7b19ce418c3aa32cfb6eff04acfb2`。上游：[sjtuross/StrmAssistant](https://github.com/sjtuross/StrmAssistant)。原说明保存在 docs/，其功能列表不代表本版已全部支持。

使用 .NET 8 及官方 Emby 4.10.1.0 system 目录作为引用：

```powershell
dotnet build ./StrmAssistant/StrmAssistant.csproj -c Release -p:EmbyServerPath=C:/path/to/emby/system
python ./packaging/build-package.py --output C:/release-output --source-revision <构建DLL时的提交>
```

服务端引用仅用于编译，不在安装包分发。GPL、Harmony、DiscUtils、OpenCC 的声明与来源均随包保留。独立项目不使用原 PRO 二进制，也不声称是原作者的 PRO 发行版。
