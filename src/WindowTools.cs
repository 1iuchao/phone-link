using System;
using System.Drawing;
using System.Text;

namespace PhoneLink
{
    /// <summary>窗口相关的 Win32 操作：查找、定位、置顶、发送快捷键</summary>
    static class WindowTools
    {
        /// <summary>按进程号找到它的第一个可见顶层窗口（scrcpy 的 SDL 窗口）</summary>
        public static IntPtr FindTopLevelWindow(int pid)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint wpid;
                Native.GetWindowThreadProcessId(hWnd, out wpid);
                if (wpid != (uint)pid) return true;
                if (!Native.IsWindowVisible(hWnd)) return true;
                if (Native.GetWindow(hWnd, Native.GW_OWNER) != IntPtr.Zero) return true;

                var cls = new StringBuilder(256);
                Native.GetClassName(hWnd, cls, cls.Capacity);
                // SDL 窗口类名通常是 "SDL_app"
                if (cls.ToString().StartsWith("SDL", StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
                }
                if (found == IntPtr.Zero) found = hWnd; // 兜底：记住任意可见顶层窗口
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>把窗口移动到指定屏幕矩形</summary>
        public static void MoveWindow(IntPtr hWnd, Rectangle screenRect)
        {
            if (hWnd == IntPtr.Zero) return;
            Native.SetWindowPos(hWnd, IntPtr.Zero, screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        /// <summary>置顶 / 取消置顶</summary>
        public static void SetTopMost(IntPtr hWnd, bool topMost)
        {
            if (hWnd == IntPtr.Zero) return;
            Native.SetWindowPos(hWnd, topMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>显示 / 隐藏窗口（用于托盘最小化时同步隐藏投屏窗口）</summary>
        public static void ShowWindow(IntPtr hWnd, bool show)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return;
            Native.ShowWindow(hWnd, show ? 5 : 0); // SW_SHOW / SW_HIDE
        }

        /// <summary>
        /// 向目标窗口发送快捷键（如 Alt+O、Alt+Shift+R）。会先把焦点交给目标窗口。
        /// </summary>
        public static bool SendShortcut(IntPtr hWnd, byte vk, bool shift, bool ctrl)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return false;
            Native.SetForegroundWindow(hWnd);
            System.Threading.Thread.Sleep(80);

            if (ctrl) Native.keybd_event(Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
            if (shift) Native.keybd_event(Native.VK_SHIFT, 0, 0, UIntPtr.Zero);
            Native.keybd_event(Native.VK_LMENU, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(30);
            Native.keybd_event(vk, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(30);
            Native.keybd_event(vk, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.keybd_event(Native.VK_LMENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (shift) Native.keybd_event(Native.VK_SHIFT, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (ctrl) Native.keybd_event(Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            return true;
        }

        /// <summary>把某个窗口所在线程的键盘布局切到指定 HKL（若已一致则不动）</summary>
        public static void ApplyLayout(IntPtr hWnd, IntPtr hkl)
        {
            if (hWnd == IntPtr.Zero || hkl == IntPtr.Zero) return;
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(hWnd, out pid);
            if (tid == 0) return;
            long cur = Native.GetKeyboardLayout(tid).ToInt64() & 0xFFFF;
            long want = hkl.ToInt64() & 0xFFFF;
            if (cur == want) return;
            Native.PostMessage(hWnd, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        }
    }
}
