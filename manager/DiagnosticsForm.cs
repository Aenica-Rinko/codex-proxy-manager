using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexProxyManager
{
    internal sealed class DiagnosticsForm : Form
    {
        private readonly Func<DiagnosticInput> snapshot;
        private readonly TextBox output;
        private readonly Button refresh, export;
        private readonly CheckBox probe;
        private readonly Label status;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private DiagnosticReport report;

        internal DiagnosticsForm(Func<DiagnosticInput> snapshot)
        {
            this.snapshot = snapshot;
            Text = "连接诊断 · " + AppInfo.DisplayVersion;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(920, 690);
            MinimumSize = new Size(740, 530);
            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font("Segoe UI", 10F);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "连接诊断与脱敏报告", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 20F) }, 0, 0);
            probe = new CheckBox { Text = "测试当前节点的外网延迟（通过核心请求 www.gstatic.com）", Dock = DockStyle.Fill };
            layout.Controls.Add(probe, 0, 1);
            output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 27, 34), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            layout.Controls.Add(output, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            refresh = MakeButton("重新检查");
            export = MakeButton("导出脱敏报告");
            var close = MakeButton("关闭");
            refresh.Click += delegate { RunChecks(); };
            export.Click += ExportReport;
            close.Click += delegate { Close(); };
            buttons.Controls.AddRange(new Control[] { refresh, export, close });
            layout.Controls.Add(buttons, 0, 3);
            status = new Label { Dock = DockStyle.Fill, Text = "报告仅在本机显示，导出后由你决定是否分享。", AutoEllipsis = true };
            layout.Controls.Add(status, 0, 4);
            Shown += delegate { RunChecks(); };
            FormClosed += delegate { cancellation.Cancel(); };
        }

        private Button MakeButton(string text)
        {
            return new Button { Text = text, Width = 155, Height = 34, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 111, 235), ForeColor = Color.White };
        }

        private async void RunChecks()
        {
            if (!refresh.Enabled) return;
            refresh.Enabled = export.Enabled = probe.Enabled = false;
            report = null;
            output.Text = "正在检查文件、进程、端口与控制接口…";
            status.Text = "检查不会修改系统代理、启动应用或切换节点。";
            try
            {
                DiagnosticInput input = snapshot();
                bool testNode = probe.Checked;
                DiagnosticReport result = await Task.Run(delegate { return Diagnostics.Collect(input, testNode, cancellation.Token); });
                if (IsDisposed || cancellation.IsCancellationRequested) return;
                report = result;
                output.Text = report.ToText();
                output.SelectionStart = 0;
                output.ScrollToCaret();
                status.Text = "检查完成。导出内容与当前显示一致；不会附加原始配置或日志。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed) { output.Text = Diagnostics.Explain(ex); status.Text = "检查失败，请检查档案或稍后重试。"; }
            }
            finally
            {
                if (!IsDisposed)
                {
                    refresh.Enabled = probe.Enabled = true;
                    export.Enabled = report != null;
                }
            }
        }

        private void ExportReport(object sender, EventArgs e)
        {
            if (report == null) return;
            using (var save = new SaveFileDialog { Filter = "文本报告 (*.txt)|*.txt", DefaultExt = "txt", AddExtension = true,
                FileName = "CodexProxy-Diagnostics-" + report.Created.ToString("yyyyMMdd-HHmmss") + ".txt", OverwritePrompt = true })
            {
                if (save.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(save.FileName, report.ToText(), new UTF8Encoding(true)); status.Text = "脱敏报告已保存到所选文件。"; }
                catch { status.Text = "报告保存失败，请选择可写入的目录。"; }
            }
        }
    }
}
