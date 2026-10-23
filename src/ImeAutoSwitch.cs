using System;
using System.Threading;

namespace PhoneLink
{
    /// <summary>
    /// 输入法自动切换：焦点在投屏窗口时切英文键盘布局，离开时恢复中文布局。
    /// 实测结论：HKL 切换是全局生效的（每应用输入法未生效），所以必须双向还原。
    /// 只在本应用运行且存在投屏会话时启动该线程，停止投屏即停止线程并还原中文布局。
    /// </summary>
    class ImeAutoSwitch
    {
        readonly Func<IntPtr> _targetProvider;
        readonly IntPtr _hklEn;
        readonly IntPtr _hklZh;

        Thread _thread;
        volatile bool _running;
        volatile bool _bindLogged;

        public ImeAutoSwitch(Func<IntPtr> targetProvider)
        {
            _targetProvider = targetProvider;
            _hklEn = Native.LoadKeyboardLayout("00000409", 0); // 英语(美式键盘)，不会出现在语言栏
            _hklZh = Native.LoadKeyboardLayout("00000804", 0); // 中文(简体，中国)
            if (_hklEn == IntPtr.Zero || _hklZh == IntPtr.Zero)
            {
                Log.Write("输入法切换初始化失败: hklEn=0x" + _hklEn.ToInt64().ToString("X") + " hklZh=0x" + _hklZh.ToInt64().ToString("X"));
            }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _bindLogged = false;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "ImeAutoSwitch";
            _thread.Start();
            Log.Write("输入法自动切换：已启动");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try
            {
                if (_thread != null) _thread.Join(800);
            }
            catch
            {
            }
            _thread = null;
            RestoreChinese();
            Log.Write("输入法自动切换：已停止并还原中文布局");
        }

        /// <summary>把当前前台窗口恢复为中文布局</summary>
        public void RestoreChinese()
        {
            WindowTools.ApplyLayout(Native.GetForegroundWindow(), _hklZh);
        }

        void Loop()
        {
            IntPtr last = IntPtr.Zero;
            while (_running)
            {
                try
                {
                    IntPtr fg = Native.GetForegroundWindow();
                    if (fg != last)
                    {
                        last = fg;
                        IntPtr target = _targetProvider();
                        bool isMirror = target != IntPtr.Zero && fg == target;
                        WindowTools.ApplyLayout(fg, isMirror ? _hklEn : _hklZh);
                        if (isMirror && !_bindLogged)
                        {
                            _bindLogged = true;
                            Log.Write("输入法自动切换：已绑定投屏窗口 HWND=0x" + target.ToInt64().ToString("X"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("输入法切换异常: " + ex.Message);
                }
                Thread.Sleep(150);
            }
        }
    }
}
