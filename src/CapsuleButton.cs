using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhoneLink
{
    enum CapsuleIcon
    {
        None,
        Device,
        Pin,
        Refresh,
        Disconnect,
        Audio,
        Settings,
        Torch,
        Min,
        Close
    }

    /// <summary>
    /// 胶囊按钮（苹果「液态玻璃」风格）：自绘胶囊 + 半透明玻璃层 + 上亮下暗的折射边。
    /// 刻意继承 Control 而不是 Button：Button 自带一整套绘制管线，覆盖 OnPaint 后
    /// 在部分系统合成分层下会把自己的底图和别的按钮内容画串（实测侧栏全部显示成主按钮）。
    /// 纯 Control + 自绘最稳，同款方案的音量胶囊一直显示正常。
    /// </summary>
    class CapsuleButton : Control, IThemeAware
    {
        bool _hover;
        bool _pressed;
        bool _down;

        /// <summary>左侧小图标</summary>
        public CapsuleIcon Icon = CapsuleIcon.None;
        /// <summary>主按钮（强调色实心、白字）</summary>
        public bool Primary;
        /// <summary>开关类激活态（强调色描边 + 强调色文字）</summary>
        public bool Active;
        /// <summary>圆角半径；小于 0 时按胶囊处理（高度的一半）</summary>
        public int Radius = -1;

        public CapsuleButton()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            // 关掉基类的 StandardClick：Click 由 OnMouseUp 精确抛一次，
            // 否则基类和自己各抛一次，一次点击会执行两遍（置顶会开了又关）
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            BackColor = Theme.CapsuleBack;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9F);
            Cursor = Cursors.Hand;
            TabStop = false;
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

            int radius = Radius > 0 ? Radius : rect.Height / 2;

            using (GraphicsPath path = Glass.Rounded(rect, radius))
            {
                if (Primary) PaintPrimary(g, path, rect, radius);
                else PaintGlass(g, path, rect, radius);
            }

            PaintContent(g, rect);
        }

        // ---------- 玻璃胶囊 ----------

        void PaintGlass(Graphics g, GraphicsPath path, Rectangle rect, int radius)
        {
            // 只在外圈垫一层淡投影（用“外圈减内圈”的环形路径），胶囊才像浮在侧栏上
            Rectangle liftRect = new Rectangle(rect.X - 1, rect.Y, rect.Width + 2, rect.Height + 2);
            using (GraphicsPath lift = Glass.Rounded(liftRect, radius + 1))
            using (var ring = new GraphicsPath())
            {
                ring.FillMode = FillMode.Alternate;
                ring.AddPath(lift, false);
                ring.AddPath(path, false);
                using (var brush = new SolidBrush(Color.FromArgb(Theme.Light ? 18 : 60, 0, 0, 0)))
                {
                    g.FillPath(brush, ring);
                }
            }

            Color tint = Active ? Theme.Accent : Color.White;
            int topAlpha = Active ? 50 : _pressed ? 12 : _hover ? 46 : 16;
            int bottomAlpha = Active ? 26 : _pressed ? 24 : _hover ? 18 : 4;

            // 玻璃本体：上亮下暗的半透明层（叠在父容器的渐变上）
            using (var fill = new LinearGradientBrush(rect,
                Color.FromArgb(topAlpha, tint), Color.FromArgb(bottomAlpha, tint),
                LinearGradientMode.Vertical))
            {
                g.FillPath(fill, path);
            }

            GraphicsState state = g.Save();
            g.SetClip(path);

            // 上半部多一层柔和光泽，模拟液体表面的反光
            Rectangle sheen = new Rectangle(rect.X, rect.Y, rect.Width, Math.Max(1, rect.Height * 45 / 100));
            using (var hl = new LinearGradientBrush(sheen,
                Color.FromArgb(Theme.Light ? 90 : 20, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(hl, sheen);
            }

            // 底部内阴影，做出玻璃厚度
            Rectangle bottom = new Rectangle(rect.X, rect.Y + rect.Height / 2,
                rect.Width, Math.Max(1, rect.Height - rect.Height / 2));
            using (var shadow = new LinearGradientBrush(bottom, Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(Theme.Light ? 8 : 42, 0, 0, 0), LinearGradientMode.Vertical))
            {
                g.FillRectangle(shadow, bottom);
            }

            g.Restore(state);

            // 折射边：上沿亮、下沿几乎消失
            Color edgeTop = Active
                ? Color.FromArgb(165, Theme.Accent)
                : Color.FromArgb(Theme.Light ? 215 : 62, 255, 255, 255);
            Color edgeBottom = Active
                ? Color.FromArgb(36, Theme.Accent)
                : Color.FromArgb(Theme.Light ? 130 : 4, 255, 255, 255);
            using (var rim = new LinearGradientBrush(rect, edgeTop, edgeBottom, LinearGradientMode.Vertical))
            using (var pen = new Pen(rim))
            {
                g.DrawPath(pen, path);
            }

            // 顶部 1px 镜面高光
            using (var hl = new Pen(Color.FromArgb(Theme.Light ? 245 : 104, 255, 255, 255)))
            {
                g.DrawLine(hl, rect.X + radius, rect.Y + 1, rect.Right - radius, rect.Y + 1);
            }

            // 底部反射光，液态玻璃的“液体感”主要来自这两道高光
            using (var hl = new Pen(Color.FromArgb(Theme.Light ? 70 : 40, 255, 255, 255)))
            {
                g.DrawLine(hl, rect.X + radius + 2, rect.Bottom - 1, rect.Right - radius - 2, rect.Bottom - 1);
            }
        }

        // ---------- 主按钮 ----------

        void PaintPrimary(Graphics g, GraphicsPath path, Rectangle rect, int radius)
        {
            // 外发光
            Rectangle glowRect = new Rectangle(rect.X - 2, rect.Y - 1, rect.Width + 4, rect.Height + 4);
            using (GraphicsPath glow = Glass.Rounded(glowRect, radius + 2))
            using (var brush = new SolidBrush(Color.FromArgb(_pressed ? 28 : 56, Theme.GradientStart)))
            {
                g.FillPath(brush, glow);
            }

            // 强调色渐变主体
            using (var fill = new LinearGradientBrush(rect, Theme.GradientStart, Theme.GradientEnd, 40f))
            {
                g.FillPath(fill, path);
            }

            GraphicsState state = g.Save();
            g.SetClip(path);

            // 上半部光泽（苹果按钮的湿润高光）
            Rectangle gloss = new Rectangle(rect.X, rect.Y, rect.Width, Math.Max(1, (int)(rect.Height * 0.46)));
            using (var hl = new LinearGradientBrush(gloss, Color.FromArgb(_pressed ? 40 : 74, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(hl, gloss);
            }

            // 底部压暗一点，颜色更沉
            Rectangle deep = new Rectangle(rect.X, rect.Y + rect.Height * 3 / 4, rect.Width, Math.Max(1, rect.Height / 4));
            using (var sh = new LinearGradientBrush(deep, Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(24, 0, 0, 0), LinearGradientMode.Vertical))
            {
                g.FillRectangle(sh, deep);
            }

            g.Restore(state);

            using (var rim = new LinearGradientBrush(rect, Color.FromArgb(170, 255, 255, 255),
                Color.FromArgb(60, 255, 255, 255), LinearGradientMode.Vertical))
            using (var pen = new Pen(rim))
            {
                g.DrawPath(pen, path);
            }
            using (var hl = new Pen(Color.FromArgb(190, 255, 255, 255)))
            {
                g.DrawLine(hl, rect.X + radius, rect.Y + 1, rect.Right - radius, rect.Y + 1);
            }
        }

        // ---------- 图标 + 文字 ----------

        void PaintContent(Graphics g, Rectangle rect)
        {
            Color fg;
            if (Primary) fg = Theme.OnAccent;
            else if (Active) fg = Theme.Accent;
            else fg = Enabled ? ForeColor : Theme.SubText;

            bool hasIcon = Icon != CapsuleIcon.None;
            bool hasText = !string.IsNullOrEmpty(Text);
            const int iconSize = 15;
            const int gap = 6;

            Size textSize = hasText ? TextRenderer.MeasureText(Text, Font) : Size.Empty;
            int total = (hasIcon ? iconSize : 0) + ((hasIcon && hasText) ? gap : 0) + (hasText ? textSize.Width : 0);
            int x = rect.X + (rect.Width - total) / 2;
            int cy = rect.Y + rect.Height / 2;

            if (hasIcon)
            {
                DrawIcon(g, Icon, new Rectangle(x, cy - iconSize / 2, iconSize, iconSize), fg);
            }

            if (hasText)
            {
                int textX = x + (hasIcon ? iconSize + gap : 0);
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(textX, rect.Y, textSize.Width + 2, rect.Height), fg, flags);
            }
        }

        static void DrawIcon(Graphics g, CapsuleIcon icon, Rectangle r, Color c)
        {
            using (var pen = new Pen(c, 1.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;

                switch (icon)
                {
                    case CapsuleIcon.Device:
                        g.DrawRoundRect(pen, r.X + 4.5f, r.Y + 1.5f, 6, 12, 2);
                        g.DrawLine(pen, cx - 1.5f, r.Bottom - 2.5f, cx + 1.5f, r.Bottom - 2.5f);
                        break;
                    case CapsuleIcon.Pin:
                        g.DrawEllipse(pen, cx - 2.6f, r.Y + 1.5f, 5.2f, 5.2f);
                        g.DrawLine(pen, cx, r.Y + 6.7f, cx, r.Bottom - 2f);
                        g.DrawLine(pen, cx - 3.2f, r.Y + 4.1f, cx + 3.2f, r.Y + 4.1f);
                        break;
                    case CapsuleIcon.Refresh:
                        g.DrawArc(pen, r.X + 2.4f, r.Y + 2.4f, 10.2f, 10.2f, 40, 280);
                        PointF tip = new PointF(r.X + 11.5f, r.Y + 4.4f);
                        g.DrawLine(pen, tip.X, tip.Y, tip.X - 3.4f, tip.Y - 0.2f);
                        g.DrawLine(pen, tip.X, tip.Y, tip.X - 0.4f, tip.Y + 3.4f);
                        break;
                    case CapsuleIcon.Disconnect:
                        // 断开的连线：左右两段各带一个插头，中间留缺口
                        g.DrawLine(pen, r.X + 2, cy, cx - 4.2f, cy);
                        g.DrawLine(pen, cx + 4.2f, cy, r.Right - 2, cy);
                        g.DrawLine(pen, cx - 4.2f, cy - 3.2f, cx - 4.2f, cy + 3.2f);
                        g.DrawLine(pen, cx + 4.2f, cy - 3.2f, cx + 4.2f, cy + 3.2f);
                        break;
                    case CapsuleIcon.Audio:
                        // 耳机：头梁弧 + 两侧耳罩
                        g.DrawArc(pen, r.X + 2.2f, r.Y + 2.2f, 11.6f, 11.6f, 190, 160);
                        g.DrawLine(pen, r.X + 2.2f, cy + 1.5f, r.X + 2.2f, r.Bottom - 1.5f);
                        g.DrawLine(pen, r.Right - 2.2f, cy + 1.5f, r.Right - 2.2f, r.Bottom - 1.5f);
                        break;
                    case CapsuleIcon.Settings:
                        // 三条推子（比齿轮更像“设置”，也不容易看成亮度）
                        g.DrawLine(pen, r.X + 2, r.Y + 4, r.Right - 2, r.Y + 4);
                        g.DrawLine(pen, r.X + 2, cy, r.Right - 2, cy);
                        g.DrawLine(pen, r.X + 2, r.Bottom - 4, r.Right - 2, r.Bottom - 4);
                        using (var brush = new SolidBrush(c))
                        {
                            g.FillEllipse(brush, r.X + 3.5f, r.Y + 2, 4, 4);
                            g.FillEllipse(brush, r.Right - 8, cy - 2, 4, 4);
                            g.FillEllipse(brush, r.X + 5.5f, r.Bottom - 6, 4, 4);
                        }
                        break;
                    case CapsuleIcon.Torch:
                        g.DrawRectangle(pen, r.X + 2, r.Y + 5, 5, 6);
                        g.DrawLine(pen, r.X + 7, r.Y + 5.5f, r.X + 9, r.Y + 4);
                        g.DrawLine(pen, r.X + 7, r.Y + 10.5f, r.X + 9, r.Y + 12);
                        g.DrawLine(pen, r.X + 9, r.Y + 4, r.X + 9, r.Y + 12);
                        g.DrawLine(pen, r.X + 11, cy - 2, r.X + 13, cy - 2);
                        g.DrawLine(pen, r.X + 11, cy, r.X + 13.5f, cy);
                        g.DrawLine(pen, r.X + 11, cy + 2, r.X + 13, cy + 2);
                        break;
                    case CapsuleIcon.Min:
                        g.DrawLine(pen, r.X + 4, cy + 1, r.Right - 4, cy + 1);
                        break;
                    case CapsuleIcon.Close:
                        g.DrawLine(pen, r.X + 4.5f, r.Y + 4.5f, r.Right - 4.5f, r.Bottom - 4.5f);
                        g.DrawLine(pen, r.Right - 4.5f, r.Y + 4.5f, r.X + 4.5f, r.Bottom - 4.5f);
                        break;
                }
            }
        }

        // ---------- 交互 ----------

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _down = true;
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_down && e.Button == MouseButtons.Left)
            {
                _down = false;
                _pressed = false;
                Invalidate();
                if (ClientRectangle.Contains(e.Location))
                {
                    OnClick(EventArgs.Empty);
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        public void ApplyTheme()
        {
            ForeColor = Theme.Text;
            Invalidate();
        }
    }

    static class GraphicsExt
    {
        public static void DrawRoundRect(this Graphics g, Pen pen, float x, float y, float w, float h, float r)
        {
            using (var path = new GraphicsPath())
            {
                float d = r * 2;
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }
    }
}
