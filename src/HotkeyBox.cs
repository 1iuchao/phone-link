using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>快捷键输入框：点击后按下组合键即可录制，Esc / Delete 清除</summary>
    class HotkeyBox : Control, IThemeAware
    {
        Hotkey _value = new Hotkey();
        bool _capturing;
        bool _hover;

        public event Action ValueChanged;

        public Hotkey Value
        {
            get { return _value; }
            set
            {
                _value = value ?? new Hotkey();
                Invalidate();
            }
        }

        public HotkeyBox()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
            BackColor = Theme.CapsuleBack;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9F);
            Height = 24;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 输入框是独立窗口，直接铺一层主题底色即可（面板背景是纯色）
            Color back = Theme.Light ? Color.FromArgb(246, 248, 252) : Color.FromArgb(18, 21, 29);
            using (var brush = new SolidBrush(back))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            Rectangle rect = ClientRectangle;
            rect.Inflate(-1, -1);
            if (rect.Width <= 2 || rect.Height <= 2) return;

            int radius = 7;
            using (GraphicsPath path = Glass.Rounded(rect, radius))
            {
                int alpha = _capturing ? 46 : _hover ? 38 : 22;
                using (var fill = new LinearGradientBrush(rect,
                    Color.FromArgb(alpha, 255, 255, 255),
                    Color.FromArgb(Math.Max(4, alpha / 4), 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(fill, path);
                }

                Color edgeTop = _capturing
                    ? Color.FromArgb(170, Theme.Accent)
                    : Color.FromArgb(Theme.Light ? 215 : 62, 255, 255, 255);
                Color edgeBottom = _capturing
                    ? Color.FromArgb(70, Theme.Accent)
                    : Color.FromArgb(Theme.Light ? 130 : 4, 255, 255, 255);
                using (var rim = new LinearGradientBrush(rect, edgeTop, edgeBottom, LinearGradientMode.Vertical))
                using (var pen = new Pen(rim))
                {
                    g.DrawPath(pen, path);
                }
            }

            string text;
            Color color;
            if (_capturing)
            {
                text = "请按下组合键…";
                color = Theme.Accent;
            }
            else if (_value.IsEmpty)
            {
                text = "未设置（点击设置）";
                color = Theme.SubText;
            }
            else
            {
                text = _value.Describe();
                color = Theme.Text;
            }

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, text, Font, rect, color, flags);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _capturing = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            _capturing = false;
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                SetValue(new Hotkey());
                _capturing = false;
                Invalidate();
                return;
            }

            Hotkey hk = Hotkey.FromKeyData(e.KeyData);
            if (hk.IsEmpty) return;                 // 只按了修饰键，继续等待
            if (!hk.HasModifier) return;            // 单键会劫持系统，必须带修饰键

            SetValue(hk);
            _capturing = false;
            Invalidate();
        }

        void SetValue(Hotkey hk)
        {
            _value = hk;
            Invalidate();
            if (ValueChanged != null) ValueChanged();
        }

        public void ApplyTheme()
        {
            ForeColor = Theme.Text;
            Invalidate();
        }
    }
}
