using System;
using System.Drawing;
using System.Windows.Forms;
using RdpSwitcher.Config;

namespace RdpSwitcher.UI
{
    /// <summary>
    /// Semi-transparent session-name label. A separate owned window because a child control cannot be translucent
    /// over the native RDP control. Click-through and never activated, so input keeps going to the session.
    /// </summary>
    internal sealed class WatermarkForm : Form
    {
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        private string _text = "";

        public WatermarkForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 24, 24);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        /// <summary>Sets text, opacity and position; <paramref name="area"/> is the screen rectangle to anchor to.</summary>
        public void Present(string text, double opacity, Rectangle area, WatermarkPosition position)
        {
            _text = text ?? "";
            Opacity = opacity;

            var scale = DeviceDpi / 96.0;
            var padX = (int)Math.Round(14 * scale);
            var padY = (int)Math.Round(8 * scale);
            var margin = (int)Math.Round(16 * scale);

            var textSize = TextRenderer.MeasureText(_text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var maxWidth = Math.Max(120, area.Width / 2);
            var size = new Size(Math.Min(textSize.Width + 2 * padX, maxWidth), textSize.Height + 2 * padY);

            Bounds = WatermarkLogic.ComputeBounds(area, size, position, margin);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var padX = (int)Math.Round(14 * DeviceDpi / 96.0);
            var rect = new Rectangle(padX, 0, Math.Max(1, Width - 2 * padX), Height);
            TextRenderer.DrawText(e.Graphics, _text, Font, rect, ForeColor,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
