// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class StrategyGroupsForm : Form
    {
        private readonly NodeControlService service;
        private readonly ComboBox groups;
        private readonly ListBox members;
        private readonly Label status;
        private readonly Button select;
        private bool pending;
        internal StrategyGroupsForm(NodeControlService service)
        {
            this.service = service;
            Text = "策略组与引用"; StartPosition = FormStartPosition.CenterParent;
            Size = new Size(700, 520); MinimumSize = new Size(550, 420);
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(22, 27, 34); ForeColor = Color.FromArgb(230, 237, 243);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 5, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "查看组内节点与下级组；选择其他组查看其引用。\n修改下级组不会改变入口组；自动策略组由核心选择线路。" }, 0, 0);
            groups = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "策略组" };
            members = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, AccessibleName = "组成员及引用" };
            status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
            select = new Button { Dock = DockStyle.Fill, Text = "将选中成员设为此组出口", Enabled = false };
            layout.Controls.Add(groups, 0, 1); layout.Controls.Add(members, 0, 2); layout.Controls.Add(status, 0, 3); layout.Controls.Add(select, 0, 4);
            Controls.Add(layout);
            groups.SelectedIndexChanged += delegate { RenderGroup(); };
            members.SelectedIndexChanged += delegate { UpdateSelection(); };
            members.DoubleClick += delegate {
                string target = members.SelectedItem as string;
                foreach (StrategyGroupInfo group in groups.Items) if (group.Name == target) { groups.SelectedItem = group; break; }
            };
            select.Click += async delegate {
                var group = groups.SelectedItem as StrategyGroupInfo; string target = members.SelectedItem as string;
                if (pending || group == null || target == null) return;
                pending = true; groups.Enabled = members.Enabled = select.Enabled = false;
                try { await Task.Run(delegate { service.Switch(group.Name, target); }); status.Text = "选择已保存；连接将重新建立。"; }
                catch { status.Text = "切换未完成，请刷新后检查组成员和连接状态。"; }
                finally { pending = false; groups.Enabled = members.Enabled = true; }
                await RefreshGroups(group.Name);
            };
            Shown += async delegate { await RefreshGroups(null); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (pending) e.Cancel = true; };
        }
        private async Task RefreshGroups(string previous)
        {
            pending = true; select.Enabled = false;
            try {
                var data = await Task.Run(delegate { return service.ReadGroups(); });
                groups.Items.Clear(); foreach (var group in data) groups.Items.Add(group);
                groups.SelectedItem = data.FirstOrDefault(g => g.Name == previous) ?? data.FirstOrDefault();
                if (data.Count == 0) status.Text = "没有可查看的策略组。";
            } catch { status.Text = "读取策略组失败，请关闭后重试。"; }
            finally { pending = false; UpdateSelection(); }
        }
        private void RenderGroup()
        {
            members.Items.Clear(); var group = groups.SelectedItem as StrategyGroupInfo;
            if (group == null) return;
            foreach (string member in group.Members) members.Items.Add(member);
            if (members.Items.Contains(group.Current)) members.SelectedItem = group.Current;
            status.Text = "当前出口：" + (String.IsNullOrEmpty(group.Current) ? "由核心按连接分配" : group.Current) +
                (group.Type == "Selector" ? "\n双击组名可查看下级引用；选择成员后可手动切换。" : "\n自动组仅查看，按导入的策略与健康检查参数运行。");
            UpdateSelection();
        }
        private void UpdateSelection()
        {
            var group = groups.SelectedItem as StrategyGroupInfo;
            select.Enabled = !pending && group != null && group.Type == "Selector" && members.SelectedItem != null &&
                (string)members.SelectedItem != group.Current;
        }
    }
}
