# 安装教程 — v0.1.2-preview.1

适用于 **Emby 4.10.1.0**。当前是用户测试版，已在用户提供的 Linux Docker 实例执行在线验收，仍未完成全部目标功能，也不等于所有飞牛设备均已验证。

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

本版在隔离 Windows Emby 环境通过 143 项集成/契约检查。用户 Linux Emby 4.10.1.0 已验证最爱提取、JSON 写入与恢复、设置、检索和媒体库复制。本版继续修复默认 HTTP 排序、缓存命中漏补图、拒绝状态码与失效范围扩大处理；新 DLL 仍需替换后在用户服务器复测。

[本轮实测和限制说明](https://github.com/dandan658/Film-Index-Emby/blob/main/docs/LIVE-RETEST-0.1.1.md)。用户服务器已经设置了可写缓存目录，升级不要清空原插件配置。

## 只读云盘目录

若媒体路径以只读方式挂载，请在插件设置中把“媒体信息 JSON 根目录”设为容器内可写且持久化的路径，例如 `/config/film-index/mediainfo`。留空会尝试写到视频旁边，只读挂载下会失败。升级时保留整个 `/config` 的映射。

在线日志还发现部分媒体内嵌封面的格式声明与内容不符，导致 Emby 原生截图失败；这与最爱提取筛选是两个问题，本版不宣称修复了原生截图提供器。

[源码仓库与完整功能记录](https://github.com/dandan658/Film-Index-Emby)。本项目是独立 GPL 社区派生版本，不修改任何产品或外部服务的授权状态。
