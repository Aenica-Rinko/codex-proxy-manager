# 文件结构与迁移

v0.10.3 把源码仓库、日常应用和历史产物分开；它仍是免安装便携程序，不是系统安装器。

## 本地源码仓库

```text
项目目录/
├── Codex Proxy Manager.lnk      日常启动快捷方式（仅本机）
├── app/                        可直接使用的应用（不提交 Git）
│   ├── CodexProxyManager.exe    固定文件名，版本在程序内查看
│   ├── runtime/                核心及规则库（本机自行准备）
│   ├── docs/                   随应用提供的文档
│   └── tools/                  手动恢复工具
├── src/CodexProxyManager/       产品 C# 源码
├── tests/CodexProxyManager.Tests/ 隔离测试代码
├── scripts/                    构建、测试、打包、迁移脚本
├── docs/                       使用与开发文档
├── examples/                   无私人凭据的示例
├── resources/                  打包专用说明资源
├── tools/                      手动维护工具源码
├── artifacts/                  发布包及可恢复历史归档（不提交 Git）
├── .github/                    持续集成
├── README.md                   项目入口
└── LICENSE                     开源许可证
```

日常只需快捷方式或 `app/CodexProxyManager.exe`。请勿单独搬走 EXE；需要换位置时一起移动 app 目录，再更新自己的快捷方式。

## 下载给普通用户的 ZIP

解压后 EXE 直接在包的根目录，不再让用户面对散落的源文件、测试和版本 EXE：

```text
CodexProxyManager.exe
README.md
LICENSE
docs/
tools/
source/       完整对应源码，保留 src/tests/scripts 等开发结构
manifest.json
```

核心和规则仍需按快速入门放进 EXE 旁的 `runtime/`，不会自动下载或捆绑。源码 ZIP（GitHub Source code）与可运行的管理器 ZIP 不同。

## 用户数据不随代码搬迁

设置、节点、订阅、收藏、缓存及日志仍位于 `%LOCALAPPDATA%\CodexProxyManager`。本次不改变数据格式、不清空配置、不把个人数据复制到仓库或发布包。不要公开此目录。

## 从旧本地源码目录整理

保存工作，在托盘退出旧管理器（关闭窗口只是最小化），在仓库根目录执行：

```powershell
& '.\scripts\build.ps1'
& '.\scripts\migrate-layout.ps1' -ArchiveLegacy
```

迁移先校验并复制三项运行文件，再生成固定快捷方式。旧的版本 EXE、runtime、dist、旧快捷方式和残留 manager 目录会移动到 `artifacts/legacy-layout/时间戳/`，不直接删除，必要时可取回。目标运行文件已存在但内容不同时会停止，不覆盖；发现运行中进程、无法确认的进程路径或目录链接时拒绝归档。

不加 `-ArchiveLegacy` 只准备新的应用运行文件与快捷方式，不归档旧目录。此脚本不关闭应用、不启动核心、不修改系统代理，不在后台计划迁移。请勿在迁移期间启动另一份管理器。

普通用户升级无需运行源码迁移脚本：完整解压新版、准备运行文件、更新快捷方式即可。
