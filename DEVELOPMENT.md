# 开发与验证

面向维护者和贡献者。产品介绍见 [README](README.md)，使用步骤见 [QUICKSTART](QUICKSTART.md)。

## 源码结构

- `manager/AppInfo.cs`：版本号唯一来源，供窗口、诊断、程序集和构建脚本使用；
- `manager/GettingStartedForm.cs`：只读安装检查和首次使用指引；
- `manager/ControllerProbe.cs`、`manager/ControllerProbeTests.cs`：外部接口只读检查和异常/响应性测试；
- `manager/CodexProxyManager.cs`：面板、会话管理和 Windows 集成；
- `manager/OpenSourceFoundation.cs`：用户设置、档案解析和配置生成；
- `manager/ProfileManager.cs`：多档案界面、订阅和分享链接 provider 配置；
- `manager/ProcessProxyLauncher.cs`：已安装 Codex 定位、应用专用启动参数和子进程代理环境；
- `manager/ApplicationConfiguration.cs`：应用列表迁移、档案绑定、进程检测和会话规则生成；
- `manager/ApplicationManagerForm.cs`：应用管理界面；
- `manager/Diagnostics.cs`：只读状态采集、连接分类和脱敏报告生成；
- `manager/DiagnosticsForm.cs`：异步诊断、可选探测和本地导出界面；
- `manager/NodeCatalog.cs`：只影响显示的节点筛选排序、独立收藏存储；
- `manager/build.ps1`：本地构建脚本；
- `manager/build-common.ps1`：统一编译源文件清单和版本读取；
- `manager/package.ps1`、`manager/test-package.ps1`：明确名单打包、SHA-256 清单和边界回归；
- `manager/release-safety.ps1`、`manager/audit-release.ps1`、`manager/test-release-safety.ps1`：发布输入安全检查、Git 工作区候选文件审计及模拟凭据回归；
- `manager/OnboardingTests.cs`：首次引导和升级设置保留测试；
- `manager/test.ps1`、`manager/RegressionTests.cs`、`manager/DiagnosticsTests.cs`：隔离回归测试；
- `manager/NodeCatalogTests.cs`：搜索排序、收藏持久化/隔离、损坏文件保护和列表交互回归；
- `config.example.yaml`：无凭据的安全配置示例；
- `runtime/mihomo.exe`：当前本地运行所需的代理核心；
- `ARCHITECTURE.md`：后端适配和扩展边界；
- `THIRD_PARTY_NOTICES.md`：第三方组件和许可证说明。

## 构建

项目当前使用 Windows 自带的 .NET Framework C# 编译器，不需要安装 Visual Studio 或 .NET SDK：

```powershell
& '.\manager\build.ps1'
```

管理器正在运行时，Windows 会锁定主程序。可以输出到另一个文件进行验证：

```powershell
& '.\manager\build.ps1' -OutputName 'CodexProxyManager.test.exe'
```

运行 `& '.\manager\test.ps1'` 验证两种模式的真实子进程参数与环境继承、旧设置迁移与应用档案绑定、删除档案后的回退、Mihomo 启停和异常清理。新增应用流量测试会运行真实 Mihomo，把测试程序请求送到隔离的本地上游，验证命中分流规则且不修改原始档案。测试使用独立临时目录与端口，并检查 Windows Internet Settings 在前后保持一致。

界面可使用 `--applications-preview` 打开无私人数据的临时测试窗口。测试不代表每个第三方程序均支持这些代理方式。

主面板可使用 `--preview` 显示示例节点，不启动代理。可测试搜索、排序和收藏交互；预览中的收藏仅在内存中保存，不读取或修改真实收藏。

“开始使用 / 版本”按钮或 `--guide` 可以随时打开只读引导，不启动代理，也不会重新配置当前档案。

## 预发布打包（v0.10）

```powershell
& '.\manager\test-release-safety.ps1'
& '.\manager\audit-release.ps1' # 源码仓库中执行，需要 Git
& '.\manager\test.ps1' -UnitOnly
& '.\manager\test-package.ps1'
& '.\manager\package.ps1'
```

打包会从暂存的对应源码重新编译，并在 `dist` 生成仅管理器预发布 ZIP 和 `.sha256`。ZIP 仅含 EXE、LICENSE、指定文档、安全模板、恢复工具、明确列出的 `manager/` 源码及构建脚本与 `manifest.json`；不复制用户目录、核心、规则库、快捷方式或旧版本 EXE。已有同名包拒绝覆盖，使用 `-OutputDirectory` 可另选输出目录。

版本统一修改 `manager/AppInfo.cs`，EXE 默认文件名和程序集版本随之更新。文件哈希用于核对字节，不是发布者签名。打包名单也不能替代对文档和源码内容的人工敏感信息审计。

已提供 Windows GitHub Actions 构建配置，运行构建、安全检查、单元测试和打包检查，不使用私人节点、不运行需 Codex/核心的完整网络回归、不自动上传产物或创建 Release。v0.10.1、v0.10.2 功能提交已在真实 GitHub 私有仓库通过；每个后续提交仍须检查其对应结果。云端通过不代替真实用户与独立 Windows 验收。步骤写法参考 [checkout 官方说明](https://github.com/actions/checkout)。

完整发布边界和 v1.0 待办见 [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md)。

### 发布输入安全检查

打包会在复制前检查允许的输入，并在编译前再次检查暂存副本。发现私钥头、常见 GitHub/API token、携带凭据的代理分享链接、URL 用户名密码或长 token 查询参数时停止；同时拒绝私人配置/运行库路径、链接文件或链接父目录、目录穿越、不可读文件和不支持的文件类型。检查只读取文件，不访问节点或订阅，不改变代理。失败报告仅含输入序号、行号和规则名，不打印匹配内容或文件名。

`audit-release.ps1` 检查 Git 已跟踪文件及未被忽略的未跟踪文件的**当前工作区内容**；被强制跟踪的私人配置也会拒绝。它不检查暂存区旧内容、提交历史或 Git 未引用对象；无 Git 仓库时明确失败，不会假装完成审计。不能检测所有普通密码、UUID、私人地址、自定义/编码/拆分的秘密。通过检查不是“没有凭据”的证明，发布前仍需人工核对。

回归使用人工合成数据，覆盖允许名单内文档泄漏时阻止生成 ZIP、脱敏错误输出、中文及空格路径下解压重编译，以及 Git 忽略与强制跟踪边界。中文路径构建通过不等于已在全新用户或操作系统下验证全部界面和网络行为。

本轮本地验收结果及不能据此推断的事项见源码中的 [ACCEPTANCE.md](ACCEPTANCE.md)。它不是独立 Windows 或云端 CI 的通过证明。

诊断回归覆盖真实本机核心接口、错误鉴权、停止时端口占用、不自动发送外网探测、进程连接筛选、敏感信息排除和无响应接口超时。图形界面的报告保存还需手动或界面自动化验证。

参考：[Electron 代理启动参数](https://www.electronjs.org/docs/latest/api/command-line-switches/#--proxy-serveraddressport)、[Microsoft UWP 网络隔离说明](https://learn.microsoft.com/en-us/windows/uwp/communication/interprocess-communication)。

## 开源前检查

- 不要提交用户档案、日志、状态文件或编译产物；
- Git 提交前运行敏感信息扫描；
- 发布包若包含 Mihomo，需要履行其 GPL-3.0 再分发要求；
- 项目已选择 GPL-3.0-only，署名 Rinko；正式发布前验证许可与对应源码材料、版本化构建和 GitHub Actions；
- 如果凭据曾经进入 Git 历史，删除文件不够，必须更换相关密码、UUID或订阅地址。

后续计划包括多应用并发会话、安装程序、自动更新和 sing-box 后端适配。
