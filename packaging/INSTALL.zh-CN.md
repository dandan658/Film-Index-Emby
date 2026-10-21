# 安装与回退说明 — v0.1.0-preview.1

当前为供用户测试的预览安装包。未实现全部目标功能，未完成飞牛 Linux 容器实机验收。安装包中的 source/ 附带对应源码，manifest.json 记录构建提交和各文件校验值。

适用目标是 Emby **4.10.1.0**。这是独立的 GPL 社区源码派生插件，文件名为 `FilmIndex.Emby.dll`。它不修改 Emby 或原插件的授权状态，外部字幕服务仍使用自己的正常账号和权限。

## 飞牛 Docker：通过文件管理器安装

1. 在 Emby 后台确认服务器版本是 4.10.1.0，在飞牛 Docker 中停止 Emby 容器。
2. 查看容器的存储映射，找到 Emby 数据目录对应的宿主机目录。常见容器路径为 `/config`，以实际映射为准。该目录内应有 `config`、`plugins` 等子目录。
3. 将整个 Emby 数据目录复制或快照到另外的位置。停止容器后再备份，避免复制到正在写入的数据库。媒体目录不属于本插件安装内容。
4. 如果以前安装过 StrmAssistant，将其 DLL 移到 `plugins` 目录以外留作备份，避免两个插件同时修改相同流程。不要覆盖 MovieDb、OpenSubtitles 等 Emby 原生插件。
5. 将安装包中的 `FilmIndex.Emby.dll` 放入数据目录的 `plugins` 子目录，然后启动原来的 Emby 容器。无需新建容器或更改镜像。
6. 在插件页面找到 Film Index Emby。新增功能默认关闭，按需启用；本地光盘提取需要探测程序包含 `bluray` 支持。

只有 DLL 是安装载荷。测试用的 `FilmIndex.IntegrationHarness.dll` 和 Emby 服务器 DLL 不得放进安装包，也不要复制到 NAS。

## 有终端时的安装脚本

脚本可在具备 `sh`、`sha256sum`、`cp`、`mv` 等常见工具的环境运行。路径填写 Emby 数据目录，不是媒体目录。先在飞牛界面停止容器，再运行：

```sh
sh film-index.sh install '/实际的/Emby数据目录' 4.10.1.0 --server-stopped
```

脚本校验包内 DLL 的 SHA-256，备份之前的插件 DLL 与 `plugins/configurations/Film Index Emby.json`，再原子替换 DLL。设置文件不在升级时重置。备份位于数据目录的 `film-index-backups` 子目录，终端会输出本次位置。

`--server-stopped` 表示执行者已停止服务器；脚本不会自动停止容器，也不能仅凭文件目录可靠判断容器是否运行。

## 回退

先停止容器。使用安装时输出的备份目录：

```sh
sh film-index.sh restore '/备份目录' '/实际的/Emby数据目录' --server-stopped
```

脚本校验备份，保存回退前的插件和设置，再恢复旧 DLL 与旧设置。首次安装前没有插件时，回退会恢复成未安装状态。

脚本备份只覆盖本插件及其设置，不包含 Emby 数据库、观看进度、图片缓存、本地元数据缓存、媒体信息侧车文件或媒体文件。若需要回退数据库中的进度隔离、刮削结果等变化，应恢复第 3 步保存的整个 Emby 数据目录及自行设置的外部缓存目录。

不能使用终端时，在停止容器后通过文件管理器恢复之前备份的 DLL 和设置即可；完整数据回退使用整个数据目录备份。

## 已知验收范围

安装/回退脚本已在隔离目录中使用 Windows BusyBox 执行检查；这不等于已完成飞牛 Linux 容器实机验收。本地未加密 Blu-ray ISO/BDMV 使用自生成光盘样本测试；远程 HTTP ISO、DVD、高级 DV P7 和光盘 BIF/章节截图仍不能宣称全部支持。
