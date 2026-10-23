using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>“液态玻璃”视觉辅助：圆角路径、柔和光斑、玻璃填充与高光</summary>
    static class Glass
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width < 2 || r.Height < 2)
            {
                path.AddRectangle(r);
                return path;
            }

            // 半径不能超过短边的一半；等于一半时正好是胶囊/圆形
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>径向柔光</summary>
        public static void Glow(Graphics g, Rectangle rect, Color color, int alpha)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(alpha, color);
                    brush.SurroundColors = new[] { Color.FromArgb(0, color) };
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>
        /// 侧栏底色（纵向渐变 + 顶部一层很淡的冷光）。
        /// 侧栏上的胶囊按钮会把同一套绘制平移到自己的坐标系里重画一遍，
        /// 这样圆角外露出的部分与侧栏严丝合缝，不需要透明控件。
        /// </summary>
        public static void SidebarBackdrop(Graphics g, Rectangle bounds)
        {
            Rectangle b = new Rectangle(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
            using (var brush = new LinearGradientBrush(b, Theme.SidebarTop, Theme.SidebarBottom, LinearGradientMode.Vertical))
            {
                g.FillRectangle(brush, b);
            }
            int h = Math.Max(1, b.Height);
            Glow(g, new Rectangle(b.X - b.Width, b.Y - h / 2, b.Width * 3, h), Theme.Accent, Theme.Light ? 12 : 30);
        }

        /// <summary>主区域底色：深色玻璃上的极光</summary>
        public static void Aurora(Graphics g, Rectangle bounds)
        {
            Aurora(g, bounds, Rectangle.Empty);
        }

        /// <summary>极光背景，可在指定区域额外打一层柔光（玻璃卡片外圈）</summary>
        public static void Aurora(Graphics g, Rectangle bounds, Rectangle glowRect)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using (var brush = new SolidBrush(Theme.AuraBack))
            {
                g.FillRectangle(brush, bounds);
            }

            int w = Math.Max(bounds.Width, 1);
            int h = Math.Max(bounds.Height, 1);
            int size = Math.Max(w, h);

            Glow(g, new Rectangle(bounds.X - size / 3, bounds.Y - size / 4, size, size),
                Theme.GradientStart, Theme.Light ? 34 : 54);
            Glow(g, new Rectangle(bounds.Right - size / 2, bounds.Bottom - size / 2, size, size),
                Theme.GradientEnd, Theme.Light ? 26 : 46);
            Glow(g, new Rectangle(bounds.X + w / 4, bounds.Bottom - size / 3, size * 2 / 3, size * 2 / 3),
                Theme.Accent, Theme.Light ? 12 : 24);

            if (glowRect.Width > 0 && glowRect.Height > 0)
            {
                Rectangle halo = glowRect;
                halo.Inflate(26, 26);
                Glow(g, halo, Theme.GradientStart, Theme.Light ? 40 : 34);
            }
        }

        /// <summary>
        /// 玻璃卡片：卡体保持平整（内部要放 Label 等子控件，大面积半透明会让子控件出现色块），
        /// 玻璃感全部由边缘的折射高光与底部内阴影承担。
        /// </summary>
        public static void Card(Graphics g, Rectangle bounds, int radius)
        {
            Rectangle rect = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            if (rect.Width <= 2 || rect.Height <= 2) return;

            using (GraphicsPath path = Rounded(rect, radius))
            {
                using (var brush = new SolidBrush(Theme.CardBack))
                {
                    g.FillPath(brush, path);
                }

                GraphicsState state = g.Save();
                g.SetClip(path);

                // 上沿内侧 3px 高光
                Rectangle top = new Rectangle(rect.X, rect.Y, rect.Width, 3);
                using (var hl = new LinearGradientBrush(top,
                    Color.FromArgb(Theme.Light ? 160 : 40, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(hl, top);
                }

                // 下沿内侧阴影，卡片有厚度
                int sh = 26;
                Rectangle bottom = new Rectangle(rect.X, rect.Bottom - sh, rect.Width, sh);
                using (var brush = new LinearGradientBrush(bottom, Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(Theme.Light ? 12 : 50, 0, 0, 0), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, bottom);
                }

                g.Restore(state);

                // 边缘：上亮下暗，模拟玻璃折射
                using (var rim = new LinearGradientBrush(rect,
                    Color.FromArgb(Theme.Light ? 190 : 62, 255, 255, 255),
                    Color.FromArgb(Theme.Light ? 120 : 16, 255, 255, 255), LinearGradientMode.Vertical))
                using (var pen = new Pen(rim))
                {
                    g.DrawPath(pen, path);
                }

                // 顶部 1px 镜面高光
                using (var hl = new Pen(Color.FromArgb(Theme.Light ? 220 : 82, 255, 255, 255)))
                {
                    g.DrawLine(hl, rect.X + radius, rect.Y + 1, rect.Right - radius, rect.Y + 1);
                }
            }
        }

        /// <summary>把父容器在控件所在位置的底色原样重画一遍（侧栏渐变才不会有色块）</summary>
        public static void PaintParentBackdrop(Graphics g, Control c)
        {
            Control p = c.Parent;
            if (p == null)
            {
                using (var brush = new SolidBrush(c.BackColor))
                {
                    g.FillRectangle(brush, c.ClientRectangle);
                }
                return;
            }

            var panel = p as GlassPanel;
            if (panel != null && (panel.Style == GlassPanelStyle.Sidebar || panel.Style == GlassPanelStyle.Aurora))
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(-c.Left, -c.Top);
                if (panel.Style == GlassPanelStyle.Sidebar) SidebarBackdrop(g, p.ClientRectangle);
                else Aurora(g, p.ClientRectangle, panel.GlowRect);
                g.Restore(state);
                return;
            }

            using (var brush = new SolidBrush(p.BackColor))
            {
                g.FillRectangle(brush, c.ClientRectangle);
            }
        }
    }
}
