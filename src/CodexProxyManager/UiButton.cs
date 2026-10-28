// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexProxyManager
{
    // Native button behavior/accessibility with readable, consistent dark painting.
    internal sealed class UiButton : Button
    {
        private bool hover;
        internal bool LightTheme;
        internal UiButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = Enabled ? BackColor : LightTheme ? Color.FromArgb(232,237,243) : Color.FromArgb(33, 38, 45);
            if (Enabled && hover) background = ControlPaint.Light(background, .10F);
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(Focused && ShowFocusCues ? Color.FromArgb(31,111,235) : LightTheme ? Color.FromArgb(170,183,200) : Color.FromArgb(48, 54, 61)))
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            var bounds = new Rectangle(Padding.Left + 6, Padding.Top, Math.Max(0, Width - Padding.Horizontal - 12), Height - Padding.Vertical);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : LightTheme ? Color.FromArgb(84,98,116) : Color.FromArgb(139, 148, 158), flags);
        }
    }

    internal sealed class UiComboBox : ComboBox
    {
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (IsDisposed || !IsHandleCreated) return;
            if (message.Msg == 0x000f)
            { using (var graphics = Graphics.FromHwnd(Handle)) DrawChrome(graphics); }
            else if ((message.Msg == 0x0317 || message.Msg == 0x0318) && message.WParam != IntPtr.Zero)
            { using (var graphics = Graphics.FromHdc(message.WParam)) DrawChrome(graphics); }
        }
        private void DrawChrome(Graphics graphics)
        {
            using (var brush = new SolidBrush(BackColor)) graphics.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(Color.FromArgb(48, 54, 61))) graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            string text = SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : Text;
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(10, 0, Math.Max(0, Width - 34), Height), ForeColor,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int x = Width - 15, y = Height / 2;
            using (var pen = new Pen(ForeColor, 1.5F))
            { graphics.DrawLine(pen, x - 4, y - 2, x, y + 2); graphics.DrawLine(pen, x, y + 2, x + 4, y - 2); }
        }
    }
}
