using System;
using System.Drawing;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>主题感知控件实现该接口，切换主题时自行刷新</summary>
    interface IThemeAware
    {
        void ApplyTheme();
    }

    /// <summary>深色 / 浅色主题调色板（偏高级感的低饱和深色 + 蓝紫强调色）</summary>
    static class Theme
    {
        public static bool Light;

        public static Color FormBack { get { return Light ? Color.FromArgb(238, 241, 246) : Color.FromArgb(11, 13, 18); } }
        public static Color ToolbarBack { get { return Light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(17, 20, 27); } }
        public static Color ClientBack { get { return Light ? Color.FromArgb(244, 246, 250) : Color.FromArgb(11, 13, 18); } }
        public static Color PlaceholderBack { get { return Light ? Color.FromArgb(248, 250, 252) : Color.FromArgb(11, 13, 18); } }
        public static Color PanelBack { get { return Light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(21, 25, 34); } }
        public static Color InputBack { get { return Light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(26, 31, 42); } }

        public static Color Text { get { return Light ? Color.FromArgb(20, 24, 33) : Color.FromArgb(231, 234, 240); } }
        public static Color SubText { get { return Light ? Color.FromArgb(106, 115, 131) : Color.FromArgb(139, 148, 166); } }
        public static Color Accent { get { return Light ? Color.FromArgb(47, 107, 255) : Color.FromArgb(108, 140, 255); } }
        public static Color AccentSoft { get { return Light ? Color.FromArgb(231, 238, 255) : Color.FromArgb(30, 42, 68); } }
        public static Color OnAccent { get { return Color.White; } }
        public static Color GradientStart { get { return Light ? Color.FromArgb(52, 108, 255) : Color.FromArgb(112, 146, 255); } }
        public static Color GradientEnd { get { return Light ? Color.FromArgb(110, 84, 240) : Color.FromArgb(138, 104, 255); } }
        public static Color Danger { get { return Light ? Color.FromArgb(210, 55, 55) : Color.FromArgb(255, 122, 122); } }

        /// <summary>侧栏纵向渐变（上浅下深，玻璃被上方光线照亮的感觉）</summary>
        public static Color SidebarTop { get { return Light ? Color.FromArgb(250, 251, 254) : Color.FromArgb(28, 33, 45); } }
        public static Color SidebarBottom { get { return Light ? Color.FromArgb(237, 240, 246) : Color.FromArgb(12, 14, 20); } }
        /// <summary>主区域极光背景的底色</summary>
        public static Color AuraBack { get { return Light ? Color.FromArgb(243, 246, 251) : Color.FromArgb(8, 10, 14); } }

        public static Color CapsuleBack { get { return Light ? Color.FromArgb(243, 245, 249) : Color.FromArgb(23, 28, 38); } }
        public static Color CapsuleHover { get { return Light ? Color.FromArgb(233, 238, 247) : Color.FromArgb(34, 41, 56); } }
        public static Color CapsuleDown { get { return Light ? Color.FromArgb(222, 229, 242) : Color.FromArgb(43, 52, 69); } }
        public static Color Border { get { return Light ? Color.FromArgb(225, 230, 239) : Color.FromArgb(42, 49, 64); } }
        /// <summary>玻璃卡片内部的底色（与卡片半透明叠加后的实际观感一致）</summary>
        public static Color CardBack { get { return Light ? Color.FromArgb(252, 253, 255) : Color.FromArgb(26, 30, 40); } }

        /// <summary>递归应用主题（用于连接窗口等简单界面）</summary>
        public static void Apply(Control root)
        {
            ApplyTo(root, true);
        }

        static void ApplyTo(Control c, bool isRoot)
        {
            var aware = c as IThemeAware;
            if (aware != null)
            {
                aware.ApplyTheme();
            }
            else if (c is Form)
            {
                c.BackColor = FormBack;
                c.ForeColor = Text;
            }
            else if (c is GroupBox)
            {
                c.BackColor = PanelBack;
                c.ForeColor = Text;
            }
            else if (c is Panel)
            {
                string tag = c.Tag as string;
                if (tag == "toolbar") c.BackColor = ToolbarBack;
                else if (tag == "client") c.BackColor = ClientBack;
                else c.BackColor = PanelBack;
            }
            else if (c is Label)
            {
                c.ForeColor = (c.Tag as string) == "sub" ? SubText : Text;
            }
            else if (c is TextBox || c is ListBox)
            {
                c.BackColor = InputBack;
                c.ForeColor = Text;
            }
            else if (c is Button)
            {
                var b = (Button)c;
                b.BackColor = CapsuleBack;
                b.ForeColor = Text;
                b.FlatAppearance.MouseOverBackColor = CapsuleHover;
                b.FlatAppearance.MouseDownBackColor = CapsuleDown;
                b.FlatAppearance.BorderColor = Border;
            }
            else if (c is RadioButton || c is CheckBox)
            {
                c.ForeColor = Text;
                c.BackColor = Color.Transparent;
            }

            foreach (Control child in c.Controls)
            {
                ApplyTo(child, false);
            }
        }
    }
}
