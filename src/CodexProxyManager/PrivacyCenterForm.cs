// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class PrivacyCenterForm : Form
    {
        private readonly Func<ManagedApplication> application;
        private readonly Func<bool> active;
        private readonly DashboardForm dashboard;
        private readonly Label appLabel, rtcState, dnsState, quicState, result;
        private readonly Button edit, probe, logs, clear;
        private readonly CheckBox screenshot;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool running;

        internal PrivacyCenterForm(Func<ManagedApplication> application, Func<bool> active,
            string logsDirectory, DashboardForm dashboard, Action editApplication)
        {
            this.application = application;
            this.active = active;
            this.dashboard = dashboard;
            Text = "隐私保护 · " + AppInfo.DisplayVersion;
            ClientSize = new Size(880, 780);
            MinimumSize = new Size(800, 800);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font("Microsoft YaHei UI", 10.5F);
            AutoScaleMode = AutoScaleMode.Font;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24),
                ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "隐私保护", AutoSize = true, Font = new Font(Font.FontFamily, 23F, FontStyle.Bold) });
            appLabel = new Label { AutoSize = true, Location = new Point(2, 50), ForeColor = Color.FromArgb(139, 148, 158) };
            header.Controls.Add(appLabel);
            layout.Controls.Add(header, 0, 0);
            var network = Section("网络保护状态");
            rtcState = StateRow(network, "WebRTC 直连限制", 43);
            dnsState = StateRow(network, "浏览器 DNS 限制", 78);
            quicState = StateRow(network, "QUIC / HTTP3", 113);
            Note(network, "启动参数不代表实际阻断；Codex / Electron 的行为尚未验证。", 150, 34);
            edit = ButtonAt(network, "编辑应用隐私选项", 0, 190, 200);
            probe = ButtonAt(network, "运行本地 WebRTC 测试", 214, 190, 240);
            Note(network, "本地测试仅支持 Chrome / Edge，使用隔离浏览器，不连接公网 STUN。", 239, 42);
            layout.Controls.Add(network, 0, 1);
            var data = Section("日志与本机数据");
            Note(data, "新日志仅保存固定事件和错误类别，不保存节点名称、地址、订阅和原始错误。", 42, 34);
            Note(data, "新日志上限：每份 1 MB + 一份轮转备份 · 最多保留 7 天。旧日志未自动脱敏。", 76, 30);
            logs = ButtonAt(data, "打开日志目录", 0, 111, 200);
            clear = ButtonAt(data, "清理本项目日志", 214, 111, 240);
            layout.Controls.Add(data, 0, 2);
            var screen = Section("截图隐私模式");
            screenshot = new CheckBox { Text = "隐藏节点名称、档案名称、应用名称和个人路径", Location = new Point(0, 42),
                Width = 730, Height = 28, Checked = dashboard.ScreenshotPrivacy };
            screen.Controls.Add(screenshot);
            Note(screen, "仅隐藏本项目界面信息，不修改节点或路由；开启时管理窗口会受限。", 73, 40);
            layout.Controls.Add(screen, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            result = new Label { Text = "效果未验证", Dock = DockStyle.Fill, AutoEllipsis = true,
                ForeColor = Color.FromArgb(139, 148, 158), TextAlign = ContentAlignment.MiddleLeft };
            footer.Controls.Add(result, 0, 0);
            var close = new UiButton { Text = "关闭", Dock = DockStyle.Top, Height = 38, Margin = new Padding(0, 14, 0, 0),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 111, 235), ForeColor = ForeColor };
            close.Click += delegate { Close(); };
            footer.Controls.Add(close, 1, 0);
            layout.Controls.Add(footer, 0, 4);
            edit.Click += delegate { editApplication(); RefreshStates(); };
            screenshot.CheckedChanged += delegate { dashboard.SetScreenshotPrivacy(screenshot.Checked); RefreshStates(); };
            logs.Click += delegate
            {
                try { Process.Start("explorer.exe", logsDirectory); }
                catch { result.Text = "日志目录无法打开。"; }
            };
            clear.Click += delegate
            {
                if (active()) return;
                if (MessageBox.Show(this, "清理本项目三份日志及各自的轮转备份？删除后不能从程序中恢复。节点、设置和其他文件会保留。",
                    "清理日志", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { int count = PrivacyLog.Clear(logsDirectory); result.Text = "已清理 " + count + " 份日志；其他文件保留。"; }
                catch { result.Text = "清理未完成；请检查目录权限，程序不会删除链接或其他文件。"; }
            };
            probe.Click += async delegate { await RunProbe(); };
            FormClosed += delegate { cancellation.Cancel(); };
            RefreshStates();
        }

        private Panel Section(string title)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
            panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font.FontFamily, 13F, FontStyle.Bold) });
            panel.Paint += delegate(object sender, PaintEventArgs e)
            { using (var pen = new Pen(Color.FromArgb(48, 54, 61))) e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1); };
            return panel;
        }

        private Label StateRow(Panel panel, string title, int y)
        {
            panel.Controls.Add(new Label { Text = title, Location = new Point(0, y), Width = 300, Height = 30 });
            var state = new Label { Location = new Point(355, y), Width = 400, Height = 30, AutoEllipsis = true };
            panel.Controls.Add(state);
            return state;
        }

        private void Note(Panel panel, string text, int y, int height)
        {
            panel.Controls.Add(new Label { Text = text, Location = new Point(0, y), Width = 810, Height = height,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, ForeColor = Color.FromArgb(139, 148, 158) });
        }

        private Button ButtonAt(Panel panel, string text, int x, int y, int width)
        {
            var button = new UiButton { Text = text, Location = new Point(x, y), Width = width, Height = 36,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 54, 61), ForeColor = ForeColor };
            panel.Controls.Add(button);
            return button;
        }

        private void RefreshStates()
        {
            ManagedApplication app = application();
            bool supported = app != null && app.LaunchMode == "chromium";
            appLabel.Text = "当前应用：" + (dashboard.ScreenshotPrivacy ? "应用已隐藏" : app == null ? "未选择" : app.Name);
            string state = active() ? "已请求 · 行为未验证" : "已配置 · 下次启动生效";
            rtcState.Text = !supported ? "不支持此启动模式" : PrivacyProtection.WebRtcRequested(app) ? state : "未启用";
            dnsState.Text = !supported ? "不支持此启动模式" : app.RestrictBrowserDns ? state : "未启用";
            quicState.Text = !supported ? "不支持此启动模式" : app.DisableQuic ? state : "未启用";
            edit.Enabled = !running && !active() && !dashboard.ScreenshotPrivacy;
            probe.Enabled = !running && PrivacyProtection.WebRtcRequested(app) && BrowserPrivacyProbe.Supports(app);
            clear.Enabled = !running && !active();
            logs.Enabled = !running && !dashboard.ScreenshotPrivacy;
            screenshot.Enabled = !running;
        }

        private async Task RunProbe()
        {
            if (running) return;
            ManagedApplication app = application();
            running = true;
            RefreshStates();
            result.Text = "正在运行隔离本地测试；不读取已有浏览器配置…";
            try
            {
                BrowserProbeResult checkedResult = await Task.Run(delegate { return BrowserPrivacyProbe.Run(app, cancellation.Token); });
                if (IsDisposed) return;
                result.Text = checkedResult.Message;
                result.ForeColor = checkedResult.Passed ? Color.FromArgb(63, 185, 80) : Color.FromArgb(255, 166, 87);
                if (checkedResult.Passed) rtcState.Text = "隔离本地测试通过 · 当前会话未验证";
            }
            catch (OperationCanceledException) { if (!IsDisposed) result.Text = "测试取消或超时；效果未验证。"; }
            catch { if (!IsDisposed) result.Text = "本地测试无法完成；效果未验证。"; }
            finally
            {
                running = false;
                if (!IsDisposed) { bool wasPassed = rtcState.Text.StartsWith("隔离本地测试通过", StringComparison.Ordinal); RefreshStates();
                    if (wasPassed) rtcState.Text = "隔离本地测试通过 · 当前会话未验证"; }
            }
        }
    }
}
