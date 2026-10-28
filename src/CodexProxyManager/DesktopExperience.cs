// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class PanelTheme
    {
        internal bool Light;
        private static readonly Color[] Dark = { Color.FromArgb(13,17,23), Color.FromArgb(22,27,34), Color.FromArgb(33,38,45),
            Color.FromArgb(48,54,61), Color.FromArgb(26,55,82), Color.FromArgb(230,237,243), Color.FromArgb(139,148,158),
            Color.FromArgb(170,185,202), Color.FromArgb(63,185,80), Color.FromArgb(210,160,70), Color.White };
        private static readonly Color[] Bright = { Color.FromArgb(244,246,249), Color.FromArgb(255,255,255), Color.FromArgb(232,237,243),
            Color.FromArgb(223,229,237), Color.FromArgb(216,232,252), Color.FromArgb(25,34,46), Color.FromArgb(84,98,116),
            Color.FromArgb(67,85,108), Color.FromArgb(24,116,48), Color.FromArgb(133,91,15), Color.FromArgb(27,36,48) };
        internal Color ColorFor(Color color)
        {
            for (int i = 0; i < Dark.Length; i++)
                if (color.ToArgb() == (Light ? Dark[i] : Bright[i]).ToArgb()) return Light ? Bright[i] : Dark[i];
            return color;
        }
        internal void Apply(Control root)
        {
            // White is both a legacy text color and the light surface; map these separately.
            if (Light && root.BackColor.ToArgb() == Color.White.ToArgb()) { }
            else root.BackColor = ColorFor(root.BackColor);
            var button = root as UiButton;
            if (button != null && button.LightTheme != Light) { button.LightTheme = Light; button.Invalidate(); }
            root.ForeColor = button != null && root.BackColor.ToArgb() == Color.FromArgb(31,111,235).ToArgb()
                ? Color.White : ColorFor(root.ForeColor);
            foreach (Control child in root.Controls) Apply(child);
        }
    }

    internal sealed class ThemePreference
    {
        private readonly string path;
        internal ThemePreference(string userRoot) { path = StartupConfiguration.ResolveUserPath(userRoot, "ui-theme.txt"); }
        internal bool Read()
        {
            if (!PrivacyLog.SafePath(path)) throw new IOException();
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length > 16) throw new InvalidDataException();
            string value = File.ReadAllText(path).Trim();
            if (value != "light" && value != "dark") throw new InvalidDataException();
            return value == "light";
        }
        internal void Save(bool light)
        {
            Read(); // Do not overwrite a corrupt or linked preference file.
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)))
                    writer.Write(light ? "light" : "dark");
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary) && PrivacyLog.SafePath(temporary)) File.Delete(temporary); }
        }
    }

    internal enum SessionEvent { Started, Resumed, Stopped, NodeChanged, ProfileChanged, ProvidersUpdated, Failed, ThemeNotSaved }
    internal sealed class SessionHistory
    {
        private readonly Queue<string> entries = new Queue<string>();
        internal void Add(SessionEvent kind)
        {
            string message;
            switch (kind)
            {
                case SessionEvent.Started: message = "代理会话已启动；实际网站与隐私效果未验证。"; break;
                case SessionEvent.Resumed: message = "代理会话已恢复，未重启应用。"; break;
                case SessionEvent.Stopped: message = "代理会话已停止，应用未关闭。"; break;
                case SessionEvent.NodeChanged: message = "节点选择已更新；已有请求可能需要重连。"; break;
                case SessionEvent.ProfileChanged: message = "当前代理档案已更新。"; break;
                case SessionEvent.ProvidersUpdated: message = "远程订阅刷新完成。"; break;
                case SessionEvent.ThemeNotSaved: message = "主题偏好无法保存；请检查用户目录权限或偏好文件。"; break;
                default: message = "操作失败；请使用连接诊断。私人详情不记录。"; break;
            }
            if (entries.Count >= 100) entries.Dequeue();
            entries.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
        }
        internal string[] Snapshot() { var result = entries.ToArray(); Array.Reverse(result); return result; }
        internal void Clear() { entries.Clear(); }
    }

    internal sealed class SessionHistoryForm : Form
    {
        internal SessionHistoryForm(SessionHistory history, bool light)
        {
            Text = "本次会话记录"; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(740, 460); MinimumSize = new Size(600, 360);
            Font = new Font("Microsoft YaHei UI", 10F); AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(22,27,34); ForeColor = Color.FromArgb(230,237,243);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); Controls.Add(layout);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "最近 100 条 · 仅内存保存，退出后清空；不含节点、地址或凭据。\r\n此窗口是打开时的快照，不代表目标网站连通性。" }, 0, 0);
            var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, BackColor = BackColor, ForeColor = ForeColor };
            Action reload = delegate { list.Items.Clear(); list.Items.AddRange(history.Snapshot()); if (list.Items.Count == 0) list.Items.Add("本次运行暂无事件。"); };
            reload(); layout.Controls.Add(list, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new UiButton { Text = "关闭", Width = 100, Height = 34, DialogResult = DialogResult.Cancel, BackColor = Color.FromArgb(48,54,61), ForeColor = Color.White };
            var clear = new UiButton { Text = "清空记录", Width = 120, Height = 34, BackColor = Color.FromArgb(48,54,61), ForeColor = Color.White };
            clear.Click += delegate { history.Clear(); reload(); };
            actions.Controls.Add(close); actions.Controls.Add(clear); layout.Controls.Add(actions, 0, 2); CancelButton = close;
            if (light) new PanelTheme { Light = true }.Apply(this);
        }
    }
}
