using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class DashboardForm : Form
    {
        internal event EventHandler StartRequested;
        internal event EventHandler StopRequested;
        internal event EventHandler TestRequested;
        internal event EventHandler FastestRequested;
        internal event EventHandler RefreshProvidersRequested;
        internal event EventHandler ProfilesRequested;
        internal event EventHandler ApplicationsRequested;
        internal event EventHandler DiagnosticsRequested;
        internal event EventHandler GroupsRequested;
        internal event EventHandler PrivacyRequested;
        internal event EventHandler ScreenshotPrivacyChanged;
        internal event EventHandler ExitRequested;
        internal event Action<string> NodeSwitchRequested;

        private readonly Label statusValue;
        private readonly Label nodeValue;
        private readonly Label downloadValue;
        private readonly Label uploadValue;
        private readonly Label downloadTotalValue;
        private readonly Label uploadTotalValue;
        private readonly Label connectionsValue;
        private readonly Label footerValue;
        private readonly ListView nodeList;
        private readonly TextBox nodeSearch;
        private readonly ComboBox nodeSort;
        private readonly CheckBox favoritesOnly;
        private readonly Button favoriteButton;
        private readonly Label nodeCount;
        private List<NodeInfo> catalog = new List<NodeInfo>();
        private HashSet<string> favorites = new HashSet<string>(StringComparer.Ordinal);
        private NodePreferences nodePreferences;
        private string catalogCurrent;
        private readonly Button startButton;
        private readonly Button stopButton;
        private readonly Button switchButton;
        private readonly Button testButton;
        private readonly Button fastestButton;
        private readonly Button refreshButton;
        private readonly Button profilesButton;
        private readonly Button applicationsButton;
        private readonly Button diagnosticsButton;
        private readonly Label applicationValue;
        private readonly Label readinessValue;
        private LaunchReadiness readiness = LaunchReadiness.Checking();
        private readonly CheckBox screenshotBox;
        private readonly Button guideButton;
        private bool screenshotPrivacy;
        private string applicationName = "Codex";
        internal bool ScreenshotPrivacy { get { return screenshotPrivacy; } }
        private string localProxyEndpoint = "127.0.0.1:17890";
        private string activeProfileName = "默认档案";
        private bool busy;
        private bool active;
        private readonly TableLayoutPanel mainLayout;
        private readonly Control overviewSummary, nodesPanel, statsPanel;
        private readonly Label pageTitle, profileSummary, nodePageHint;
        private readonly Button overviewNav, nodesNav, groupsButton;
        private readonly Button themeButton, historyButton;
        private readonly PanelTheme theme = new PanelTheme();
        private readonly SessionHistory history = new SessionHistory();
        private ThemePreference themePreference;
        private float layoutScale = 1F;

        internal DashboardForm()
        {
            Text = AppInfo.Title;
            Icon = SystemIcons.Shield;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1020, 720);
            ClientSize = new Size(1100, 780);
            MaximizeBox = true;
            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font("Microsoft YaHei UI", 10.5F);
            AutoScaleMode = AutoScaleMode.Font;
            DoubleBuffered = true;
            KeyPreview = true;

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            var rail = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 27, 34),
                Padding = new Padding(16, 24, 16, 14), ColumnCount = 1, RowCount = 11 };
            rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            for (int i = 1; i <= 7; i++) rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            shell.Controls.Add(rail, 0, 0);
            var brand = new Panel { Dock = DockStyle.Fill };
            brand.Controls.Add(new Label { Text = "Codex Proxy\nManager", Dock = DockStyle.Top, Height = 60,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = ForeColor });
            brand.Controls.Add(new Label { Text = AppInfo.DisplayVersion, AutoSize = true,
                Font = new Font(Font.FontFamily, 9F), Location = new Point(0, 70), ForeColor = Color.FromArgb(139, 148, 158) });
            rail.Controls.Add(brand, 0, 0);
            var overview = NavButton("连接概览");
            overviewNav = overview;
            overview.BackColor = Color.FromArgb(26, 55, 82);
            rail.Controls.Add(overview, 0, 1);
            var privacy = NavButton("隐私保护");
            nodesNav = NavButton("节点与策略");
            rail.Controls.Add(nodesNav, 0, 2);
            rail.Controls.Add(privacy, 0, 3);
            nodesNav.Click += delegate { ShowNodesPage(true); };
            applicationsButton = NavButton("管理应用");
            profilesButton = NavButton("代理档案");
            diagnosticsButton = NavButton("连接诊断");
            rail.Controls.Add(applicationsButton, 0, 4);
            rail.Controls.Add(profilesButton, 0, 5);
            rail.Controls.Add(diagnosticsButton, 0, 6);
            themeButton = NavButton("切换浅色主题");
            rail.Controls.Add(themeButton, 0, 7);
            themeButton.Click += delegate { SetLightTheme(!theme.Light); };
            groupsButton = MakeButton("策略组与引用", Color.FromArgb(48, 54, 61));
            groupsButton.Click += delegate { if (GroupsRequested != null) GroupsRequested(this, EventArgs.Empty); };
            var guide = NavButton("帮助与版本");
            guideButton = guide;
            var exit = NavButton("退出管理器");
            rail.Controls.Add(guide, 0, 9);
            rail.Controls.Add(exit, 0, 10);
            overview.Click += delegate { ShowNodesPage(false); };
            privacy.Click += delegate { if (PrivacyRequested != null) PrivacyRequested(this, EventArgs.Empty); };
            applicationsButton.Click += delegate { if (ApplicationsRequested != null) ApplicationsRequested(this, EventArgs.Empty); };
            profilesButton.Click += delegate { if (ProfilesRequested != null) ProfilesRequested(this, EventArgs.Empty); };
            diagnosticsButton.Click += delegate { if (DiagnosticsRequested != null) DiagnosticsRequested(this, EventArgs.Empty); };
            guide.Click += delegate { using (var help = new GettingStartedForm(AppDomain.CurrentDomain.BaseDirectory,
                StartupConfiguration.GetUserDataRoot(), false)) help.ShowDialog(this); };
            exit.Click += delegate { if (ExitRequested != null) ExitRequested(this, EventArgs.Empty); };

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                Padding = new Padding(24, 20, 24, 12) };
            mainLayout = main;
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            shell.Controls.Add(main, 1, 0);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            var title = new Panel { Dock = DockStyle.Fill };
            pageTitle = new Label { Text = "连接概览", AutoSize = true,
                Font = new Font(Font.FontFamily, 23F, FontStyle.Bold), Location = new Point(0, 0) };
            title.Controls.Add(pageTitle);
            applicationValue = new Label { Text = "当前应用：Codex", AutoSize = true, Location = new Point(2, 51),
                ForeColor = Color.FromArgb(139, 148, 158) };
            title.Controls.Add(applicationValue);
            header.Controls.Add(title, 0, 0);
            startButton = MakeButton("启动 / 恢复", Color.FromArgb(31, 111, 235));
            stopButton = MakeButton("停止代理", Color.FromArgb(48, 54, 61));
            startButton.Dock = stopButton.Dock = DockStyle.Top;
            startButton.Height = stopButton.Height = 40;
            startButton.Margin = stopButton.Margin = new Padding(6, 16, 0, 0);
            header.Controls.Add(startButton, 1, 0);
            header.Controls.Add(stopButton, 2, 0);
            startButton.Click += delegate { if (StartRequested != null) StartRequested(this, EventArgs.Empty); };
            stopButton.Click += delegate { if (StopRequested != null) StopRequested(this, EventArgs.Empty); };
            main.Controls.Add(header, 0, 0);
            readinessValue = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                ForeColor = Color.FromArgb(170, 185, 202), AccessibleName = "应用启动条件与连接验证说明",
                Margin = new Padding(0, 0, 0, 10) };
            main.Controls.Add(readinessValue, 0, 1);

            var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1,
                BackColor = Color.FromArgb(22, 27, 34), Margin = new Padding(0, 0, 0, 18) };
            statsPanel = stats;
            foreach (float width in new[] { 19F, 27F, 19F, 19F, 16F })
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            statusValue = AddCard(stats, 0, "本地代理入口", "已停止", ForeColor);
            nodeValue = AddCard(stats, 1, "当前节点", "—", ForeColor);
            downloadValue = AddCard(stats, 2, "下载速度", "0 B/s", ForeColor);
            uploadValue = AddCard(stats, 3, "上传速度", "0 B/s", ForeColor);
            connectionsValue = AddCard(stats, 4, "活动连接", "0", ForeColor);
            main.Controls.Add(stats, 0, 2);

            var nodes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                BackColor = Color.FromArgb(22, 27, 34), Padding = new Padding(16, 12, 16, 0) };
            nodesPanel = nodes;
            nodes.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            nodes.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            nodes.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            nodes.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            nodes.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            var pageBody = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            main.Controls.Add(pageBody, 0, 3);
            pageBody.Controls.Add(nodes);
            var nodeHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            nodeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nodeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            nodePageHint = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            groupsButton.Dock = DockStyle.Top;
            nodeHeader.Controls.Add(nodePageHint, 0, 0);
            nodeHeader.Controls.Add(groupsButton, 1, 0);
            nodes.Controls.Add(nodeHeader, 0, 0);
            var summary = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4,
                BackColor = Color.FromArgb(22, 27, 34) };
            overviewSummary = summary;
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            profileSummary = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold) };
            summary.Controls.Add(profileSummary, 0, 0);
            summary.Controls.Add(new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "当前应用使用此档案。节点选择、测速、收藏和策略组引用统一在“节点与策略”中管理。",
                ForeColor = Color.FromArgb(170, 185, 202) }, 0, 1);
            var browseNodes = MakeButton("查看节点与策略 →", Color.FromArgb(48, 54, 61));
            browseNodes.Width = 190; browseNodes.Height = 38;
            browseNodes.Click += delegate { ShowNodesPage(true); };
            var quickActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            quickActions.Controls.Add(browseNodes);
            historyButton = MakeButton("会话记录", Color.FromArgb(48,54,61));
            historyButton.Width = 120; historyButton.Height = 38;
            historyButton.Click += delegate { ShowHistory(); };
            quickActions.Controls.Add(historyButton);
            summary.Controls.Add(quickActions, 0, 2);
            summary.Controls.Add(new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "使用提示\r\n打开管理器不会自动启动应用；更换应用或档案前先停止代理。\r\n停止代理不关闭应用，也不保证自动直连。\r\n\r\nCtrl+1 概览 · Ctrl+2 节点 · Ctrl+F 搜索\r\nCtrl+T 主题 · Ctrl+H 会话记录 · F1 帮助",
                ForeColor = Color.FromArgb(139, 148, 158) }, 0, 3);
            pageBody.Controls.Add(summary);
            var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            nodeSearch = new TextBox { Dock = DockStyle.Top, AccessibleName = "搜索名称或协议",
                BackColor = Color.FromArgb(13, 17, 23), ForeColor = ForeColor, Margin = new Padding(0, 4, 12, 4) };
            nodeSort = new UiComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "节点排序", Margin = new Padding(0, 4, 8, 4) };
            nodeSearch.HandleCreated += delegate { SendMessage(nodeSearch.Handle, 0x1501, new IntPtr(1), "搜索名称或协议"); };
            nodeSort.DrawMode = DrawMode.OwnerDrawFixed;
            nodeSort.ItemHeight = 24;
            nodeSort.BackColor = Color.FromArgb(22, 27, 34);
            nodeSort.ForeColor = ForeColor;
            nodeSort.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                using (var brush = new SolidBrush(theme.ColorFor(Color.FromArgb(22, 27, 34)))) e.Graphics.FillRectangle(brush, e.Bounds);
                int index = e.Index >= 0 ? e.Index : nodeSort.SelectedIndex;
                if (index >= 0) TextRenderer.DrawText(e.Graphics, Convert.ToString(nodeSort.Items[index]), Font, e.Bounds, ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            nodeSort.Items.AddRange(new object[] { "原始顺序", "名称 A–Z", "延迟从低到高" });
            nodeSort.SelectedIndex = 0;
            favoritesOnly = new CheckBox { Text = "仅看收藏", Dock = DockStyle.Fill, Margin = new Padding(2, 0, 0, 0) };
            var clear = MakeButton("清除筛选", Color.FromArgb(48, 54, 61));
            clear.Dock = DockStyle.Top;
            filters.Controls.Add(nodeSearch, 0, 0);
            filters.Controls.Add(nodeSort, 1, 0);
            filters.Controls.Add(favoritesOnly, 2, 0);
            filters.Controls.Add(clear, 3, 0);
            nodes.Controls.Add(filters, 0, 1);
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            testButton = MakeButton("测试延迟", Color.FromArgb(48, 54, 61));
            fastestButton = MakeButton("选择最快", Color.FromArgb(48, 54, 61));
            refreshButton = MakeButton("刷新订阅", Color.FromArgb(48, 54, 61));
            favoriteButton = MakeButton("收藏选中", Color.FromArgb(48, 54, 61));
            foreach (Button button in new[] { testButton, fastestButton, refreshButton, favoriteButton })
            { button.Width = 96; tools.Controls.Add(button); }
            switchButton = MakeButton("切换节点", Color.FromArgb(31, 111, 235));
            switchButton.Dock = DockStyle.Top;
            toolbar.Controls.Add(tools, 0, 0);
            toolbar.Controls.Add(switchButton, 1, 0);
            nodes.Controls.Add(toolbar, 0, 2);
            nodeList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(22, 27, 34),
                ForeColor = ForeColor, HeaderStyle = ColumnHeaderStyle.Nonclickable, MultiSelect = false,
                ShowItemToolTips = true, AccessibleName = "节点列表" };
            nodeList.Columns.Add("节点", 260);
            nodeList.Columns.Add("协议", 116);
            nodeList.Columns.Add("延迟", 112);
            nodeList.Columns.Add("状态", 96);
            nodeList.Columns.Add("收藏", 58);
            var rowSize = new ImageList { ImageSize = new Size(1, 36) };
            var spacer = new Bitmap(1, 36);
            rowSize.Images.Add(spacer);
            nodeList.SmallImageList = rowSize;
            Disposed += delegate { rowSize.Dispose(); spacer.Dispose(); };
            nodeList.OwnerDraw = true;
            nodeList.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (var brush = new SolidBrush(theme.ColorFor(Color.FromArgb(33, 38, 45)))) e.Graphics.FillRectangle(brush, e.Bounds);
                var bounds = new Rectangle(e.Bounds.X + 12, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 16), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, bounds, theme.ColorFor(Color.FromArgb(139, 148, 158)),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            nodeList.DrawItem += delegate { };
            nodeList.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                Color background = theme.ColorFor(e.Item.Selected ? Color.FromArgb(26, 55, 82) : Color.FromArgb(22, 27, 34));
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
                var bounds = new Rectangle(e.Bounds.X + 12, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 16), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, bounds, theme.Light ? theme.ColorFor(e.Item.ForeColor) : e.Item.ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            nodeList.Resize += delegate {
                if (nodeList.ClientSize.Width > 0)
                    nodeList.Columns[0].Width = Math.Max((int)(180 * layoutScale), nodeList.ClientSize.Width -
                        nodeList.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width) - 4);
            };
            nodes.Controls.Add(nodeList, 0, 3);
            nodeCount = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(139, 148, 158),
                TextAlign = ContentAlignment.MiddleLeft };
            nodes.Controls.Add(nodeCount, 0, 4);
            nodeList.SelectedIndexChanged += delegate { UpdateButtons(); };
            nodeSearch.TextChanged += delegate { RenderNodes(); };
            nodeSort.SelectedIndexChanged += delegate { RenderNodes(); };
            favoritesOnly.CheckedChanged += delegate { RenderNodes(); };
            clear.Click += delegate { nodeSearch.Clear(); favoritesOnly.Checked = false; nodeSort.SelectedIndex = 0; RenderNodes(); };
            favoriteButton.Click += delegate { ToggleFavorite(); };
            testButton.Click += delegate { if (TestRequested != null) TestRequested(this, EventArgs.Empty); };
            fastestButton.Click += delegate { if (FastestRequested != null) FastestRequested(this, EventArgs.Empty); };
            refreshButton.Click += delegate { if (RefreshProvidersRequested != null) RefreshProvidersRequested(this, EventArgs.Empty); };
            switchButton.Click += delegate {
                if (nodeList.SelectedItems.Count == 1 && NodeSwitchRequested != null)
                    NodeSwitchRequested(Convert.ToString(nodeList.SelectedItems[0].Tag));
            };
            var totals = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            downloadTotalValue = AddInlineStat(totals, 0, "累计下载", "0 B");
            uploadTotalValue = AddInlineStat(totals, 1, "累计上传", "0 B");
            main.Controls.Add(totals, 0, 4);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
            footerValue = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, ForeColor = Color.FromArgb(139, 148, 158), Font = new Font(Font.FontFamily, 9F) };
            screenshotBox = new CheckBox { Text = "截图隐私模式", Dock = DockStyle.Fill };
            screenshotBox.CheckedChanged += delegate { SetScreenshotPrivacy(screenshotBox.Checked); };
            footer.Controls.Add(footerValue, 0, 0);
            footer.Controls.Add(screenshotBox, 1, 0);
            main.Controls.Add(footer, 0, 5);
            UpdateStatus(false, "已停止", "—", 0, 0, 0, 0, 0);
            ShowNodesPage(false);
        }

        internal void ShowNodesPage(bool show)
        {
            mainLayout.SuspendLayout();
            nodesPanel.Visible = show;
            overviewSummary.Visible = !show;
            readinessValue.Visible = statsPanel.Visible = !show;
            mainLayout.RowStyles[1].Height = show ? 0 : 78 * layoutScale;
            mainLayout.RowStyles[2].Height = show ? 0 : 118 * layoutScale;
            pageTitle.Text = show ? "节点与策略" : "连接概览";
            overviewNav.BackColor = Color.FromArgb(show ? 22 : 26, show ? 27 : 55, show ? 34 : 82);
            nodesNav.BackColor = Color.FromArgb(show ? 26 : 22, show ? 55 : 27, show ? 82 : 34);
            mainLayout.ResumeLayout(true);
            if (show) nodeSearch.Focus();
            else startButton.Focus();
            if (theme.Light) theme.Apply(this);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            layoutScale *= factor.Height;
            base.ScaleControl(factor, specified);
        }

        internal void SetThemePreferenceRoot(string userRoot)
        {
            try { themePreference = new ThemePreference(userRoot); SetLightTheme(themePreference.Read(), false); }
            catch { history.Add(SessionEvent.ThemeNotSaved); }
        }

        internal void SetLightTheme(bool light) { SetLightTheme(light, true); }
        private void SetLightTheme(bool light, bool save)
        {
            if (theme.Light != light)
            {
                theme.Light = light; theme.Apply(this);
                Invalidate(true);
                themeButton.Text = light ? "切换深色主题" : "切换浅色主题";
                RenderNodes();
            }
            if (save && themePreference != null)
            {
                try { themePreference.Save(light); }
                catch { history.Add(SessionEvent.ThemeNotSaved); footerValue.Text = "主题仅本次生效，保存失败；详见会话记录。"; }
            }
        }

        internal void RecordEvent(SessionEvent kind) { history.Add(kind); }
        private void ShowHistory()
        {
            if (screenshotPrivacy) return;
            using (var form = new SessionHistoryForm(history, theme.Light)) form.ShowDialog(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleShortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
        internal bool HandleShortcut(Keys keys)
        {
            if (keys == (Keys.Control | Keys.D1)) { ShowNodesPage(false); return true; }
            if (keys == (Keys.Control | Keys.D2)) { ShowNodesPage(true); return true; }
            if (keys == (Keys.Control | Keys.F)) { if (!screenshotPrivacy) { ShowNodesPage(true); nodeSearch.Focus(); } return true; }
            if (keys == (Keys.Control | Keys.T)) { SetLightTheme(!theme.Light); return true; }
            if (keys == (Keys.Control | Keys.H)) { ShowHistory(); return true; }
            if (keys == Keys.F1) { guideButton.PerformClick(); return true; }
            return false;
        }

        private static Button NavButton(string text)
        {
            var button = MakeButton(text, Color.FromArgb(22, 27, 34));
            button.Dock = DockStyle.Fill;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(10, 0, 0, 0);
            button.Margin = new Padding(0, 2, 0, 2);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 43, 56);
            return button;
        }

        private static Panel MakePanel()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 27, 34),
                Margin = new Padding(0)
            };
        }

        private static Label AddCard(TableLayoutPanel table, int column, string caption, string value, Color accent)
        {
            Panel card = MakePanel();
            card.Margin = new Padding(0);
            Label captionLabel = new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor = Color.FromArgb(139, 148, 158),
                Location = new Point(16, 14)
            };
            Label valueLabel = new Label
            {
                Text = value,
                AutoEllipsis = true,
                ForeColor = accent,
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                Location = new Point(16, 46),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Size = new Size(100, 38)
            };
            card.Controls.Add(captionLabel);
            card.Controls.Add(valueLabel);
            card.Resize += delegate { valueLabel.Width = Math.Max(60, card.ClientSize.Width - 24); };
            table.Controls.Add(card, column, 0);
            return valueLabel;
        }

        private static Label AddInlineStat(TableLayoutPanel table, int column, string caption, string value)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill };
            Label captionLabel = new Label
            {
                Text = caption,
                ForeColor = Color.FromArgb(139, 148, 158),
                Location = new Point(0, 16),
                AutoSize = true
            };
            Label valueLabel = new Label
            {
                Text = value,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Location = new Point(82, 14),
                AutoSize = true
            };
            panel.Controls.Add(captionLabel);
            panel.Controls.Add(valueLabel);
            table.Controls.Add(panel, column, 0);
            return valueLabel;
        }

        private static Button MakeButton(string text, Color color)
        {
            Button button = new UiButton();
            button.Text = text;
            button.Width = 118;
            button.Height = 34;
            button.Margin = new Padding(0, 0, 4, 4);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            return button;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        internal void UpdateStatus(bool isActive, string status, string node, double downBytes, double upBytes,
            long downTotal, long upTotal, int connections)
        {
            bool activeChanged = active != isActive;
            active = isActive;
            statusValue.Text = status;
            statusValue.ForeColor = !isActive ? Color.FromArgb(139, 148, 158) :
                status == "入口可用" ? Color.FromArgb(63, 185, 80) : Color.FromArgb(210, 160, 70);
            nodeValue.Text = DisplayNode(node);
            downloadValue.Text = FormatRate(downBytes);
            uploadValue.Text = FormatRate(upBytes);
            downloadTotalValue.Text = FormatBytes(downTotal);
            uploadTotalValue.Text = FormatBytes(upTotal);
            connectionsValue.Text = connections.ToString();
            if (activeChanged) RenderNodes();
            UpdateFooter();
            UpdateButtons();
        }

        internal void SetLocalProxyEndpoint(string endpoint)
        {
            if (!String.IsNullOrWhiteSpace(endpoint)) localProxyEndpoint = endpoint;
            UpdateFooter();
        }

        internal void SetProfileName(string profileName)
        {
            if (!String.IsNullOrWhiteSpace(profileName)) activeProfileName = profileName;
            UpdateFooter();
        }

        internal void SetApplicationName(string name)
        {
            applicationName = name;
            Text = screenshotPrivacy ? AppInfo.Title : AppInfo.Title + " · " + name;
            applicationValue.Text = "当前应用：" + (screenshotPrivacy ? "应用已隐藏" : name);
            UpdateButtons();
        }

        internal void SetReadiness(LaunchReadiness value)
        {
            readiness = value ?? LaunchReadiness.Checking();
            UpdateButtons();
        }

        internal void UpdateNodes(IList<NodeInfo> nodes, string current)
        {
            catalog = nodes.Select(n => new NodeInfo { Name = n.Name, Type = n.Type, Delay = n.Delay }).ToList();
            catalogCurrent = current;
            RenderNodes();
        }

        internal void SetNodeProfile(string userRoot, string profileId)
        {
            nodePreferences = new NodePreferences(Path.Combine(userRoot, "node-preferences.json"), profileId);
            catalog.Clear();
            catalogCurrent = null;
            favorites.Clear();
            nodeSearch.Clear();
            favoritesOnly.Checked = false;
            nodeSort.SelectedIndex = 0;
            try { favorites = nodePreferences.Load(); RenderNodes(); }
            catch { RenderNodes(); nodeCount.Text = "收藏文件无法读取；原文件保留，节点功能仍可使用。"; }
        }

        private void ToggleFavorite()
        {
            if (nodeList.SelectedItems.Count != 1) return;
            string name = Convert.ToString(nodeList.SelectedItems[0].Tag);
            try
            {
                if (nodePreferences != null) favorites = nodePreferences.Toggle(name);
                else if (!favorites.Remove(name)) favorites.Add(name); // Preview has no persistent state.
                RenderNodes();
            }
            catch { nodeCount.Text = "收藏未保存；请检查用户目录权限或收藏文件，原文件未覆盖。"; }
        }

        private void RenderNodes()
        {
            string selected = null;
            if (nodeList.SelectedItems.Count == 1) selected = Convert.ToString(nodeList.SelectedItems[0].Tag);
            var visible = NodeCatalog.Select(catalog, nodeSearch.Text, favoritesOnly.Checked, favorites, nodeSort.SelectedIndex);
            nodeList.BeginUpdate();
            nodeList.Items.Clear();
            foreach (NodeInfo node in visible)
            {
                bool isCurrent = String.Equals(node.Name, catalogCurrent, StringComparison.Ordinal);
                ListViewItem item = new ListViewItem(DisplayNode(node.Name));
                item.SubItems.Add(node.Type ?? "—");
                item.SubItems.Add(node.Delay > 0 ? node.Delay + " ms" : "未测试");
                item.SubItems.Add(isCurrent ? (active ? "使用中" : "上次选择") : "待机");
                item.SubItems.Add(favorites.Contains(node.Name) ? "★" : "");
                item.ToolTipText = screenshotPrivacy ? "" : node.Name;
                item.Tag = node.Name;
                if (isCurrent)
                {
                    item.ForeColor = Color.FromArgb(63, 185, 80);
                }
                nodeList.Items.Add(item);
                if (String.Equals(selected, node.Name, StringComparison.Ordinal)) item.Selected = true;
            }
            nodeList.EndUpdate();
            nodeCount.Text = "显示 " + visible.Count + " / " + catalog.Count + " 个入口成员（" + catalog.Count(n => !StrategyGraph.IsGroup(n.Type)) + " 节点、" + catalog.Count(n => StrategyGraph.IsGroup(n.Type)) + " 策略组）" +
                (visible.Count == 0 && catalog.Count > 0 ? " · 没有匹配项，可清除筛选" : "") +
                (!String.IsNullOrEmpty(catalogCurrent) && !visible.Any(n => n.Name == catalogCurrent) ? "\r\n当前节点不在列表中；筛选不会改变连接。" : "");
            UpdateButtons();
        }

        internal void SetBusy(bool value, string message)
        {
            busy = value;
            if (!String.IsNullOrWhiteSpace(message)) footerValue.Text = screenshotPrivacy ? "正在处理，请稍候…" : message;
            UpdateButtons();
        }

        internal void ShowDashboard()
        {
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void UpdateButtons()
        {
            if (startButton == null) return;
            favoriteButton.Enabled = !busy && nodeList.SelectedItems.Count == 1;
            favoriteButton.Text = nodeList.SelectedItems.Count == 1 && favorites.Contains(Convert.ToString(nodeList.SelectedItems[0].Tag))
                ? "取消收藏" : "收藏选中";
            startButton.Text = busy ? "正在处理…" : active ? "代理运行中" : readiness.ActionText;
            startButton.Enabled = !busy && !active && readiness.Kind != LaunchReadinessKind.Checking;
            if (readinessValue != null)
                readinessValue.Text = (active ? "应用：本次会话已启动或恢复；启动参数的实际效果待验证。停止代理不会关闭应用，也不保证自动直连。" : readiness.Hint) +
                    "\r\n目标网站连通性：未检测；入口可用不等于网站可用或隐私验证通过。";
            stopButton.Enabled = !busy && active;
            stopButton.BackColor = active ? Color.FromArgb(31, 111, 235) : Color.FromArgb(48, 54, 61);
            if (groupsButton != null) groupsButton.Enabled = !busy && active && !screenshotPrivacy;
            if (nodePageHint != null) nodePageHint.Text = busy ? "正在处理，请稍候…" : active ? "入口成员 · 测速不代表最大带宽" :
                catalog.Count == 0 ? "代理已停止 · 启动后可获取节点与策略" : "代理已停止 · 上次列表仅供参考，启动后刷新";
            testButton.Enabled = !busy && active;
            fastestButton.Enabled = !busy && active;
            switchButton.Enabled = !busy && active && nodeList.SelectedItems.Count == 1;
            refreshButton.Enabled = !busy && active;
            profilesButton.Enabled = !busy && !active && !screenshotPrivacy;
            applicationsButton.Enabled = !busy && !active && !screenshotPrivacy;
            diagnosticsButton.Enabled = !busy;
            guideButton.Enabled = !screenshotPrivacy;
            if (historyButton != null) historyButton.Enabled = !screenshotPrivacy;
            if (theme.Light) theme.Apply(this);
        }

        internal void SetScreenshotPrivacy(bool enabled)
        {
            if (screenshotPrivacy == enabled) return;
            screenshotPrivacy = enabled;
            screenshotBox.Checked = enabled;
            nodeList.ShowItemToolTips = !enabled;
            if (enabled) nodeSearch.Clear();
            nodeSearch.Enabled = !enabled;
            SetApplicationName(applicationName);
            RenderNodes();
            nodeValue.Text = DisplayNode(catalogCurrent);
            UpdateFooter();
            if (ScreenshotPrivacyChanged != null) ScreenshotPrivacyChanged(this, EventArgs.Empty);
        }

        private string DisplayNode(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name == "—") return "—";
            if (!screenshotPrivacy) return CleanNodeName(name);
            int index = catalog.FindIndex(n => String.Equals(n.Name, name, StringComparison.Ordinal));
            return index >= 0 ? "节点 " + (index + 1).ToString("00") : "节点已隐藏";
        }

        private void UpdateFooter()
        {
            if (profileSummary != null) profileSummary.Text = "当前代理档案\r\n" + (screenshotPrivacy ? "档案已隐藏" : activeProfileName);
            footerValue.Text = screenshotPrivacy ? (active ? "代理运行中 · 界面信息已隐藏" : "专用代理已停止 · 界面信息已隐藏") :
                "档案：" + activeProfileName + (active ? " · 本地代理 " + localProxyEndpoint : " · 专用代理已停止");
        }

        private static string CleanNodeName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "—";
            return value;
        }

        private static string FormatRate(double bytes)
        {
            return FormatBytes((long)Math.Max(0, bytes)) + "/s";
        }

        private static string FormatBytes(long bytes)
        {
            double value = Math.Max(0, bytes);
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString(value >= 100 || unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }
}
