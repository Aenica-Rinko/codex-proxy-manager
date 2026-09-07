using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class ProfileListItem
    {
        internal ProxyProfileDefinition Profile { get; set; }
        internal bool Active { get; set; }

        public override string ToString()
        {
            return (Active ? "● " : "   ") + Profile.Name + "  ·  " + FriendlyKind(Profile.Kind);
        }

        private static string FriendlyKind(string kind)
        {
            if (String.Equals(kind, "subscription", StringComparison.OrdinalIgnoreCase)) return "订阅";
            if (String.Equals(kind, "share-links", StringComparison.OrdinalIgnoreCase)) return "分享链接";
            if (String.Equals(kind, "local-upstream", StringComparison.OrdinalIgnoreCase)) return "本地代理";
            if (String.Equals(kind, "external-mihomo", StringComparison.OrdinalIgnoreCase)) return "外部 Clash/Mihomo";
            if (String.Equals(kind, "managed-config", StringComparison.OrdinalIgnoreCase)) return "自定义配置";
            return kind ?? "未知";
        }
    }

    internal sealed class ProfileManagerForm : Form
    {
        private readonly string projectRoot;
        private readonly string userRoot;
        private ManagerSettings settings;
        private readonly ListBox profileList;
        private readonly Label selectionInfo;
        private readonly CheckBox autoStartBox;
        private readonly CheckBox autoFastestBox;
        private readonly CheckBox autoFailoverBox;
        private readonly TextBox profileNameBox;
        private readonly ComboBox sourceTypeBox;
        private readonly Panel localPanel;
        private readonly ComboBox localTypeBox;
        private readonly TextBox localHostBox;
        private readonly NumericUpDown localPortBox;
        private readonly TextBox localUserBox;
        private readonly TextBox localPasswordBox;
        private readonly Panel subscriptionPanel;
        private readonly TextBox subscriptionUrlBox;
        private readonly Panel linksPanel;
        private readonly TextBox linksBox;
        private readonly Panel externalPanel;
        private readonly TextBox externalHostBox;
        private readonly NumericUpDown externalPortBox;
        private readonly TextBox externalControllerBox;
        private readonly TextBox externalSecretBox;
        private readonly TextBox externalGroupBox;
        private readonly Label statusLabel;
        private bool changed;
        private bool controllerTestRunning;

        private ProfileManagerForm(string projectRoot, string userRoot, bool firstRun)
        {
            this.projectRoot = projectRoot;
            this.userRoot = userRoot;
            settings = LoadSettingsOrDefault(userRoot);

            Text = firstRun ? "Codex Proxy Manager · 首次配置" : "Codex Proxy Manager · 代理档案";
            Icon = SystemIcons.Shield;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(920, 620);
            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font("Segoe UI", 10F);
            FormClosing += ProfileManagerClosing;

            AddLabel(firstRun ? "创建第一个代理档案" : "管理代理档案", 24, 20, 860, 38, 21F, Color.White, true);
            AddLabel("不同来源统一生成只代理 Codex 的配置；真实节点和订阅不会写入项目仓库。",
                27, 60, 850, 28, 9.5F, Color.FromArgb(139, 148, 158), false);

            Panel left = MakePanel(24, 100, 290, 474);
            AddChildLabel(left, "已有档案", 16, 14, 240, 28, 12F, Color.White, true);
            profileList = new ListBox
            {
                Left = 16,
                Top = 49,
                Width = 258,
                Height = 245,
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = ForeColor,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            profileList.SelectedIndexChanged += ProfileSelectionChanged;
            left.Controls.Add(profileList);

            Button activateButton = MakeChildButton(left, "设为当前档案", 16, 307, 125, Color.FromArgb(35, 134, 54));
            Button deleteButton = MakeChildButton(left, "移出列表", 149, 307, 125, Color.FromArgb(218, 54, 51));
            activateButton.Click += ActivateSelectedProfile;
            deleteButton.Click += DeleteSelectedProfile;
            autoStartBox = new CheckBox
            {
                Text = "启动管理器后自动打开当前应用",
                Left = 16,
                Top = 382,
                Width = 255,
                Checked = settings.AutoStart,
                ForeColor = ForeColor
            };
            left.Controls.Add(autoStartBox);
            autoFastestBox = new CheckBox
            {
                Text = "启动后测速并选择最快节点",
                Left = 16,
                Top = 410,
                Width = 255,
                Checked = settings.AutoSelectFastest,
                ForeColor = ForeColor
            };
            left.Controls.Add(autoFastestBox);
            autoFailoverBox = new CheckBox
            {
                Text = "节点连续失败时自动切换",
                Left = 16,
                Top = 438,
                Width = 255,
                Checked = settings.AutoFailover,
                ForeColor = ForeColor
            };
            left.Controls.Add(autoFailoverBox);
            selectionInfo = AddChildLabel(left, "", 16, 352, 255, 24, 8.5F,
                Color.FromArgb(139, 148, 158), false);

            Panel right = MakePanel(330, 100, 566, 474);
            AddChildLabel(right, "添加新档案", 18, 14, 300, 28, 12F, Color.White, true);
            AddChildLabel(right, "档案名称", 18, 52, 90, 25, 9.5F, ForeColor, false);
            profileNameBox = AddChildTextBox(right, "", 116, 49, 420, false, false);
            AddChildLabel(right, "节点来源", 18, 94, 90, 25, 9.5F, ForeColor, false);
            sourceTypeBox = new ComboBox
            {
                Left = 116,
                Top = 91,
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = ForeColor
            };
            sourceTypeBox.Items.AddRange(new object[] { "订阅 URL", "分享链接 / Base64", "本地 HTTP / SOCKS5", "外部 Clash / Mihomo API" });
            sourceTypeBox.SelectedIndex = 0;
            sourceTypeBox.SelectedIndexChanged += delegate { UpdateSourcePanels(); };
            right.Controls.Add(sourceTypeBox);

            subscriptionPanel = MakeChildPanel(right, 18, 133, 518, 242);
            AddChildLabel(subscriptionPanel, "订阅地址", 0, 0, 100, 25, 9.5F, ForeColor, false);
            subscriptionUrlBox = AddChildTextBox(subscriptionPanel, "", 0, 30, 518, false, false);
            AddChildLabel(subscriptionPanel,
                "支持 Mihomo/Clash YAML、逐行 URI 和 Base64 订阅内容。订阅每小时自动更新，节点延迟每 5 分钟检查一次。",
                0, 72, 510, 58, 9F, Color.FromArgb(139, 148, 158), false);
            AddChildLabel(subscriptionPanel,
                "订阅地址通常包含访问凭据，将只保存在当前 Windows 用户目录。",
                0, 137, 510, 30, 9F, Color.FromArgb(210, 153, 34), false);

            linksPanel = MakeChildPanel(right, 18, 133, 518, 242);
            AddChildLabel(linksPanel, "每行粘贴一个分享链接，也可以粘贴完整 Base64 内容：", 0, 0, 510, 25,
                9.5F, ForeColor, false);
            linksBox = AddChildTextBox(linksPanel, "", 0, 30, 518, true, false);
            linksBox.Height = 160;
            linksBox.ScrollBars = ScrollBars.Vertical;
            AddChildLabel(linksPanel, "由 Mihomo 解析实际协议，当前核心可识别常见 VLESS、VMess、SS、Trojan、Hysteria2、TUIC 等 URI。",
                0, 196, 510, 42, 8.8F, Color.FromArgb(139, 148, 158), false);

            localPanel = MakeChildPanel(right, 18, 133, 518, 242);
            AddChildLabel(localPanel, "类型", 0, 0, 70, 25, 9.5F, ForeColor, false);
            localTypeBox = new ComboBox
            {
                Left = 96,
                Top = 0,
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = ForeColor
            };
            localTypeBox.Items.AddRange(new object[] { "SOCKS5", "HTTP" });
            localTypeBox.SelectedIndex = 0;
            localPanel.Controls.Add(localTypeBox);
            AddChildLabel(localPanel, "地址", 0, 45, 70, 25, 9.5F, ForeColor, false);
            localHostBox = AddChildTextBox(localPanel, "127.0.0.1", 96, 42, 250, false, false);
            AddChildLabel(localPanel, "端口", 358, 45, 52, 25, 9.5F, ForeColor, false);
            localPortBox = new NumericUpDown
            {
                Left = 414,
                Top = 42,
                Width = 104,
                Minimum = 1,
                Maximum = 65535,
                Value = 10808,
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = ForeColor
            };
            localPanel.Controls.Add(localPortBox);
            AddChildLabel(localPanel, "用户名", 0, 90, 70, 25, 9.5F, ForeColor, false);
            localUserBox = AddChildTextBox(localPanel, "", 96, 87, 422, false, false);
            AddChildLabel(localPanel, "密码", 0, 135, 70, 25, 9.5F, ForeColor, false);
            localPasswordBox = AddChildTextBox(localPanel, "", 96, 132, 422, false, true);
            Button testLocalButton = MakeChildButton(localPanel, "测试本地端口", 96, 180, 150, Color.FromArgb(31, 111, 235));
            testLocalButton.Click += TestLocalPort;

            externalPanel = MakeChildPanel(right, 18, 133, 518, 242);
            AddChildLabel(externalPanel, "流量地址", 0, 0, 84, 25, 9.2F, ForeColor, false);
            externalHostBox = AddChildTextBox(externalPanel, "127.0.0.1", 92, 0, 196, false, false);
            AddChildLabel(externalPanel, "端口", 302, 0, 48, 25, 9.2F, ForeColor, false);
            externalPortBox = new NumericUpDown
            {
                Left = 354,
                Top = 0,
                Width = 112,
                Minimum = 1,
                Maximum = 65535,
                Value = 7890,
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = ForeColor
            };
            externalPanel.Controls.Add(externalPortBox);
            AddChildLabel(externalPanel, "控制接口", 0, 41, 84, 25, 9.2F, ForeColor, false);
            externalControllerBox = AddChildTextBox(externalPanel, "http://127.0.0.1:9090", 92, 39, 374, false, false);
            AddChildLabel(externalPanel, "控制密钥", 0, 82, 84, 25, 9.2F, ForeColor, false);
            externalSecretBox = AddChildTextBox(externalPanel, "", 92, 80, 374, false, true);
            AddChildLabel(externalPanel, "策略组", 0, 123, 84, 25, 9.2F, ForeColor, false);
            externalGroupBox = AddChildTextBox(externalPanel, "", 92, 121, 374, false, false);
            AddChildLabel(externalPanel, "留空会自动选择节点最多的策略组；控制接口仅允许本机地址。", 92, 151, 410, 36,
                8.5F, Color.FromArgb(210, 153, 34), false);
            Button testExternalButton = MakeChildButton(externalPanel, "测试控制接口", 92, 194, 150, Color.FromArgb(31, 111, 235));
            testExternalButton.Click += TestExternalController;

            Button addButton = MakeChildButton(right, "添加并设为当前", 18, 391, 170, Color.FromArgb(35, 134, 54));
            addButton.Click += AddProfile;
            statusLabel = AddChildLabel(right, "", 205, 397, 330, 55, 8.8F,
                Color.FromArgb(139, 148, 158), false);

            Button closeButton = MakeButton("完成", 770, 584, 126, Color.FromArgb(48, 54, 61));
            closeButton.Click += delegate
            {
                if (settings.Profiles == null || settings.Profiles.Count == 0)
                {
                    MessageBox.Show("请至少创建一个代理档案。", "Codex Proxy Manager",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SaveSettings();
                DialogResult = changed ? DialogResult.OK : DialogResult.Cancel;
                Close();
            };

            RefreshProfileList();
            UpdateSourcePanels();
        }

        internal static bool ShowManager(string projectRoot, string userRoot, bool firstRun)
        {
            using (ProfileManagerForm form = new ProfileManagerForm(projectRoot, userRoot, firstRun))
                return form.ShowDialog() == DialogResult.OK;
        }

        private static ManagerSettings LoadSettingsOrDefault(string userRoot)
        {
            string path = Path.Combine(userRoot, "settings.json");
            if (File.Exists(path)) return StartupConfiguration.ReadSettings(path);
            return new ManagerSettings
            {
                SchemaVersion = 1,
                ActiveProfileId = "",
                AutoStart = false,
                AutoSelectFastest = false,
                AutoFailover = false,
                MixedPort = 17890,
                ControllerPort = 19090,
                ControllerSecret = Guid.NewGuid().ToString("N"),
                Profiles = new List<ProxyProfileDefinition>()
            };
        }

        private void RefreshProfileList()
        {
            string selectedId = null;
            ProfileListItem selected = profileList.SelectedItem as ProfileListItem;
            if (selected != null) selectedId = selected.Profile.Id;
            profileList.BeginUpdate();
            profileList.Items.Clear();
            foreach (ProxyProfileDefinition profile in settings.Profiles)
            {
                ProfileListItem item = new ProfileListItem
                {
                    Profile = profile,
                    Active = String.Equals(profile.Id, settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase)
                };
                int index = profileList.Items.Add(item);
                if (String.Equals(profile.Id, selectedId, StringComparison.OrdinalIgnoreCase)) profileList.SelectedIndex = index;
            }
            if (profileList.SelectedIndex < 0 && profileList.Items.Count > 0) profileList.SelectedIndex = 0;
            profileList.EndUpdate();
            UpdateSelectionInfo();
        }

        private void ProfileSelectionChanged(object sender, EventArgs e)
        {
            UpdateSelectionInfo();
        }

        private void UpdateSelectionInfo()
        {
            ProfileListItem item = profileList.SelectedItem as ProfileListItem;
            selectionInfo.Text = item == null ? "" : (item.Active ? "当前启动档案" : "可设为当前档案");
            selectionInfo.ForeColor = item != null && item.Active
                ? Color.FromArgb(63, 185, 80) : Color.FromArgb(139, 148, 158);
        }

        private void ActivateSelectedProfile(object sender, EventArgs e)
        {
            ProfileListItem item = profileList.SelectedItem as ProfileListItem;
            if (item == null) return;
            settings.ActiveProfileId = item.Profile.Id;
            FollowActiveProfile();
            settings.AutoStart = autoStartBox.Checked;
            SaveSettings();
            changed = true;
            SetStatus("已选择“" + item.Profile.Name + "”。关闭窗口后即可使用。", true);
            RefreshProfileList();
        }

        private void DeleteSelectedProfile(object sender, EventArgs e)
        {
            ProfileListItem item = profileList.SelectedItem as ProfileListItem;
            if (item == null) return;
            if (settings.Profiles.Count <= 1)
            {
                MessageBox.Show("至少需要保留一个代理档案。", "Codex Proxy Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult confirm = MessageBox.Show("从列表中移除“" + item.Profile.Name + "”？\n配置文件会保留，避免误删凭据。",
                "移除代理档案", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
            settings.Profiles.Remove(item.Profile);
            if (String.Equals(settings.ActiveProfileId, item.Profile.Id, StringComparison.OrdinalIgnoreCase))
                settings.ActiveProfileId = settings.Profiles[0].Id;
            SaveSettings();
            changed = true;
            SetStatus("档案已从列表移除，原文件仍保留在用户目录。", true);
            RefreshProfileList();
        }

        private void AddProfile(object sender, EventArgs e)
        {
            try
            {
                string name = profileNameBox.Text.Trim();
                if (String.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("请填写档案名称。");
                string id = "profile-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string profileDirectory = Path.Combine(userRoot, "profiles", id);
                string relativeConfig = Path.Combine("profiles", id, "config.yaml");
                string configPath = StartupConfiguration.ResolveUserPath(userRoot, relativeConfig);
                string kind;
                string yaml;

                if (sourceTypeBox.SelectedIndex == 0)
                {
                    string url = subscriptionUrlBox.Text.Trim();
                    Uri parsed;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                        !(parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                        throw new InvalidOperationException("请输入有效的 HTTP 或 HTTPS 订阅地址。");
                    kind = "subscription";
                    yaml = ProviderConfigFactory.BuildHttpProvider(url, id, settings.MixedPort);
                }
                else if (sourceTypeBox.SelectedIndex == 1)
                {
                    string links = linksBox.Text.Trim();
                    if (String.IsNullOrWhiteSpace(links)) throw new InvalidOperationException("请粘贴分享链接或 Base64 内容。");
                    if (!LooksLikeProviderContent(links))
                        throw new InvalidOperationException("内容不像分享链接或 Base64 订阅，请检查后重试。");
                    kind = "share-links";
                    string providerDirectory = Path.Combine(userRoot, "core-data", "providers");
                    Directory.CreateDirectory(providerDirectory);
                    File.WriteAllText(Path.Combine(providerDirectory, id + ".txt"), links + Environment.NewLine,
                        new UTF8Encoding(false));
                    yaml = ProviderConfigFactory.BuildFileProvider(id, settings.MixedPort);
                }
                else if (sourceTypeBox.SelectedIndex == 2)
                {
                    string host = localHostBox.Text.Trim();
                    int port = Convert.ToInt32(localPortBox.Value);
                    if (String.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("请填写本地代理地址。");
                    if (port == settings.MixedPort || port == settings.ControllerPort)
                        throw new InvalidOperationException("上游端口不能与管理器本地端口相同。");
                    kind = "local-upstream";
                    yaml = MihomoConfigFactory.BuildLocalUpstream(localTypeBox.SelectedIndex == 1 ? "http" : "socks5",
                        host, port, localUserBox.Text, localPasswordBox.Text, settings.MixedPort);
                }
                else
                {
                    string host = externalHostBox.Text.Trim();
                    int port = Convert.ToInt32(externalPortBox.Value);
                    if (String.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("请填写外部客户端的本地流量地址。");
                    if (port == settings.MixedPort || port == settings.ControllerPort)
                        throw new InvalidOperationException("外部客户端流量端口不能与管理器本地端口相同。");
                    kind = "external-mihomo";
                    yaml = MihomoConfigFactory.BuildLocalUpstream("http", host, port, "", "", settings.MixedPort);
                }

                Directory.CreateDirectory(profileDirectory);
                File.WriteAllText(configPath, yaml, new UTF8Encoding(false));
                ProxyProfileDefinition profile = new ProxyProfileDefinition
                {
                    Id = id,
                    Name = name,
                    Kind = kind,
                    ConfigFile = relativeConfig,
                    ProxyGroupName = sourceTypeBox.SelectedIndex == 3 ? externalGroupBox.Text.Trim() : "Codex Proxy",
                    NodeControllerUrl = sourceTypeBox.SelectedIndex == 3
                        ? StartupConfiguration.NormalizeControllerUrl(externalControllerBox.Text) : "",
                    NodeControllerSecret = sourceTypeBox.SelectedIndex == 3 ? externalSecretBox.Text : ""
                };
                settings.Profiles.Add(profile);
                settings.ActiveProfileId = id;
                FollowActiveProfile();
                settings.AutoStart = autoStartBox.Checked;
                SaveSettings();
                changed = true;
                ClearEditor();
                SetStatus("档案已保存并设为当前。", true);
                RefreshProfileList();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, false);
            }
        }

        private void SaveSettings()
        {
            settings.AutoStart = autoStartBox.Checked;
            settings.AutoSelectFastest = autoFastestBox.Checked;
            settings.AutoFailover = autoFailoverBox.Checked;
            StartupConfiguration.WriteSettings(Path.Combine(userRoot, "settings.json"), settings);
        }

        private void FollowActiveProfile()
        {
            ApplicationConfiguration.Normalize(settings);
            settings.Applications.Find(delegate(ManagedApplication app) { return app.Id == settings.ActiveApplicationId; }).ProfileId = null;
        }

        private void ProfileManagerClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.None) return;
            if (settings.Profiles != null && settings.Profiles.Count > 0)
            {
                SaveSettings();
                DialogResult = changed ? DialogResult.OK : DialogResult.Cancel;
            }
            else
            {
                DialogResult = DialogResult.Cancel;
            }
        }

        private void UpdateSourcePanels()
        {
            subscriptionPanel.Visible = sourceTypeBox.SelectedIndex == 0;
            linksPanel.Visible = sourceTypeBox.SelectedIndex == 1;
            localPanel.Visible = sourceTypeBox.SelectedIndex == 2;
            externalPanel.Visible = sourceTypeBox.SelectedIndex == 3;
            if (subscriptionPanel.Visible) subscriptionPanel.BringToFront();
            if (linksPanel.Visible) linksPanel.BringToFront();
            if (localPanel.Visible) localPanel.BringToFront();
            if (externalPanel.Visible) externalPanel.BringToFront();
        }

        private void TestLocalPort(object sender, EventArgs e)
        {
            string host = localHostBox.Text.Trim();
            int port = Convert.ToInt32(localPortBox.Value);
            using (TcpClient client = new TcpClient())
            {
                try
                {
                    IAsyncResult result = client.BeginConnect(host, port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(1800)) throw new TimeoutException("连接超时");
                    client.EndConnect(result);
                    SetStatus("本地端口连接成功。", true);
                }
                catch (Exception ex)
                {
                    SetStatus("本地端口连接失败：" + ex.Message, false);
                }
            }
        }

        private async void TestExternalController(object sender, EventArgs e)
        {
            if (controllerTestRunning) return;
            Button trigger = sender as Button;
            controllerTestRunning = true;
            if (trigger != null) trigger.Enabled = false;
            try
            {
                string baseUrl = StartupConfiguration.NormalizeControllerUrl(externalControllerBox.Text);
                if (String.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("请填写控制接口地址。");
                string secret = externalSecretBox.Text;
                SetStatus("正在检查控制接口，可继续操作窗口；结果对应本次点击时填写的地址。", true);
                int selectors = await Task.Run(delegate { return ControllerProbe.Check(baseUrl, secret, 2500); });
                if (!IsDisposed) SetStatus("本次接口检查成功，发现 " + selectors + " 个可切换策略组；不代表外网可用。", true);
            }
            catch (Exception ex)
            {
                if (!IsDisposed) SetStatus("本次控制接口检查失败：" + Diagnostics.Explain(ex), false);
            }
            finally
            {
                controllerTestRunning = false;
                if (trigger != null && !trigger.IsDisposed) trigger.Enabled = true;
            }
        }

        private static bool LooksLikeProviderContent(string value)
        {
            string lower = value.ToLowerInvariant();
            if (lower.Contains("://")) return true;
            string compact = new string(value.Where(delegate(char c) { return !Char.IsWhiteSpace(c); }).ToArray());
            if (compact.Length < 24 || compact.Length % 4 != 0) return false;
            foreach (char c in compact)
            {
                if (!(Char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=' || c == '-' || c == '_'))
                    return false;
            }
            return true;
        }

        private void ClearEditor()
        {
            profileNameBox.Clear();
            subscriptionUrlBox.Clear();
            linksBox.Clear();
            localUserBox.Clear();
            localPasswordBox.Clear();
            externalSecretBox.Clear();
            externalGroupBox.Clear();
        }

        private void SetStatus(string message, bool success)
        {
            statusLabel.Text = message;
            statusLabel.ForeColor = success ? Color.FromArgb(63, 185, 80) : Color.FromArgb(248, 81, 73);
        }

        private Panel MakePanel(int left, int top, int width, int height)
        {
            Panel panel = new Panel
            {
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(22, 27, 34)
            };
            Controls.Add(panel);
            return panel;
        }

        private static Panel MakeChildPanel(Control parent, int left, int top, int width, int height)
        {
            Panel panel = new Panel { Left = left, Top = top, Width = width, Height = height, BackColor = parent.BackColor };
            parent.Controls.Add(panel);
            return panel;
        }

        private Label AddLabel(string text, int left, int top, int width, int height, float size, Color color, bool bold)
        {
            Label label = NewLabel(text, left, top, width, height, size, color, bold);
            Controls.Add(label);
            return label;
        }

        private static Label AddChildLabel(Control parent, string text, int left, int top, int width, int height,
            float size, Color color, bool bold)
        {
            Label label = NewLabel(text, left, top, width, height, size, color, bold);
            parent.Controls.Add(label);
            return label;
        }

        private static Label NewLabel(string text, int left, int top, int width, int height, float size, Color color, bool bold)
        {
            return new Label
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color
            };
        }

        private static TextBox AddChildTextBox(Control parent, string text, int left, int top, int width,
            bool multiline, bool password)
        {
            TextBox box = new TextBox
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = multiline ? 120 : 27,
                Multiline = multiline,
                BackColor = Color.FromArgb(13, 17, 23),
                ForeColor = Color.FromArgb(230, 237, 243),
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = password
            };
            parent.Controls.Add(box);
            return box;
        }

        private Button MakeButton(string text, int left, int top, int width, Color color)
        {
            Button button = NewButton(text, left, top, width, color);
            Controls.Add(button);
            return button;
        }

        private static Button MakeChildButton(Control parent, string text, int left, int top, int width, Color color)
        {
            Button button = NewButton(text, left, top, width, color);
            parent.Controls.Add(button);
            return button;
        }

        private static Button NewButton(string text, int left, int top, int width, Color color)
        {
            Button button = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }
    }

    internal static class ProviderConfigFactory
    {
        internal static string BuildHttpProvider(string url, string profileId, int mixedPort)
        {
            return BuildProvider("http", url, "./providers/" + profileId + ".yaml", mixedPort);
        }

        internal static string BuildFileProvider(string profileId, int mixedPort)
        {
            return BuildProvider("file", null, "./providers/" + profileId + ".txt", mixedPort);
        }

        private static string BuildProvider(string type, string url, string path, int mixedPort)
        {
            StringBuilder yaml = new StringBuilder();
            yaml.AppendLine("mode: rule");
            yaml.AppendLine("log-level: info");
            yaml.AppendLine("find-process-mode: strict");
            yaml.AppendLine("allow-lan: false");
            yaml.AppendLine("ipv6: true");
            yaml.AppendLine("mixed-port: " + mixedPort);
            yaml.AppendLine("external-controller: \"\"");
            yaml.AppendLine("proxy-providers:");
            yaml.AppendLine("  codex-provider:");
            yaml.AppendLine("    type: " + type);
            if (!String.IsNullOrWhiteSpace(url))
            {
                yaml.AppendLine("    url: \"" + EscapeYaml(url) + "\"");
                yaml.AppendLine("    interval: 3600");
                yaml.AppendLine("    proxy: DIRECT");
                yaml.AppendLine("    size-limit: 10485760");
            }
            yaml.AppendLine("    path: \"" + EscapeYaml(path) + "\"");
            yaml.AppendLine("    health-check:");
            yaml.AppendLine("      enable: true");
            yaml.AppendLine("      url: https://www.gstatic.com/generate_204");
            yaml.AppendLine("      interval: 300");
            yaml.AppendLine("      timeout: 5000");
            yaml.AppendLine("      lazy: true");
            yaml.AppendLine("proxy-groups:");
            yaml.AppendLine("  - name: \"Codex Proxy\"");
            yaml.AppendLine("    type: select");
            yaml.AppendLine("    use:");
            yaml.AppendLine("      - codex-provider");
            yaml.AppendLine("    proxies:");
            yaml.AppendLine("      - DIRECT");
            AppendCodexRules(yaml);
            return yaml.ToString();
        }

        private static void AppendCodexRules(StringBuilder yaml)
        {
            yaml.AppendLine("rules:");
            yaml.AppendLine("  - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve");
            yaml.AppendLine("  - IP-CIDR,10.0.0.0/8,DIRECT,no-resolve");
            yaml.AppendLine("  - IP-CIDR,172.16.0.0/12,DIRECT,no-resolve");
            yaml.AppendLine("  - IP-CIDR,192.168.0.0/16,DIRECT,no-resolve");
            yaml.AppendLine("  - AND,((PROCESS-NAME,ChatGPT.exe),(GEOSITE,cn)),DIRECT");
            yaml.AppendLine("  - AND,((PROCESS-NAME,codex.exe),(GEOSITE,cn)),DIRECT");
            yaml.AppendLine("  - AND,((PROCESS-NAME,ChatGPT.exe),(GEOIP,CN)),DIRECT");
            yaml.AppendLine("  - AND,((PROCESS-NAME,codex.exe),(GEOIP,CN)),DIRECT");
            yaml.AppendLine("  - PROCESS-NAME,ChatGPT.exe,Codex Proxy");
            yaml.AppendLine("  - PROCESS-NAME,codex.exe,Codex Proxy");
            yaml.AppendLine("  - MATCH,DIRECT");
        }

        private static string EscapeYaml(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
