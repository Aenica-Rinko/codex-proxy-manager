# Codex Proxy Manager

Windows 应用专用代理管理器 · v1.2.0 · Rinko · GPL-3.0-only。

双击本目录的 **CodexProxyManager.exe** 打开管理面板。打开面板不会自动启动代理或目标应用；主按钮按检查结果显示“启动应用并连接”“恢复代理”或处理说明。只有主动点击启动／恢复才开启会话。

## 第一次使用

1. 完整解压，保留 EXE 旁的 `YamlDotNet.dll`；需要 .NET Framework 4.7.2 或更高版本，不要在 ZIP 中直接运行。
2. 按 [快速入门](docs/QUICKSTART.md) 自行准备 `runtime/mihomo.exe`、`runtime/data/GeoSite.dat` 和 `runtime/data/geoip.metadb`。
3. 在管理器中导入自己的代理来源；本包不提供节点、订阅、账号或代理核心。
4. 首次使用先保存工作并完全退出普通方式打开的目标应用，再点击“启动应用并连接”。

本地文件入口：代理档案 → 节点来源 → 本地文件导入 → 选择 YAML/YML/TXT → 校验并预览导入 → 确认导入。取消不会新增档案，确认保存同一份已校验快照。保留内联节点、策略组及引用，不采用源文件的 TUN、DNS、监听端口和全局规则。依赖远程／外部文件 provider 的组需要先展开；其他客户端 JSON 不支持。原文件不修改，旧档案需重新导入才能采用新功能。详见 [策略组与故障切换](docs/STRATEGY_GROUPS.md)。

**恢复代理必须先从本管理器启动应用。之后应用保持运行，停止代理后再点“恢复代理”，才可免重启恢复。** 仅支持相同入口/隐私参数的兼容 Chromium/Electron 应用；普通方式打开的应用不能中途接管，环境变量模式、参数变更或检查失败仍需完全退出后从管理器启动。停止代理不关闭应用，也不自动直连。本地入口可用不等于目标网站可用或隐私验证通过。详见 [使用手册](docs/USER_GUIDE.md)。

## 目录说明

- `CodexProxyManager.exe`：唯一启动入口，版本更新不再更改文件名。
- `YamlDotNet.dll`：随包提供的固定版本 YAML 解析库；许可证位于 `third_party/YamlDotNet/LICENSE.txt`。
- `runtime/`：用户自行准备的核心和规则文件。
- `docs/`：使用、隐私、升级及技术文档。
- `tools/`：需要时手动使用的网络恢复工具，不会自动执行。
- `source/`：发布 ZIP 随附的完整对应源码与构建脚本；本地源码构建时，源码仍在仓库中，不重复复制进 app。
- `LICENSE`：完整 GPL 许可证；`manifest.json`：发布 ZIP 的文件校验清单。

个人设置、节点和日志仍保存在 `%LOCALAPPDATA%\CodexProxyManager`，不会随 EXE 改名而迁移或删除。不要上传这个私人数据目录。

## 注意

“管理应用”可保存 WebRTC 直连限制请求，以及可选浏览器 DNS 限制和关闭 QUIC。保存后完全退出目标应用，再手动启动。效果依赖应用支持，Electron/Codex 可能忽略参数；详见 [隐私说明](docs/PRIVACY.md)。

v0.11.0 使用固定左侧导航与顶部启动/停止操作。“隐私保护”中查看请求状态、运行兼容浏览器的隔离本地测试、管理日志；右下角“截图隐私模式”隐藏界面私人信息。新日志默认减少留存，但旧日志没有被追溯脱敏。没有凭据加密或系统级防直连保证。

v1.0.0 由维护者决定发布当前功能范围，不代表全面兼容或零泄漏认证。独立 Windows/DPI/真实共存验收尚未全部完成。已收到启动应用后商店或游戏联网变慢的反馈，尚未定位和修复。没有安装器、自动更新或代码签名；不要为了运行本项目关闭系统安全保护。

[详细手册](docs/USER_GUIDE.md) · [版本说明](docs/RELEASE_NOTES.md) · [项目与反馈](https://github.com/Aenica-Rinko/codex-proxy-manager)
