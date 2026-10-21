# 安装教程 — v0.1.0-preview.1

适用于 **Emby 4.10.1.0**。当前是用户测试版，飞牛 Docker/Linux 尚待实际部署验证，未完成全部目标功能。

## 飞牛 Docker 安装（无需终端）

1. 在飞牛 Docker 中停止 Emby 容器。
2. 查看容器的存储映射，找到 Emby 数据目录对应的宿主机目录。常见容器路径是 `/config`，以实际映射为准；里面应有 `config`、`plugins` 等子目录。
3. 将整个 Emby 数据目录复制或快照到其他位置留作备份，必须在容器停止后操作。
4. 如果安装过 StrmAssistant，把原 `StrmAssistant.dll` 或 `StrmAssistant.Pro.dll` 移到 `plugins` 目录之外备份，避免同时加载。不要移除 Emby 自带的 MovieDb、OpenSubtitles 等插件。
5. 解压安装包，只将 `FilmIndex.Emby.dll` 放入 Emby 数据目录的 `plugins` 子目录。
6. 启动原来的 Emby 容器，在 Emby 后台插件页面找到 Film Index Emby，按需开启功能。

不需要更换 Docker 镜像，不需要新建容器。ffmpeg/ffprobe 使用原 Emby 镜像提供的版本，不在本包中附带。本地蓝光提取要求探测程序支持 `bluray`。

## 后续升级

停止 Emby 容器，备份整个数据目录及旧插件 DLL，再用新 DLL 替换 `plugins/FilmIndex.Emby.dll`，启动原容器。不要删除 `plugins/configurations/Film Index Emby.json`，以保留本插件设置。

## 出错回退

停止容器，将本次插件移出 `plugins`，恢复原先备份的插件 DLL 和设置后再启动。若要回退已写入数据库的刮削结果或进度隔离等修改，需要恢复整个 Emby 数据目录备份；外部缓存目录也应自行备份和恢复。

## 当前限制

远程 HTTP ISO、DVD、复杂 DV P7、部分豆瓣高级刮削、剧集组和部分高级行为尚未完成。在线字幕下载、外部通知及部分客户端行为仍待实际环境验收。请先使用测试媒体库。

本版在隔离 Windows Emby 4.10.1.0 环境通过 136 项集成/契约检查，另通过 49 项兼容/规则/FFmpeg 检查、17 项 Windows BusyBox 安装回退检查和 8 项 JavaScript 断言。这不等于飞牛实机认证，也不代表全部目标功能完成。

[源码仓库与完整功能记录](https://github.com/dandan658/Film-Index-Emby)。本项目是独立 GPL 社区派生版本，不修改任何产品或外部服务的授权状态。
