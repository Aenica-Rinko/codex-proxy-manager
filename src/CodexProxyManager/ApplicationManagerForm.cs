using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class ApplicationManagerForm : Form
    {
        private ManagerSettings settings;
        private readonly string settingsPath;
        private readonly ListBox apps = new ListBox();
        private readonly TextBox nameBox = new TextBox();
        private readonly TextBox executableBox = new TextBox();
        private readonly ComboBox modeBox = new ComboBox();
        private readonly ComboBox profileBox = new ComboBox();
        private readonly Button browseButton;
        private readonly Label status;
        private readonly CheckBox webRtcBox = new CheckBox();
        private readonly CheckBox dnsBox = new CheckBox();
        private readonly CheckBox quicBox = new CheckBox();
        private readonly Label privacyStatus;
        private bool changed;

        internal ApplicationManagerForm(string userRoot)
        {
            settingsPath = Path.Combine(userRoot, "settings.json");
            settings = StartupConfiguration.ReadSettings(settingsPath);
            Text = "应用管理 · " + AppInfo.DisplayVersion;
            ClientSize = new Size(860, 682);
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            LabelAt("选择应用，绑定它使用的代理档案", 22, 16, 800, 38, 19);
            LabelAt("每次运行一个应用会话；Windows 系统代理保持原样。", 24, 59, 800, 28, 10);
            apps.SetBounds(24, 105, 235, 463);
            apps.BackColor = Color.FromArgb(22, 27, 34);
            apps.ForeColor = ForeColor;
            apps.IntegralHeight = false;
            Controls.Add(apps);
            Button newButton = ButtonAt("添加应用", 24, 604, 110);
            Button deleteButton = ButtonAt("移除应用", 149, 604, 110);
            newButton.Click += delegate { apps.ClearSelected(); LoadSelection(); nameBox.Focus(); };
            deleteButton.Click += DeleteSelection;
            LabelAt("应用名称", 284, 106, 100, 27, 10);
            TextAt(nameBox, 390, 103, 438);
            LabelAt("程序路径", 284, 157, 100, 27, 10);
            TextAt(executableBox, 390, 153, 342);
            browseButton = ButtonAt("浏览…", 742, 151, 86);
            browseButton.Click += delegate
            {
                using (var picker = new OpenFileDialog { Filter = "应用程序 (*.exe)|*.exe", CheckFileExists = true })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        executableBox.Text = picker.FileName;
                        if (String.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Text = Path.GetFileNameWithoutExtension(picker.FileName);
                    }
            };
            LabelAt("启动方式", 284, 207, 100, 27, 10);
            ComboAt(modeBox, 390, 203, 438);
            modeBox.Items.AddRange(new object[] { "Chromium / Electron 应用", "环境变量（应用需自行支持）" });
            LabelAt("代理档案", 284, 257, 100, 27, 10);
            ComboAt(profileBox, 390, 253, 438);
            profileBox.Items.Add(new ProfileChoice { Name = "跟随当前代理档案" });
            foreach (ProxyProfileDefinition profile in settings.Profiles)
                profileBox.Items.Add(new ProfileChoice { Id = profile.Id, Name = profile.Name });
            LabelAt("隐私保护 · 仅对支持相应参数的应用生效", 284, 304, 544, 28, 10);
            CheckAt(webRtcBox, "请求限制 WebRTC 非代理 UDP（默认开启）", 338);
            CheckAt(dnsBox, "限制浏览器本机 DNS 解析（可选）", 370);
            CheckAt(quicBox, "关闭 QUIC / HTTP3（可选）", 402);
            privacyStatus = LabelAt("", 284, 440, 544, 76, 9);
            LabelAt("WebRTC 限制可能影响语音/视频。DNS 限制可能使内网域名和直连功能不可用；核心及其他进程的 DNS 不在保护范围。", 284, 518, 544, 48, 9);
            LabelAt("修改启动/隐私参数后退出应用，再点“启动 / 恢复”。参数未变的兼容应用可直接恢复；不兼容程序不会自动获得保护。", 284, 568, 544, 34, 9);
            Button save = ButtonAt("保存并设为当前", 390, 604, 184);
            save.BackColor = Color.FromArgb(31, 111, 235);
            save.Click += SaveSelection;
            Button close = ButtonAt("关闭", 706, 604, 122);
            close.Click += delegate { Close(); };
            status = LabelAt("", 24, 650, 804, 26, 9);
            FormClosing += delegate { DialogResult = changed ? DialogResult.OK : DialogResult.Cancel; };
            apps.SelectedIndexChanged += delegate { LoadSelection(); };
            modeBox.SelectedIndexChanged += delegate { UpdatePrivacyStatus(); };
            webRtcBox.CheckedChanged += delegate { UpdatePrivacyStatus(); };
            dnsBox.CheckedChanged += delegate { UpdatePrivacyStatus(); };
            quicBox.CheckedChanged += delegate { UpdatePrivacyStatus(); };
            RefreshList(settings.ActiveApplicationId);
        }

        private sealed class ProfileChoice
        {
            internal string Id;
            internal string Name;
            public override string ToString() { return Name; }
        }

        private void RefreshList(string id)
        {
            apps.Items.Clear();
            foreach (ManagedApplication app in settings.Applications)
            {
                int index = apps.Items.Add(app);
                if (app.Id == id) apps.SelectedIndex = index;
            }
        }

        private void LoadSelection()
        {
            ManagedApplication app = apps.SelectedItem as ManagedApplication;
            bool codex = app != null && app.Id == "codex";
            nameBox.Text = app == null ? "" : app.Name;
            executableBox.Text = codex ? "自动定位已安装的 Codex" : app == null ? "" : app.Executable;
            nameBox.ReadOnly = codex;
            executableBox.ReadOnly = codex;
            browseButton.Enabled = !codex;
            modeBox.Enabled = !codex;
            modeBox.SelectedIndex = app != null && app.LaunchMode == "environment" ? 1 : 0;
            webRtcBox.Checked = app == null || app.LimitWebRtc != false;
            dnsBox.Checked = app != null && app.RestrictBrowserDns;
            quicBox.Checked = app != null && app.DisableQuic;
            UpdatePrivacyStatus();
            profileBox.SelectedIndex = 0;
            for (int i = 1; i < profileBox.Items.Count; i++)
                if (app != null && ((ProfileChoice)profileBox.Items[i]).Id == app.ProfileId) profileBox.SelectedIndex = i;
            status.Text = app != null && app.Id == settings.ActiveApplicationId ? "当前应用：" + app.Name : "";
        }

        private void SaveSelection(object sender, EventArgs e)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string before = serializer.Serialize(settings);
            try
            {
                ManagedApplication previous = apps.SelectedItem as ManagedApplication;
                var app = new ManagedApplication
                {
                    Id = previous == null ? "app-" + Guid.NewGuid().ToString("N") : previous.Id,
                    Name = nameBox.Text.Trim(),
                    Executable = previous != null && previous.Id == "codex" ? null : executableBox.Text.Trim(),
                    LaunchMode = modeBox.SelectedIndex == 0 ? "chromium" : "environment",
                    ProfileId = ((ProfileChoice)profileBox.SelectedItem).Id,
                    LimitWebRtc = webRtcBox.Checked,
                    RestrictBrowserDns = dnsBox.Checked,
                    DisableQuic = quicBox.Checked
                };
                ApplicationConfiguration.Validate(app);
                if (previous == null) settings.Applications.Add(app);
                else settings.Applications[settings.Applications.IndexOf(previous)] = app;
                settings.ActiveApplicationId = app.Id;
                StartupConfiguration.WriteSettings(settingsPath, settings);
                changed = true;
                Close();
            }
            catch { settings = serializer.Deserialize<ManagerSettings>(before); RefreshList(settings.ActiveApplicationId);
                status.Text = "应用配置未保存，已恢复原列表。请检查输入和目录权限。"; }
        }

        private void DeleteSelection(object sender, EventArgs e)
        {
            ManagedApplication app = apps.SelectedItem as ManagedApplication;
            if (app == null) return;
            if (app.Id == "codex") { status.Text = "Codex 是内置默认应用，不能移除。"; return; }
            if (MessageBox.Show(this, "移除“" + app.Name + "”的启动配置？应用程序及代理档案会保留。", "移除应用",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            string before = serializer.Serialize(settings);
            try
            {
                settings.Applications.Remove(app);
                StartupConfiguration.WriteSettings(settingsPath, settings);
                changed = true;
                RefreshList(settings.ActiveApplicationId);
            }
            catch { settings = serializer.Deserialize<ManagerSettings>(before); RefreshList(settings.ActiveApplicationId);
                status.Text = "应用配置未保存，已恢复原列表。请检查目录权限。"; }
        }

        private Label LabelAt(string text, int x, int y, int w, int h, float size)
        {
            var label = new Label { Text = text, Left = x, Top = y, Width = w, Height = h,
                ForeColor = ForeColor, Font = new Font(Font.FontFamily, size) };
            Controls.Add(label);
            return label;
        }

        private void TextAt(TextBox box, int x, int y, int width)
        {
            box.SetBounds(x, y, width, 28);
            box.BackColor = Color.FromArgb(22, 27, 34);
            box.ForeColor = ForeColor;
            Controls.Add(box);
        }

        private void ComboAt(ComboBox box, int x, int y, int width)
        {
            box.SetBounds(x, y, width, 28);
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            Controls.Add(box);
        }

        private void CheckAt(CheckBox box, string text, int y)
        {
            box.Text = text;
            box.SetBounds(284, y, 544, 30);
            Controls.Add(box);
        }

        private void UpdatePrivacyStatus()
        {
            bool supported = modeBox.SelectedIndex == 0;
            webRtcBox.Enabled = dnsBox.Enabled = quicBox.Enabled = supported;
            if (privacyStatus == null) return;
            privacyStatus.Text = PrivacyProtection.Describe(new ManagedApplication
            {
                LaunchMode = supported ? "chromium" : "environment",
                LimitWebRtc = webRtcBox.Checked, RestrictBrowserDns = dnsBox.Checked,
                DisableQuic = quicBox.Checked
            });
        }

        private Button ButtonAt(string text, int x, int y, int width)
        {
            var button = new Button { Text = text, Left = x, Top = y, Width = width, Height = 34,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 54, 61), ForeColor = ForeColor };
            Controls.Add(button);
            return button;
        }
    }
}
