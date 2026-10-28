// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class ImportPreviewForm : Form
    {
        internal ImportPreviewForm(LocalImportPreview preview)
        {
            Text = "本地文件导入 · 确认预览";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(740, 540);
            ClientSize = new Size(820, 620);
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(22, 27, 34);
            ForeColor = Color.FromArgb(230, 237, 243);
            MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);
            string route = preview.Topology == null ? "分享链接 / Base64：由核心提供节点；在概览中选择" : preview.Topology.InitialRoute;
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "识别结果：" + preview.NodeCount + " 个节点 · " + preview.GroupCount + " 个原策略组\r\n管理器入口：" + preview.EntryGroup +
                    "\r\n初始入口成员：" + route + "（仅说明新档案初始顺序，不修改已有档案）" }, 0, 0);
            var groups = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                BackColor = BackColor, ForeColor = ForeColor, ShowItemToolTips = true, AccessibleName = "将保留的策略组与成员引用" };
            groups.Columns.Add("原策略组", 180); groups.Columns.Add("类型", 110); groups.Columns.Add("成员引用（按原顺序）", 450);
            if (preview.Topology != null)
                foreach (string[] row in preview.Topology.DescribeGroups())
                    groups.Items.Add(new ListViewItem(row) { ToolTipText = row[2] });
            if (preview.GroupCount == 0) groups.Items.Add(new ListViewItem(new[] { "无原策略组", "—", "将生成管理器选择入口，节点仍完整导入。" }));
            layout.Controls.Add(groups, 0, 1);
            string ignored = preview.Topology == null ? "不适用（节点文本）" :
                String.IsNullOrEmpty(preview.Topology.IgnoredFields) ? "无其他顶层字段" : preview.Topology.IgnoredFields;
            layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = BackColor, ForeColor = ForeColor, BorderStyle = BorderStyle.None,
                Text = "未沿用的源文件顶层设置：" + ignored + "。\r\n仅节点、支持的策略组及其引用进入运行配置；DNS、TUN、端口和分流规则由管理器控制。外部依赖或不支持的组字段会阻止导入，不会静默删除引用。\r\n校验通过只表示核心接受配置，不保证节点联网或隐私安全。自动测试组可能在启动代理后按原策略请求测试地址。\r\n确认后保存本次校验的快照；源文件后续变化不会自动同步。原文件及已有档案不变，取消不会新增档案。" }, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var confirm = new UiButton { Text = "确认导入", Width = 130, Height = 36, DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(31, 111, 235), ForeColor = Color.White };
            var cancel = new UiButton { Text = "取消", Width = 100, Height = 36, DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(48, 54, 61), ForeColor = Color.White };
            actions.Controls.Add(confirm); actions.Controls.Add(cancel); layout.Controls.Add(actions, 0, 3);
            AcceptButton = confirm; CancelButton = cancel;
        }
    }
}
