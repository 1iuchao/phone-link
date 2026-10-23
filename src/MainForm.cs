using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>宿主窗口：自绘工具栏 + 投屏客户区（scrcpy 无边框窗口精确覆盖在客户区上）</summary>
    class MainForm : Form, IMessageFilter
    {
        const int SidebarWidth = 88;

        // 全局快捷键 id（托盘用；侧栏显隐不是全局热键）
        const int HotkeyTray = 2;

        readonly Config _cfg;
        readonly ScrcpySession _session = new ScrcpySession();
        readonly ImeAutoSwitch _ime;

        Panel _sidebar;
        Panel _client;
        Panel _placeholder;
        Panel _hero;
        HeroMark _mark;
        SettingsPanel _settings;
        Label _lblTitle;
        Label _lblHint;

        CapsuleButton _btnDevice;
        VolumeCapsule _volume;
        CapsuleButton _btnTop;
        CapsuleButton _btnReconnect;
        CapsuleButton _btnDisconnect;
        CapsuleButton _btnAudio;
        CapsuleButton _btnSettings;
        CapsuleButton _btnTorch;
        CapsuleButton _btnConnect;
        CapsuleButton _btnMin;
        CapsuleButton _btnClose;

        NotifyIcon _tray;
        readonly ToolTip _tips = new ToolTip();
        System.Windows.Forms.Timer _boundsTimer;
        System.Windows.Forms.Timer _volumeTimer;
        System.Windows.Forms.Timer _cameraRetryTimer;
        int _cameraRetry;
        bool _torchOn;
        int _pendingVolume;
        int _wheelRemainder;
        IntPtr _mouseHook;
        Native.LowLevelMouseProc _mouseProc;
        IntPtr _kbdHook;
        Native.LowLevelKeyboardProc _kbdProc;
        bool _busy;
        DeviceInfo _device;
        /// <summary>隐藏按钮栏前的窗口宽度，用于恢复</summary>
        int _widthBeforeBarHide;
        /// <summary>设置面板里执行会抢焦点的操作时，临时禁止「失焦即收起」</summary>
        bool _suppressSettingsHide;

        // 侧栏拉伸状态（只允许在左侧按钮栏的边缘拉伸，避免抢投屏画面的点击）
        bool _sidebarResizing;
        int _resizeHit;
        Point _resizeStart;
        Rectangle _resizeFrom;

        public MainForm()
        {
            _cfg = Config.Load();
            Theme.Light = _cfg.LightTheme;
            _ime = new ImeAutoSwitch(delegate { return _session.Window; });

            BuildUi();

            _session.WindowReady += OnWindowReady;
            _session.ProcessExited += OnProcessExited;
            _session.VideoSizeKnown += delegate { RunOnUi(FitToVideo); };

            Application.AddMessageFilter(this);

            ApplyConfigToUi();
            Log.Write("应用启动。内核目录: " + Paths.ScrcpyDir);
        }

        // ---------- 界面搭建 ----------

        void BuildUi()
        {
            Text = "手机互联";
            Icon = AppIcon.Get();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.FormBack;
            ClientSize = new Size(540, 920);
            MinimumSize = new Size(260, 410);
            KeyPreview = true;
            // 不要黑边：窗体不留边距，默认就是「左侧按钮栏 + 满屏手机画面」。
            // 边缘缩放改由全局鼠标钩子判定（scrcpy 窗口盖住客户区后拿不到 NCHITTEST）
            Padding = Padding.Empty;

            _client = new Panel();
            _client.Dock = DockStyle.Fill;
            _client.BackColor = Theme.ClientBack;
            _client.Tag = "client";

            var sidebar = new GlassPanel();
            sidebar.Style = GlassPanelStyle.Sidebar;
            _sidebar = sidebar;
            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = SidebarWidth;
            _sidebar.BackColor = Theme.ToolbarBack;
            _sidebar.Tag = "toolbar";

            BuildPlaceholder();

            Controls.Add(_client);
            Controls.Add(_sidebar);

            int x = 8;
            int y = 12;
            _btnDevice = MakeButton("设备", "选择连接方式 / 设备", delegate { OpenConnectDialog(); });
            _btnTop = MakeButton("置顶", "窗口始终显示在最前面", delegate { ToggleTopMost(); });
            _btnReconnect = MakeButton("重连", "刷新视频流；未连接时重新连接", delegate { Reconnect(); });
            _btnDisconnect = MakeButton("断开", "断开当前投屏连接（手机端不装任何东西，随时可用「设备」重新连接）", delegate { DisconnectSession(); });
            _btnAudio = MakeButton("仅音频", "只把手机声音传到电脑、不传画面；再点一次回到电脑端投屏", delegate { ToggleAudioOnly(); });
            _btnSettings = MakeButton("设置", "音频输出 / 熄屏投屏 / 保持常亮 / 摄像头 / 主题", delegate { ToggleSettings(); });

            _btnDevice.Icon = CapsuleIcon.Device;
            _btnTop.Icon = CapsuleIcon.Pin;
            _btnReconnect.Icon = CapsuleIcon.Refresh;
            _btnDisconnect.Icon = CapsuleIcon.Disconnect;
            _btnAudio.Icon = CapsuleIcon.Audio;
            _btnSettings.Icon = CapsuleIcon.Settings;

            _sidebar.Controls.Add(_btnDevice);
            _btnDevice.Location = new Point(x, y);
            y += 44;

            // 音量只占一个胶囊：左半减、右半加，悬停滚轮调节
            _volume = new VolumeCapsule();
            _volume.Size = new Size(SidebarWidth - 16, 34);
            _volume.BackColor = Theme.ToolbarBack;
            _volume.Location = new Point(x, y);
            _volume.Step += delegate(int step) { AdjustVolume(step); };
            _sidebar.Controls.Add(_volume);
            _tips.SetToolTip(_volume, "左半减音量 / 右半加音量；鼠标悬停时可用滚轮调节");
            y += 42;

            foreach (CapsuleButton b in new CapsuleButton[] { _btnTop, _btnReconnect, _btnDisconnect, _btnAudio, _btnSettings })
            {
                b.Location = new Point(x, y);
                y += 44;
                _sidebar.Controls.Add(b);
            }

            // 手电筒：仅摄像头模式下显示，位于「设置」下方
            _btnTorch = MakeButton("手电筒", "开关手机手电筒（仅摄像头模式）", delegate { ToggleTorch(); });
            _btnTorch.Icon = CapsuleIcon.Torch;
            _btnTorch.Location = new Point(x, y);
            _sidebar.Controls.Add(_btnTorch);
            _btnTorch.Visible = false;

            foreach (CapsuleButton b in new CapsuleButton[] { _btnDevice, _btnTop, _btnReconnect, _btnDisconnect, _btnAudio, _btnSettings, _btnTorch })
            {
                b.Size = new Size(SidebarWidth - 16, 38);
                b.BackColor = Theme.ToolbarBack;
            }

            _btnMin = MakeButton("", "最小化到任务栏", delegate { MinimizeToTaskbar(); });
            _btnClose = MakeButton("", "关闭（按设置收进托盘或退出）", delegate
            {
                if (_cfg.CloseToTray) HideToTray();
                else Close();
            });
            _btnMin.Icon = CapsuleIcon.Min;
            _btnClose.Icon = CapsuleIcon.Close;
            _btnMin.Size = new Size(32, 32);
            _btnClose.Size = new Size(32, 32);
            _btnMin.BackColor = Theme.ToolbarBack;
            _btnClose.BackColor = Theme.ToolbarBack;
            _btnClose.ForeColor = Theme.Danger;
            _sidebar.Controls.Add(_btnMin);
            _sidebar.Controls.Add(_btnClose);
            _sidebar.Resize += delegate
            {
                _btnClose.Location = new Point(_sidebar.Width - 40, _sidebar.Height - 44);
                _btnMin.Location = new Point(8, _sidebar.Height - 44);
            };
            // 侧栏空白处按住即可拖动窗口（子面板会截获 WM_NCHITTEST，不能只靠非客户区拖动）
            // 拉伸也只在侧栏上进行：左/上/下边缘 5px 内是缩放区，其余空白是拖动区
            _sidebar.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;

                int hit = SidebarResizeHit(e.Location);
                if (hit != 0)
                {
                    _sidebarResizing = true;
                    _resizeHit = hit;
                    _resizeStart = Cursor.Position;
                    _resizeFrom = Bounds;
                    _sidebar.Capture = true;
                    return;
                }
                BeginWindowDrag();
            };
            _sidebar.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_sidebarResizing)
                {
                    ApplyResize(Cursor.Position.X, Cursor.Position.Y);
                    return;
                }
                UpdateSidebarCursor(SidebarResizeHit(e.Location));
            };
            _sidebar.MouseUp += delegate
            {
                EndSidebarResize();
            };
            _sidebar.MouseCaptureChanged += delegate
            {
                if (_sidebarResizing && !_sidebar.Capture) _sidebarResizing = false;
            };

            _settings = new SettingsPanel(_cfg);
            _settings.Visible = false;
            _settings.AudioChanged += delegate { UpdateAudioButton(); RestartSession("音频输出已切换"); };
            _settings.TurnScreenOffChanged += delegate
            {
                _suppressSettingsHide = true;
                try
                {
                    // 屏幕亮灭只由启动参数（--turn-screen-off）决定，直接重启会话生效。
                    // 不再做任何亮屏尝试：这类机型上 adb 的唤屏/亮度通道全部无效，只会拖慢重连。
                    RestartSession("熄屏投屏已切换");
                }
                finally
                {
                    _suppressSettingsHide = false;
                }

                if (_settings.Visible) { try { _settings.Activate(); } catch { } }
            };
            _settings.StayAwakeChanged += delegate { ApplyStayAwake(); };
            _settings.CameraChanged += delegate
            {
                _cameraRetry = 0;
                _torchOn = false;
                RestartSession("摄像头模式已切换");
            };
            _settings.ThemeChanged += delegate
            {
                Theme.Light = _cfg.LightTheme;
                ApplyTheme();
            };
            // 快捷键改完先不生效（录制组合键时容易误触发），等设置面板关闭再注册
            _settings.VisibleChanged += delegate
            {
                if (!_settings.Visible) ApplyHotkeys();
            };
            // 切到别的程序时收起弹层；点主窗口/投屏窗口则保留（避免和「设置」按钮的开关互相打架）
            _settings.Deactivate += delegate
            {
                // 面板里点了会抢焦点的操作（如熄屏投屏要激活投屏窗口发快捷键）时不要收起
                if (_suppressSettingsHide) return;
                // 鼠标正点在「设置」按钮上时交给它的 Click 去切换，避免先被这里收起来
                if (_btnSettings.ClientRectangle.Contains(_btnSettings.PointToClient(Cursor.Position))) return;
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != Handle && fg != _session.Window) _settings.Hide();
            };
            // Alt+F4 只隐藏，避免弹层被销毁后无法再次打开
            _settings.FormClosing += delegate(object s, FormClosingEventArgs ev)
            {
                if (ev.CloseReason == CloseReason.UserClosing)
                {
                    ev.Cancel = true;
                    _settings.Hide();
                }
            };

            // 滚轮焦点可能在 scrcpy 进程，用低级鼠标钩子才能收到悬停滚轮
            _mouseProc = HookMouse;
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
            if (_mouseHook == IntPtr.Zero) Log.Write("滚轮钩子安装失败，改用窗口内滚轮");

            // 侧栏显隐快捷键只在本程序窗口激活时生效（不注册全局热键，不抢其它程序的组合键）
            _kbdProc = HookKeyboard;
            _kbdHook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _kbdProc, IntPtr.Zero, 0);
            if (_kbdHook == IntPtr.Zero) Log.Write("键盘钩子安装失败，侧栏快捷键不可用");

            _boundsTimer = new System.Windows.Forms.Timer();
            _boundsTimer.Interval = 300;
            _boundsTimer.Tick += delegate { SyncScrcpyBounds(); };
            _boundsTimer.Start();

            _volumeTimer = new System.Windows.Forms.Timer();
            _volumeTimer.Interval = 70;
            _volumeTimer.Tick += delegate { FlushVolume(); };

            // 摄像头被占用时延迟重试；失败多次后回退普通投屏
            _cameraRetryTimer = new System.Windows.Forms.Timer();
            _cameraRetryTimer.Interval = 2000;
            _cameraRetryTimer.Tick += delegate
            {
                _cameraRetryTimer.Stop();
                if (_busy) { _cameraRetryTimer.Start(); return; }
                if (!_session.IsRunning) StartSession(_device);
            };

            BuildTray();
        }

        void BuildPlaceholder()
        {
            var placeholder = new GlassPanel();
            placeholder.Style = GlassPanelStyle.Aurora;
            _placeholder = placeholder;
            _placeholder.Dock = DockStyle.Fill;
            _placeholder.BackColor = Theme.PlaceholderBack;
            _placeholder.Tag = "client";

            var hero = new GlassPanel();
            hero.Style = GlassPanelStyle.Card;
            hero.CornerRadius = 24;
            hero.Size = new Size(380, 250);
            _hero = hero;
            _hero.BackColor = Theme.CardBack;
            _hero.Tag = "client";

            _mark = new HeroMark();
            _mark.Size = new Size(64, 64);

            _lblTitle = new Label();
            _lblTitle.Text = "连接你的手机";
            _lblTitle.ForeColor = Theme.Text;
            _lblTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            _lblTitle.AutoSize = true;
            _lblTitle.BackColor = Theme.CardBack;

            _lblHint = new Label();
            _lblHint.Text = "USB 数据线连接并开启「USB 调试」\n或让手机与电脑处于同一 WiFi";
            _lblHint.ForeColor = Theme.SubText;
            _lblHint.Font = new Font("Microsoft YaHei UI", 9.5F);
            _lblHint.AutoSize = true;
            _lblHint.BackColor = Theme.CardBack;
            _lblHint.TextAlign = ContentAlignment.MiddleCenter;
            _lblHint.MaximumSize = new Size(380, 0);

            _btnConnect = MakeButton("连接设备", "打开连接窗口", delegate { OpenConnectDialog(); });
            _btnConnect.Primary = true;
            _btnConnect.BackColor = Theme.CardBack;
            _btnConnect.Size = new Size(150, 40);
            _btnConnect.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);

            _hero.Controls.Add(_mark);
            _hero.Controls.Add(_lblTitle);
            _hero.Controls.Add(_lblHint);
            _hero.Controls.Add(_btnConnect);

            _hero.Resize += delegate { LayoutHero(); };
            _placeholder.Controls.Add(_hero);
            _placeholder.Resize += delegate { CenterHero(); };
            _placeholder.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) BeginWindowDrag();
            };
            _client.Controls.Add(_placeholder);

            LayoutHero();
            CenterHero();
        }

        void LayoutHero()
        {
            _mark.Location = new Point((_hero.ClientSize.Width - _mark.Width) / 2, 8);
            _lblTitle.Location = new Point((_hero.ClientSize.Width - _lblTitle.Width) / 2, 84);
            _lblHint.Location = new Point((_hero.ClientSize.Width - _lblHint.Width) / 2, 126);
            _btnConnect.Location = new Point((_hero.ClientSize.Width - _btnConnect.Width) / 2, 186);
        }

        void CenterHero()
        {
            if (_hero == null) return;
            _hero.Left = Math.Max(0, (_placeholder.ClientSize.Width - _hero.Width) / 2);
            _hero.Top = Math.Max(0, (_placeholder.ClientSize.Height - _hero.Height) / 2 - 20);

            var gp = _placeholder as GlassPanel;
            if (gp != null)
            {
                gp.GlowRect = new Rectangle(_hero.Left, _hero.Top, _hero.Width, _hero.Height);
                gp.Invalidate();
            }
        }

        CapsuleButton MakeButton(string text, string tip, EventHandler onClick)
        {
            var b = new CapsuleButton();
            b.Text = text;
            b.ForeColor = Theme.Text;
            b.Click += onClick;
            if (!string.IsNullOrEmpty(tip)) _tips.SetToolTip(b, tip);
            return b;
        }

        void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = AppIcon.Get();
            _tray.Text = "手机互联";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { RestoreFromTray(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { RestoreFromTray(); });
            menu.Items.Add("连接设备…", null, delegate { RestoreFromTray(); OpenConnectDialog(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            _tray.ContextMenuStrip = menu;
        }

        // ---------- 窗口行为 ----------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyHotkeys();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 无边框窗体默认没有最小化框，任务栏点击图标就不会最小化
                cp.Style |= 0x00020000 | 0x00080000; // WS_MINIMIZEBOX | WS_SYSMENU
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // 全局快捷键：隐藏到托盘（侧栏显隐走键盘钩子，不再注册全局热键）
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HotkeyTray)
                {
                    // 再按一次恢复，省得去找托盘图标
                    if (Visible && WindowState != FormWindowState.Minimized) HideToTray();
                    else RestoreFromTray();
                    return;
                }
            }

            // 点击任务栏图标 / 系统菜单最小化：正常最小化到任务栏（投屏窗口一起隐藏）
            if (m.Msg == Native.WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == Native.SC_MINIMIZE)
            {
                _settings.Hide();
                WindowTools.ShowWindow(_session.Window, false);
                base.WndProc(ref m);
                return;
            }

            if (m.Msg == Native.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (m.Result.ToInt32() != Native.HTCLIENT) return;

                int lp = m.LParam.ToInt32();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);
                Point p = PointToClient(new Point(sx, sy));

                const int grip = 3;
                bool left = p.X <= grip;
                bool right = p.X >= ClientSize.Width - grip;
                bool top = p.Y <= grip;
                bool bottom = p.Y >= ClientSize.Height - grip;

                if (top && left) m.Result = (IntPtr)Native.HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)Native.HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)Native.HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)Native.HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)Native.HTLEFT;
                else if (right) m.Result = (IntPtr)Native.HTRIGHT;
                else if (top) m.Result = (IntPtr)Native.HTTOP;
                else if (bottom) m.Result = (IntPtr)Native.HTBOTTOM;
                else if (IsSidebarBlank(p)) m.Result = (IntPtr)Native.HTCAPTION;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// 只有侧栏的空白区域才算标题栏（可拖动窗口）。
        /// 按钮/音量胶囊所在的区域必须返回 HTCLIENT，否则真实鼠标点击会被当成拖动窗口而吞掉。
        /// </summary>
        bool IsSidebarBlank(Point p)
        {
            if (_sidebar == null) return false;
            Rectangle bounds = _sidebar.Bounds;
            if (!bounds.Contains(p)) return false;

            Point local = new Point(p.X - bounds.X, p.Y - bounds.Y);
            foreach (Control c in _sidebar.Controls)
            {
                if (c.Visible && c.Bounds.Contains(local)) return false;
            }
            return true;
        }

        /// <summary>由控件内的空白处发起窗口拖动（等效按住标题栏）</summary>
        void BeginWindowDrag()
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        /// <summary>
        /// 侧栏上可用于缩放的边缘：左边缘改宽度，上下边缘改高度（侧栏是贴着窗体边缘的，所以这些位置就是窗体的边）。
        /// 返回 0 表示这一处是拖动区。
        /// </summary>
        int SidebarResizeHit(Point p)
        {
            const int edge = 5;
            bool left = p.X <= edge;
            bool top = p.Y <= edge;
            bool bottom = p.Y >= _sidebar.Height - edge - 1;

            if (left && top) return Native.HTTOPLEFT;
            if (left && bottom) return Native.HTBOTTOMLEFT;
            if (left) return Native.HTLEFT;
            if (top) return Native.HTTOP;
            if (bottom) return Native.HTBOTTOM;
            return 0;
        }

        void UpdateSidebarCursor(int hit)
        {
            Cursor wanted = Cursors.Default;
            if (hit == Native.HTLEFT) wanted = Cursors.SizeWE;
            else if (hit == Native.HTTOP || hit == Native.HTBOTTOM) wanted = Cursors.SizeNS;
            else if (hit == Native.HTTOPLEFT || hit == Native.HTBOTTOMLEFT) wanted = Cursors.SizeNWSE;

            if (_sidebar.Cursor != wanted) _sidebar.Cursor = wanted;
        }

        void EndSidebarResize()
        {
            if (!_sidebarResizing) return;
            _sidebarResizing = false;
            if (_sidebar.Capture) _sidebar.Capture = false;
            _sidebar.Cursor = Cursors.Default;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEWHEEL)
            {
                if (IsMouseOver(_volume))
                {
                    long wp = m.WParam.ToInt64();
                    int delta = (short)((wp >> 16) & 0xFFFF);
                    AccumulateVolume(delta);
                    return true; // 消费掉，避免传给其它控件
                }
            }
            return false;
        }

        /// <summary>全局低级鼠标钩子：焦点在 scrcpy 进程时也能收到悬停滚轮</summary>
        IntPtr HookMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt32() == Native.WM_MOUSEWHEEL && IsMouseOver(_volume))
                {
                    var data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    int delta = (short)((data.mouseData >> 16) & 0xFFFF);
                    AccumulateVolume(delta);
                    return (IntPtr)1; // 吞掉，避免滚轮传给手机或其它窗口
                }
            }
            catch (Exception ex)
            {
                Log.Write("滚轮钩子异常: " + ex.Message);
            }
            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        /// <summary>
        /// 按鼠标位移计算新窗口位置/大小。锚点固定（拉左边时右边不动、拉上边时下边不动），
        /// 投屏中保持画面比例并提前算好屏幕边界内的最大尺寸，避免拖到边界时来回跳。
        /// </summary>
        void ApplyResize(int screenX, int screenY)
        {
            int dx = screenX - _resizeStart.X;
            int dy = screenY - _resizeStart.Y;

            Rectangle from = _resizeFrom;
            Rectangle r = from;
            Rectangle wa = Screen.FromControl(this).WorkingArea;

            bool left = _resizeHit == Native.HTLEFT || _resizeHit == Native.HTTOPLEFT || _resizeHit == Native.HTBOTTOMLEFT;
            bool top = _resizeHit == Native.HTTOP || _resizeHit == Native.HTTOPLEFT;
            bool bottom = _resizeHit == Native.HTBOTTOM || _resizeHit == Native.HTBOTTOMLEFT;

            int minW = MinimumSize.Width;
            int minH = MinimumSize.Height;

            Size video = _session.VideoSize;
            bool hasVideo = _session.IsRunning && video.Width > 0 && video.Height > 0;
            int chrome = hasVideo && _sidebar != null && _sidebar.Visible ? SidebarWidth : 0;

            if (hasVideo)
            {
                double ratio = (double)video.Height / video.Width;   // 客户区高 / 宽

                if (left)
                {
                    // 右边固定；宽度上限同时受屏幕左边和上下空间限制
                    int maxW = Math.Max(minW, from.Right - wa.Left);
                    int maxH = top ? from.Bottom - wa.Top : wa.Bottom - from.Top;
                    int maxClientW = Math.Max(1, Math.Min(maxW - chrome, (int)(maxH / ratio)));

                    int clientW = from.Width - chrome - dx;
                    clientW = Math.Max(1, Math.Min(clientW, maxClientW));

                    int w = Math.Max(minW, clientW + chrome);
                    int h = Math.Max(minH, (int)Math.Round(clientW * ratio));

                    r.Width = w;
                    r.X = from.Right - w;
                    r.Height = h;
                    r.Y = top ? from.Bottom - h : from.Top;
                }
                else
                {
                    // 上下边：左边固定，以高度为准反推宽度
                    int maxH = Math.Max(minH, top ? from.Bottom - wa.Top : wa.Bottom - from.Top);
                    int maxClientW = Math.Max(1, wa.Right - from.Left - chrome);
                    maxH = Math.Min(maxH, (int)(maxClientW * ratio));

                    int h = top ? from.Height - dy : from.Height + dy;
                    h = Math.Max(minH, Math.Min(h, maxH));

                    r.Height = h;
                    r.Y = top ? from.Bottom - h : from.Top;
                    r.Width = Math.Max(minW, (int)Math.Round(h * video.Width / (double)video.Height) + chrome);
                    r.X = from.Left;
                }
            }
            else
            {
                if (left)
                {
                    int w = Math.Max(minW, from.Width - dx);
                    r.X = from.Right - w;
                    r.Width = w;
                }

                if (top)
                {
                    int h = Math.Max(minH, from.Height - dy);
                    r.Y = from.Bottom - h;
                    r.Height = h;
                }
                else if (bottom)
                {
                    r.Height = Math.Max(minH, from.Height + dy);
                }
            }

            // 兜底：保证窗口完整落在工作区内
            if (r.Width > wa.Width) r.Width = wa.Width;
            if (r.Height > wa.Height) r.Height = wa.Height;
            if (r.Right > wa.Right) r.X = wa.Right - r.Width;
            if (r.Bottom > wa.Bottom) r.Y = wa.Bottom - r.Height;
            if (r.X < wa.Left) r.X = wa.Left;
            if (r.Y < wa.Top) r.Y = wa.Top;

            if (r != Bounds) Bounds = r;
        }

        bool IsMouseOver(Control c)
        {
            if (c == null || !c.Visible) return false;
            return c.ClientRectangle.Contains(c.PointToClient(Cursor.Position));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                WindowTools.ShowWindow(_session.Window, false);
            }
            else if (Visible)
            {
                WindowTools.ShowWindow(_session.Window, true);
                SyncScrcpyBounds();
            }
            if (_settings != null && _settings.Visible && !_suppressSettingsHide) _settings.Hide();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            SyncScrcpyBounds();
            if (_settings != null && _settings.Visible && !_suppressSettingsHide) _settings.Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _boundsTimer.Stop();
            _volumeTimer.Stop();
            _cameraRetryTimer.Stop();
            if (_mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            _ime.Stop();
            _session.Stop();
            _cfg.Save();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_kbdHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_kbdHook);
                _kbdHook = IntPtr.Zero;
            }
            Application.RemoveMessageFilter(this);
            Log.Write("应用退出");
            base.OnFormClosing(e);
        }

        /// <summary>最小化到任务栏（不隐藏托盘图标）</summary>
        void MinimizeToTaskbar()
        {
            _settings.Hide();
            WindowTools.ShowWindow(_session.Window, false);
            WindowState = FormWindowState.Minimized;
        }

        void HideToTray()
        {
            // 宿主窗口隐藏后 scrcpy 是独立顶层窗口，必须一起隐藏，否则只剩手机画面、看不到工具栏
            // 不再弹气泡通知（系统通知在专注工作时很打扰），托盘图标本身就是提示
            _settings.Hide();
            WindowTools.ShowWindow(_session.Window, false);
            Hide();
        }

        void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            WindowTools.ShowWindow(_session.Window, true);
            SyncScrcpyBounds();
        }

        // ---------- 快捷键 ----------

        /// <summary>重新注册全局快捷键（设置面板关闭后调用，避免录制时误触发）</summary>
        void ApplyHotkeys()
        {
            if (!IsHandleCreated) return;

            // 只有「隐藏到托盘」是全局热键；侧栏显隐由键盘钩子处理，仅本程序内生效
            Native.UnregisterHotKey(Handle, HotkeyTray);
            RegisterHotkey(HotkeyTray, _cfg.TrayHotkey, "隐藏到托盘");
        }

        void RegisterHotkey(int id, Hotkey hk, string what)
        {
            if (hk == null || hk.IsEmpty) return;
            bool ok = Native.RegisterHotKey(Handle, id, hk.Modifiers | Native.MOD_NOREPEAT, (int)hk.Key);
            if (ok) Log.Write("已注册快捷键 " + what + "：" + hk.Describe());
            else Log.Write("注册快捷键失败（可能被其他程序占用）：" + what + " -> " + hk.Describe());
        }

        void SuspendHotkeys()
        {
            if (!IsHandleCreated) return;
            Native.UnregisterHotKey(Handle, HotkeyTray);
        }

        /// <summary>
        /// 键盘钩子：只在本程序（宿主窗口 / 投屏窗口）激活时响应「显示隐藏按钮栏」。
        /// 在其它程序里按同一组合键原样放行，不抢占系统组合键，也不会误操作投屏。
        /// </summary>
        IntPtr HookKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        Hotkey hk = _cfg.BarHotkey;
                        if (hk != null && !hk.IsEmpty)
                        {
                            var data = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                            if ((int)data.vkCode == (int)hk.Key && ModifiersDown(hk.Modifiers) && IsAppForeground())
                            {
                                ToggleSidebar();
                                return (IntPtr)1;   // 吞掉，避免同时也发给手机
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("键盘钩子异常: " + ex.Message);
            }
            return Native.CallNextHookEx(_kbdHook, nCode, wParam, lParam);
        }

        static bool ModifiersDown(int mods)
        {
            bool ctrl = (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0;
            bool alt = (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0;
            bool shift = (Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0;
            bool win = (Native.GetAsyncKeyState(Native.VK_LWIN) & 0x8000) != 0 ||
                       (Native.GetAsyncKeyState(Native.VK_RWIN) & 0x8000) != 0;

            return ctrl == ((mods & Native.MOD_CONTROL) != 0)
                && alt == ((mods & Native.MOD_ALT) != 0)
                && shift == ((mods & Native.MOD_SHIFT) != 0)
                && win == ((mods & Native.MOD_WIN) != 0);
        }

        bool IsAppForeground()
        {
            // 设置面板打开时不响应，避免录制快捷键时被自己的钩子截走
            if (_settings != null && _settings.Visible) return false;

            IntPtr fg = Native.GetForegroundWindow();
            return fg != IntPtr.Zero && (fg == Handle || fg == _session.Window);
        }

        /// <summary>隐藏 / 显示左侧按钮栏；隐藏时窗口同步收窄 88px，让手机画面保持原尺寸不变形</summary>
        void ToggleSidebar()
        {
            bool show = !_sidebar.Visible;

            if (show)
            {
                _sidebar.Visible = true;
                if (_widthBeforeBarHide > 0) Width = _widthBeforeBarHide;
                _widthBeforeBarHide = 0;
            }
            else
            {
                _widthBeforeBarHide = Width;
                _sidebar.Visible = false;
                Width = Math.Max(MinimumSize.Width, Width - SidebarWidth);
            }

            SyncScrcpyBounds();
            Log.Write("侧边按钮栏=" + (show ? "显示" : "隐藏"));
        }

        /// <summary>
        /// 按视频真实尺寸（scrcpy 日志里的 Texture 尺寸）把客户区调成同样的宽高比，
        /// 这样默认状态下画面可以铺满，不会出现上下或左右黑边。
        /// </summary>
        void FitToVideo()
        {
            Size v = _session.VideoSize;
            if (v.Width <= 0 || v.Height <= 0) return;

            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int chrome = _sidebar != null && _sidebar.Visible ? SidebarWidth : 0;

            int height = Math.Min(ClientSize.Height, wa.Height - 40);
            int width = (int)Math.Round(height * (double)v.Width / v.Height);
            int total = chrome + width;

            if (total > wa.Width - 20)
            {
                total = wa.Width - 20;
                width = total - chrome;
                height = (int)Math.Round(width * (double)v.Height / v.Width);
            }
            if (total < MinimumSize.Width || height < MinimumSize.Height) return;
            if (total == ClientSize.Width && height == ClientSize.Height) return;

            ClientSize = new Size(total, height);

            // 适配后可能有一部分跑到屏幕外（横竖屏切换时尤其明显），把它挪回工作区
            int x = Math.Min(Math.Max(Left, wa.Left), Math.Max(wa.Left, wa.Right - Width));
            int y = Math.Min(Math.Max(Top, wa.Top), Math.Max(wa.Top, wa.Bottom - Height));
            if (x != Left || y != Top) Location = new Point(x, y);

            SyncScrcpyBounds();
            Log.Write("按画面比例调整窗口：" + total + "x" + height + "（画面 " + v.Width + "x" + v.Height + "）");
        }

        // ---------- 投屏会话 ----------

        Rectangle ClientBoundsInScreen()
        {
            return _client.RectangleToScreen(new Rectangle(0, 0, _client.Width, _client.Height));
        }

        void SyncScrcpyBounds()
        {
            IntPtr h = _session.Window;
            if (h == IntPtr.Zero) return;
            WindowTools.MoveWindow(h, ClientBoundsInScreen());
        }

        void OnWindowReady()
        {
            RunOnUi(delegate
            {
                // 让 scrcpy 成为本窗口的"所有者窗口"：始终位于宿主之上，但仍是独立顶层窗口，
                // 键盘焦点、UHID、输入法切换链路都不受影响。
                Native.SetWindowLongPtr(_session.Window, Native.GWLP_HWNDPARENT, Handle);
                FitToVideo();   // 画面尺寸可能先于窗口就绪拿到
                SyncScrcpyBounds();
                WindowTools.SetTopMost(_session.Window, _cfg.TopMost);
                WindowTools.ShowWindow(_session.Window, Visible && WindowState != FormWindowState.Minimized);
                Log.Write("投屏窗口已就位，宿主客户区=" + ClientBoundsInScreen());
                _placeholder.Visible = false;
                UpdateStatusText();
            });
        }

        void OnProcessExited()
        {
            bool cameraBusy = _session.CameraBusy;
            RunOnUi(delegate
            {
                _ime.Stop();
                _placeholder.Visible = true;
                UpdateStatusText();
                if (_settings.Visible) _settings.Visible = false;
                if (_cfg.CameraMode && cameraBusy) HandleCameraBusy();
                UpdateTorchButton();
            });
        }

        /// <summary>摄像头被其他应用占用：先自动重试，连续失败则回退普通投屏并提示</summary>
        void HandleCameraBusy()
        {
            _cameraRetry++;
            if (_cameraRetry <= 5)
            {
                Log.Write("摄像头被占用，2 秒后重试（第 " + _cameraRetry + " 次）");
                _cameraRetryTimer.Start();
                return;
            }

            Log.Write("摄像头连续被占用，自动回退普通投屏");
            _cameraRetry = 0;
            _cfg.CameraMode = false;
            _cfg.Save();
            _settings.SyncFromConfig();
            _torchOn = false;
            UpdateStatusText();
            MessageBox.Show(this,
                "摄像头模式启动失败：手机摄像头正被其他应用占用（如相机、视频通话）。\n已自动切回普通投屏。请关闭占用摄像头的应用后，再重新打开摄像头模式。",
                "手机互联", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _cameraRetryTimer.Start();
        }

        void RunOnUi(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(action);
            }
            catch (Exception ex)
            {
                Log.Write("UI 回调失败: " + ex.Message);
            }
        }

        /// <summary>没有显式选择设备时，自动挑选一个可用设备（优先 USB）</summary>
        DeviceInfo ResolveDevice()
        {
            try
            {
                var ready = new System.Collections.Generic.List<DeviceInfo>();
                foreach (DeviceInfo d in Adb.ListDevices())
                {
                    if (d.IsReady) ready.Add(d);
                }
                if (ready.Count == 0) return null;

                foreach (DeviceInfo d in ready)
                {
                    if (d.IsUsb && d.Serial == _cfg.LastUsbSerial) return d;
                }
                foreach (DeviceInfo d in ready)
                {
                    if (d.IsUsb) return d;
                }
                if (ready.Count == 1) return ready[0];
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("自动选择设备失败: " + ex.Message);
                return null;
            }
        }

        void StartSession(DeviceInfo device)
        {
            if (_busy) return;

            if (device == null) device = ResolveDevice();
            if (device == null)
            {
                // 交给用户选择；选择成功后 ConnectForm 会再次调用 StartSession
                OpenConnectDialog();
                return;
            }

            _busy = true;
            try
            {
                _device = device;
                if (!string.IsNullOrEmpty(device.Serial))
                {
                    if (device.IsUsb) _cfg.LastUsbSerial = device.Serial;
                    else _cfg.LastWireless = device.Serial;
                    _cfg.Save();
                }
                Log.Write("投屏设备: " + device.Serial + " (" + (device.IsUsb ? "USB" : "无线") + ")");
                _torchOn = false;
                Adb.StartServer();
                // 摄像头模式：先清理残留的 scrcpy 服务端，避免它继续占用摄像头
                if (_cfg.CameraMode) Adb.KillServer(device.Serial);
                _session.Start(ScrcpyArgs.Build(_cfg, device, ClientBoundsInScreen()));
                // 仅音频模式没有投屏窗口，不需要输入法联动
                if (_cfg.Audio != AudioMode.Only) _ime.Start();
                UpdateStatusText();
            }
            catch (Exception ex)
            {
                Log.Write("启动投屏失败: " + ex.Message);
                MessageBox.Show(this, "启动投屏失败：\n" + ex.Message, "手机互联", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
            }
        }

        void RestartSession(string reason)
        {
            if (!_session.IsRunning && _device == null)
            {
                Log.Write("忽略重启请求（当前没有会话）：" + reason);
                return;
            }
            Log.Write("重启会话：" + reason);
            DeviceInfo device = _device;
            _session.Stop();
            _ime.Stop();
            _placeholder.Visible = true;
            UpdateStatusText();
            Application.DoEvents();
            StartSession(device);
        }

        void StopSession()
        {
            _ime.Stop();
            _session.Stop();
            _placeholder.Visible = true;
            UpdateStatusText();
        }

        /// <summary>断开投屏会话回到未连接状态；设备信息保留，可用「重连」或「设备」再次连接</summary>
        void DisconnectSession()
        {
            if (!_session.IsRunning) return;
            _settings.Hide();
            Log.Write("手动断开投屏连接");
            StopSession();
        }

        void OpenConnectDialog()
        {
            _settings.Hide();
            using (var dlg = new ConnectForm(_cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultDevice != null)
                {
                    StartSession(dlg.ResultDevice);
                }
            }
        }

        // ---------- 工具栏动作 ----------

        void AccumulateVolume(int wheelDelta)
        {
            _wheelRemainder += wheelDelta;
            int steps = _wheelRemainder / 120;
            if (steps == 0) return;
            _wheelRemainder -= steps * 120;
            _pendingVolume += steps;
            _volumeTimer.Stop();
            _volumeTimer.Start();
        }

        void FlushVolume()
        {
            _volumeTimer.Stop();
            int steps = _pendingVolume;
            _pendingVolume = 0;
            if (steps == 0) return;
            AdjustVolume(steps);
        }

        void AdjustVolume(int steps)
        {
            if (steps == 0 || _device == null) return;
            int count = Math.Min(Math.Abs(steps), 6);
            int key = steps > 0 ? KeyCodes.VolumeUp : KeyCodes.VolumeDown;
            string serial = _device.Serial;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < count; i++) Adb.KeyEvent(serial, key);
            });
        }

        void ToggleTopMost()
        {
            _cfg.TopMost = !_cfg.TopMost;
            _cfg.Save();
            TopMost = _cfg.TopMost;
            // TopMost 属性之外再显式设置一次，保证无边框窗口也稳定生效
            WindowTools.SetTopMost(Handle, _cfg.TopMost);
            WindowTools.SetTopMost(_session.Window, _cfg.TopMost);
            UpdateTopButton();
            Log.Write("窗口置顶=" + _cfg.TopMost);
        }

        void UpdateTopButton()
        {
            _btnTop.Text = _cfg.TopMost ? "已置顶" : "置顶";
            _btnTop.Active = _cfg.TopMost;
            _btnTop.ForeColor = _cfg.TopMost ? Theme.Accent : Theme.Text;
            _btnTop.Invalidate();
        }

        /// <summary>侧栏「仅音频」：点一下只传声音（不传画面），再点回到电脑端投屏</summary>
        void ToggleAudioOnly()
        {
            if (_cfg.Audio == AudioMode.Only)
            {
                _cfg.Audio = AudioMode.Pc;
            }
            else
            {
                _cfg.Audio = AudioMode.Only;
                // 仅音频没有画面，摄像头模式没有意义，顺手关掉
                _cfg.CameraMode = false;
            }
            _cfg.Save();
            _settings.SyncFromConfig();
            UpdateAudioButton();
            if (_session.IsRunning) RestartSession("仅音频模式已切换");
            else UpdateStatusText();
        }

        void UpdateAudioButton()
        {
            bool on = _cfg.Audio == AudioMode.Only;
            _btnAudio.Active = on;
            _btnAudio.ForeColor = on ? Theme.Accent : Theme.Text;
            _btnAudio.Invalidate();
        }

        void Reconnect()
        {
            if (!_session.IsRunning)
            {
                StartSession(_device);
                return;
            }
            // 仅音频模式没有投屏窗口，重连就是把音频流重新拉起来
            if (_cfg.Audio == AudioMode.Only)
            {
                RestartSession("仅音频模式：重启音频流");
                return;
            }
            IntPtr h = _session.Window;
            if (h == IntPtr.Zero)
            {
                SyncScrcpyBounds();
                return;
            }
            // scrcpy 的"重置视频采集/编码" = MOD+Shift+R（MOD 默认左 Alt）
            if (!WindowTools.SendShortcut(h, 0x52 /*R*/, true, false))
            {
                RestartSession("重连：快捷键发送失败，改为重启会话");
            }
            else
            {
                Log.Write("已发送重置视频流快捷键");
            }
        }

        /// <summary>保持常亮走 adb，免重启会话</summary>
        void ApplyStayAwake()
        {
            bool on = _cfg.StayAwake;
            string serial = _device == null ? "" : _device.Serial;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string output = Adb.StayOn(serial, on);
                Log.Write("保持常亮=" + on + " -> " + output);
            });
        }

        void ToggleSettings()
        {
            if (_settings.Visible)
            {
                _settings.Hide();
                return;
            }
            _settings.SyncFromConfig();

            // 录制新快捷键时先把已注册的组合键临时注销，避免按下旧组合键直接触发动作
            SuspendHotkeys();

            // 独立顶层窗口：紧贴侧栏右边缘，纵向对齐「设置」按钮
            Point p = _sidebar.PointToScreen(new Point(_sidebar.Width + 12, _btnSettings.Top));
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int x = Math.Min(p.X, wa.Right - _settings.Width - 8);
            int y = Math.Min(p.Y, wa.Bottom - _settings.Height - 8);
            _settings.Location = new Point(Math.Max(wa.Left + 8, x), Math.Max(wa.Top + 8, y));
            _settings.Show(this);
            _settings.BringToFront();
        }

        void ApplyConfigToUi()
        {
            if (_cfg.TopMost) TopMost = true;
            ApplyTheme();
            UpdateStatusText();
        }

        /// <summary>把当前主题应用到主窗口和所有自绘控件</summary>
        void ApplyTheme()
        {
            BackColor = Theme.FormBack;
            _sidebar.BackColor = Theme.ToolbarBack;
            _client.BackColor = Theme.ClientBack;
            _placeholder.BackColor = Theme.PlaceholderBack;

            _hero.BackColor = Theme.CardBack;
            _lblTitle.BackColor = Theme.CardBack;
            _lblHint.BackColor = Theme.CardBack;
            _lblTitle.ForeColor = Theme.Text;
            _lblHint.ForeColor = Theme.SubText;
            _mark.BackColor = Theme.CardBack;
            _mark.ApplyTheme();

            foreach (Control c in _sidebar.Controls)
            {
                var aware = c as IThemeAware;
                if (aware != null) aware.ApplyTheme();
                else if (c is Button) ((Button)c).ForeColor = Theme.Text;
                if (c is CapsuleButton) c.BackColor = Theme.ToolbarBack;
            }
            _btnMin.BackColor = Theme.ToolbarBack;
            _btnClose.BackColor = Theme.ToolbarBack;
            _volume.BackColor = Theme.ToolbarBack;
            _btnClose.ForeColor = Theme.Danger;

            _btnConnect.BackColor = Theme.CardBack;
            _btnConnect.ApplyTheme();
            _volume.ApplyTheme();
            _settings.ApplyTheme();

            UpdateTopButton();
            UpdateTorchButton();
            UpdateAudioButton();
            Invalidate(true);
        }

        void UpdateStatusText()
        {
            if (_session.IsRunning && _device != null)
            {
                _btnDevice.Text = _device.IsUsb ? "USB" : "无线";
            }
            else
            {
                _btnDevice.Text = "设备";
            }

            // 手电筒只在摄像头模式下提供；没有投屏会话时禁用
            _btnTorch.Visible = _cfg.CameraMode;
            _btnTorch.Enabled = _cfg.CameraMode && _session.IsRunning;
            UpdateTorchButton();

            // 没有会话时「断开」不可用
            _btnDisconnect.Enabled = _session.IsRunning;

            // 仅音频模式没有画面，用占位卡片说明当前状态
            bool audioOnly = _session.IsRunning && _cfg.Audio == AudioMode.Only;
            _lblTitle.Text = audioOnly ? "仅音频模式" : "连接你的手机";
            _lblHint.Text = audioOnly
                ? "手机声音正在输出到电脑，不传画面\n在「设置」里切回其他音频档位即可恢复投屏"
                : "USB 数据线连接并开启「USB 调试」\n或让手机与电脑处于同一 WiFi";
            _btnConnect.Visible = !audioOnly;
            LayoutHero();
        }

        void UpdateTorchButton()
        {
            if (_btnTorch == null) return;
            _btnTorch.Active = _torchOn;
            _btnTorch.ForeColor = _torchOn ? Theme.Accent : Theme.Text;
            _btnTorch.Invalidate();
            _tips.SetToolTip(_btnTorch, _torchOn
                ? "手电筒已开，点击关闭"
                : "手电筒已关，点击打开（仅摄像头模式）");
        }

        void ToggleTorch()
        {
            if (!_cfg.CameraMode || !_session.IsRunning) return;
            IntPtr h = _session.Window;
            if (h == IntPtr.Zero) return;

            bool turnOn = !_torchOn;
            // scrcpy：MOD+T 开手电筒，MOD+Shift+T 关手电筒
            if (!WindowTools.SendShortcut(h, 0x54 /*T*/, !turnOn, false)) return;

            _torchOn = turnOn;
            Log.Write("手电筒=" + (turnOn ? "开" : "关"));
            UpdateTorchButton();
        }
    }
}
