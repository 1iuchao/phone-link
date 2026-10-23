using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>未连接页的品牌图形：圆角底 + 手机轮廓 + 强调色光晕</summary>
    class HeroMark : Control, IThemeAware
    {
        public HeroMark()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.PlaceholderBack;
            Size = new Size(64, 64);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // 外层柔和光晕
            using (var glow = new Pen(Color.FromArgb(28, Theme.Accent), 6))
            {
                using (GraphicsPath p = Rounded(rect, 18)) e.Graphics.DrawPath(glow, p);
            }

            using (GraphicsPath path = Rounded(new Rectangle(3, 3, Width - 7, Height - 7), 15))
            {
                using (var brush = new SolidBrush(Theme.AccentSoft))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (var pen = new Pen(Color.FromArgb(70, Theme.Accent)))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            // 手机轮廓
            using (var pen = new Pen(Theme.Accent, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = Width / 2;
                int cy = Height / 2;
                using (GraphicsPath phone = Rounded(new Rectangle(cx - 9, cy - 15, 18, 30), 5))
                {
                    e.Graphics.DrawPath(pen, phone);
                }
                e.Graphics.DrawLine(pen, cx - 3, cy + 11, cx + 3, cy + 11);
            }
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
            }
            else
            {
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
            }
            return path;
        }

        public void ApplyTheme()
        {
            Invalidate();
        }
    }
}
