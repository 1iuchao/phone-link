using System;
using System.Text;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>
    /// 一组全局快捷键（Win32 RegisterHotKey 用）。
    /// 配置里存成 "修饰键掩码,虚拟键码"，例如 Ctrl+Alt+H = "3,72"。
    /// </summary>
    class Hotkey
    {
        public int Modifiers;   // Native.MOD_*
        public Keys Key;
        /// <summary>当前没有按下任何键（或已被清除）</summary>
        public bool IsEmpty { get { return Key == Keys.None; } }

        public Hotkey()
        {
            Key = Keys.None;
        }

        public Hotkey(int modifiers, Keys key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        /// <summary>用一串按键信息构造；没有有效主键时返回空快捷键</summary>
        public static Hotkey FromKeyData(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.None) return new Hotkey();

            // 只按下修饰键（Ctrl/Alt/Shift/Win）不算一组快捷键
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.LControlKey ||
                key == Keys.RControlKey || key == Keys.LShiftKey || key == Keys.RShiftKey)
            {
                return new Hotkey();
            }

            return new Hotkey(ToModifiers(keyData), key);
        }

        static int ToModifiers(Keys keyData)
        {
            int m = 0;
            if ((keyData & Keys.Control) == Keys.Control) m |= Native.MOD_CONTROL;
            if ((keyData & Keys.Alt) == Keys.Alt) m |= Native.MOD_ALT;
            if ((keyData & Keys.Shift) == Keys.Shift) m |= Native.MOD_SHIFT;
            return m;
        }

        /// <summary>至少要有一个 Ctrl/Alt/Shift，避免单键劫持整个系统</summary>
        public bool HasModifier { get { return Modifiers != 0; } }

        public string Describe()
        {
            if (IsEmpty) return "未设置";

            var sb = new StringBuilder();
            if ((Modifiers & Native.MOD_CONTROL) != 0) sb.Append("Ctrl + ");
            if ((Modifiers & Native.MOD_ALT) != 0) sb.Append("Alt + ");
            if ((Modifiers & Native.MOD_SHIFT) != 0) sb.Append("Shift + ");
            if ((Modifiers & Native.MOD_WIN) != 0) sb.Append("Win + ");
            sb.Append(KeyName(Key));
            return sb.ToString();
        }

        static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "小键盘" + (key - Keys.NumPad0);
            if (key >= Keys.A && key <= Keys.Z) return ((char)('A' + (key - Keys.A))).ToString();
            if (key >= Keys.F1 && key <= Keys.F24) return "F" + (key - Keys.F1 + 1);

            switch (key)
            {
                case Keys.Space: return "空格";
                case Keys.Tab: return "Tab";
                case Keys.Enter: return "Enter";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Oemtilde: return "`";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Delete";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                default: return key.ToString();
            }
        }

        public override string ToString()
        {
            return IsEmpty ? "" : Modifiers.ToString() + "," + ((int)Key).ToString();
        }

        public static Hotkey Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return new Hotkey();
            string[] parts = text.Split(',');
            if (parts.Length != 2) return new Hotkey();
            int mods, vk;
            if (!int.TryParse(parts[0].Trim(), out mods)) return new Hotkey();
            if (!int.TryParse(parts[1].Trim(), out vk)) return new Hotkey();
            if (vk <= 0) return new Hotkey();
            return new Hotkey(mods, (Keys)vk);
        }
    }
}
