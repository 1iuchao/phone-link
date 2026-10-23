using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>音量胶囊：一个胶囊分成左右两半，左减右加；悬停滚轮由主窗口统一处理</summary>
    class VolumeCapsule : Control, IThemeAware
    {
        int _hover;      // 0 无，-1 左半，1 右半
        int _pressedHalf;
        bool _pressed;

        /// <summary>调节请求：-1 减，+1 加</summary>
        public event Action<int> Step;

        public VolumeCapsule()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(96, 28);
            Cursor = Cursors.Hand;
            TabStop = false;
            BackColor = Theme.CapsuleBack;
            Font = new Font("Microsoft YaHei UI", 10F);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Glass.PaintParentBackdrop(g, this);

            Rectangle rect = ClientRectangle;
            rect.Inflate(-1, -1);
            if (rect.Width <= 2 || rect.Height <= 2) return;

            int radius = rect.Height / 2;
            int mid = rect.X + rect.Width / 2;

            using (GraphicsPath path = Glass.Rounded(rect, radius))
            {
                // 玻璃本体
                using (var fill = new LinearGradientBrush(rect,
                    Color.FromArgb(_pressed ? 22 : _hover != 0 ? 42 : 26, 255, 255, 255),
                    Color.FromArgb(_pressed ? 30 : _hover != 0 ? 16 : 8, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(fill, path);
                }

                // 悬停 / 按下半区
                if (_hover != 0 || _pressed)
                {
                    int half = _pressed ? _pressedHalf : _hover;
                    Rectangle hr = half < 0
                        ? new Rectangle(rect.X, rect.Y, mid - rect.X, rect.Height)
                        : new Rectangle(mid, rect.Y, rect.Right - mid, rect.Height);

                    GraphicsState state = g.Save();
                    g.SetClip(path);
                    using (var brush = new SolidBrush(Color.FromArgb(_pressed ? 46 : 30, Theme.Accent)))
                    {
                        g.FillRectangle(brush, hr);
                    }
                    g.Restore(state);
                }

                // 折射边
                using (var rim = new LinearGradientBrush(rect,
                    Color.FromArgb(Theme.Light ? 205 : 66, 255, 255, 255),
                    Color.FromArgb(Theme.Light ? 120 : 12, 255, 255, 255), LinearGradientMode.Vertical))
                using (var pen = new Pen(rim))
                {
                    g.DrawPath(pen, path);
                }

                // 镜面高光 + 中间分隔
                using (var pen = new Pen(Color.FromArgb(Theme.Light ? 235 : 92, 255, 255, 255)))
                {
                    g.DrawLine(pen, rect.X + radius, rect.Y + 1, rect.Right - radius, rect.Y + 1);
                }
                using (var pen = new Pen(Color.FromArgb(Theme.Light ? 70 : 46, 255, 255, 255)))
                {
                    g.DrawLine(pen, mid, rect.Y + 8, mid, rect.Bottom - 8);
                }
            }

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            Color fg = Theme.Text;
            TextRenderer.DrawText(g, "−", Font, new Rectangle(rect.X, rect.Y, mid - rect.X, rect.Height), fg, flags);
            TextRenderer.DrawText(g, "+", Font, new Rectangle(mid, rect.Y, rect.Right - mid, rect.Height), fg, flags);
        }

        int HitHalf(Point p)
        {
            return p.X < Width / 2 ? -1 : 1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitHalf(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = 0;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                _pressedHalf = HitHalf(e.Location);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pressed && e.Button == MouseButtons.Left)
            {
                int half = HitHalf(e.Location);
                if (half == _pressedHalf && Step != null) Step(half);
            }
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        public void ApplyTheme()
        {
            Invalidate();
        }
    }
}
