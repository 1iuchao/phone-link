using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhoneLink
{
    enum GlassPanelStyle
    {
        None,
        Aurora,
        Sidebar,
        Card
    }

    /// <summary>自绘面板：侧栏渐变 / 主区域极光 / 玻璃卡片</summary>
    class GlassPanel : Panel
    {
        public GlassPanelStyle Style = GlassPanelStyle.None;
        public int CornerRadius = 0;
        /// <summary>极光背景上要额外打光的区域（例如中间的玻璃卡片外圈）</summary>
        public Rectangle GlowRect;

        public GlassPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.FormBack;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Style == GlassPanelStyle.Aurora)
            {
                Glass.Aurora(g, ClientRectangle, GlowRect);
            }
            else if (Style == GlassPanelStyle.Sidebar)
            {
                Glass.SidebarBackdrop(g, ClientRectangle);

                // 侧栏右缘：一层细的折射亮边
                using (var pen = new Pen(Color.FromArgb(Theme.Light ? 200 : 30, 255, 255, 255)))
                {
                    g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
                }
            }
            else if (Style == GlassPanelStyle.Card)
            {
                // 圆角外的区域要先画出父容器的极光，否则四角会露出面板底色变成方角
                Glass.PaintParentBackdrop(g, this);
                Glass.Card(g, ClientRectangle, CornerRadius);
            }

            base.OnPaint(e);
        }
    }
}
