# Codex Proxy Manager

Windows 应用专用代理管理器 · Rinko · GPL-3.0-only。

双击本目录的 **CodexProxyManager.exe** 打开管理面板。打开面板不会自动启动代理或目标应用，只有主动点击“启动应用”才会启动。

## 第一次使用

1. 完整解压，不要在 ZIP 中直接运行。
2. 按 [快速入门](docs/QUICKSTART.md) 自行准备 `runtime/mihomo.exe`、`runtime/data/GeoSite.dat` 和 `runtime/data/geoip.metadb`。
3. 在管理器中导入自己的代理来源；本包不提供节点、订阅、账号或代理核心。
4. 完全退出目标应用，再点击“启动应用”。

## 目录说明

- `CodexProxyManager.exe`：唯一启动入口，版本更新不再更改文件名。
- `runtime/`：用户自行准备的核心和规则文件。
- `docs/`：使用、隐私、升级及技术文档。
- `tools/`：需要时手动使用的网络恢复工具，不会自动执行。
- `source/`：发布 ZIP 随附的完整对应源码与构建脚本；本地源码构建时，源码仍在仓库中，不重复复制进 app。
- `LICENSE`：完整 GPL 许可证；`manifest.json`：发布 ZIP 的文件校验清单。

个人设置、节点和日志仍保存在 `%LOCALAPPDATA%\CodexProxyManager`，不会随 EXE 改名而迁移或删除。不要上传这个私人数据目录。

## 注意

当前仍为预览版。已收到启动应用后商店或游戏联网变慢的反馈，尚未定位和修复。没有安装器、自动更新或代码签名；不要为了运行本项目关闭系统安全保护。

[详细手册](docs/USER_GUIDE.md) · [版本说明](docs/RELEASE_NOTES.md) · [项目与反馈](https://github.com/Aenica-Rinko/codex-proxy-manager)
