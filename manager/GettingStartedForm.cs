using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal static class InstallationCheck
    {
        internal static List<string> Missing(string root, string userRoot)
        {
            var missing = new List<string>();
            if (!Present(Path.Combine(root, "runtime", "mihomo.exe"))) missing.Add(@"runtime\mihomo.exe");
            foreach (string name in new[] { "GeoSite.dat", "geoip.metadb" })
                if (!Present(Path.Combine(root, "runtime", "data", name)) &&
                    !Present(Path.Combine(userRoot, "core-data", name))) missing.Add(@"runtime\data\" + name);
            return missing;
        }
        private static bool Present(string path)
        {
            try { return File.Exists(path) && new FileInfo(path).Length > 0; }
            catch { return false; }
        }
    }

    internal sealed class GettingStartedForm : Form
    {
        private readonly TextBox instructions;
        private readonly Button proceed;
        private readonly string root, userRoot;
        internal GettingStartedForm(string root, string userRoot, bool setup)
        {
            this.root = root;
            this.userRoot = userRoot;
            Text = "开始使用 · " + AppInfo.DisplayVersion;
            Size = new Size(850, 680);
            MinimumSize = new Size(720, 580);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "开始使用 " + AppInfo.DisplayVersion, Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 22F) }, 0, 0);
            instructions = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(22, 27, 34), ForeColor = ForeColor, AccessibleName = "安装检查与使用步骤" };
            layout.Controls.Add(instructions, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            var recheck = MakeButton("重新检查");
            recheck.Click += delegate { RefreshCheck(); };
            proceed = MakeButton("继续配置");
            proceed.Visible = setup;
            proceed.Click += delegate { RefreshCheck(); if (proceed.Enabled) { DialogResult = DialogResult.OK; Close(); } };
            var close = MakeButton("关闭");
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.AddRange(new Control[] { recheck, proceed, close });
            layout.Controls.Add(buttons, 0, 2);
            RefreshCheck();
            Shown += delegate { instructions.Select(0, 0); recheck.Focus(); };
        }
        private Button MakeButton(string text)
        {
            return new Button { Text = text, Width = 150, Height = 34, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 111, 235), ForeColor = Color.White };
        }
        private void RefreshCheck()
        {
            var missing = InstallationCheck.Missing(root, userRoot);
            proceed.Enabled = missing.Count == 0;
            var text = new StringBuilder();
            text.AppendLine("安装检查（只读，不下载、不启动代理、不改设置）");
            text.AppendLine(missing.Count == 0 ? "[通过] 必需文件已找到；仍需配置有效节点。" : "[待补齐] 以下文件缺失或为空：");
            foreach (string item in missing) text.AppendLine("  • " + item);
            text.AppendLine("只检查文件存在且非空，不验证真伪、兼容性或外网连通性。");
            text.AppendLine();
            text.AppendLine("1. 准备运行文件");
            text.AppendLine("解压到可管理的目录。仅管理器预发布包不含核心和规则库。");
            text.AppendLine("按随包 QUICKSTART.md 的来源说明准备 Windows x64 核心和规则文件，放入上述位置，再点重新检查。");
            text.AppendLine();
            text.AppendLine("2. 选择你的代理来源");
            text.AppendLine("• 有订阅 URL 或分享链接：在档案管理器选择对应来源，无需同时运行其他客户端。");
            text.AppendLine("• 已有代理软件：选择本地 HTTP/SOCKS5 端口，上游软件须保持运行。");
            text.AppendLine("• 想控制外部客户端节点：选择本机 Clash/Mihomo API，填写端口、控制密钥和策略组。");
            text.AppendLine();
            text.AppendLine("3. 选择应用并启动");
            text.AppendLine("新用户首次保存后先进入管理面板，不自动启动应用。默认入口是 Codex，也可以在管理应用中添加兼容的 EXE。");
            text.AppendLine("完全退出目标应用，再点击启动应用。只支持代理参数/环境变量方式，不是任意游戏或 UWP 的透明代理。");
            text.AppendLine();
            text.AppendLine("4. 验证与排障");
            text.AppendLine("查看连接状态和节点列表；异常时用连接诊断。节点探测成功不等于应用可登录。");
            text.AppendLine("管理器不修改 Windows 系统代理；其他客户端自己的系统代理/TUN 不由本项目控制。");
            text.AppendLine();
            text.AppendLine("配置、订阅和收藏保存在 %LOCALAPPDATA%\\CodexProxyManager，不应上传到 GitHub。");
            text.AppendLine("作者：Rinko。许可证：GNU GPL 第 3 版（GPL-3.0-only），详见程序目录 LICENSE。");
            text.AppendLine("本程序不提供任何担保；允许依 GPL 再分发和修改，对应源码及构建脚本随包提供。");
            text.AppendLine("本版是发布准备版本，尚未完成干净 Windows 环境验收。");
            instructions.Text = text.ToString();
            instructions.Select(0, 0);
            instructions.ScrollToCaret();
        }
    }
}
