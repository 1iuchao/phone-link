using System;
using System.Drawing;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>
    /// 设置弹层：音频输出 / 熄屏投屏 / 保持常亮 / 摄像头模式 / 主题。
    /// 必须是独立顶层窗口——scrcpy 窗口始终位于宿主之上，内嵌面板会被挡住。
    /// </summary>
    class SettingsPanel : Form
    {
        readonly Config _cfg;

        RadioButton _rbPc;
        RadioButton _rbPhone;
        RadioButton _rbBoth;
        RadioButton _rbOnly;
        RadioButton _rbDark;
        RadioButton _rbLight;
        RadioButton _rbCloseTray;
        RadioButton _rbCloseExit;
        CheckBox _ckScreenOff;
        CheckBox _ckAwake;
        CheckBox _ckCamera;
        Label _lblCameraHint;
        Label _lblTitle;
        Label _lblAudio;
        Label _lblTheme;
        Label _lblClose;
        Label _lblHotkey;
        HotkeyBox _hkBar;
        HotkeyBox _hkTray;

        bool _suppress;

        public event Action AudioChanged;
        public event Action TurnScreenOffChanged;
        public event Action StayAwakeChanged;
        public event Action CameraChanged;
        public event Action ThemeChanged;
        public event Action CloseActionChanged;
        public event Action HotkeysChanged;

        public SettingsPanel(Config cfg)
        {
            _cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // 弹层始终盖在投屏画面之上，避免被 scrcpy 窗口挡住
            TopMost = true;
            ClientSize = new Size(310, 360);
            BackColor = Theme.CardBack;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9F);

            _lblTitle = new Label();
            _lblTitle.Text = "设置";
            _lblTitle.ForeColor = Theme.Text;
            _lblTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            _lblTitle.Location = new Point(12, 8);
            _lblTitle.AutoSize = true;
            Controls.Add(_lblTitle);

            _lblAudio = new Label();
            _lblAudio.Text = "音频输出";
            _lblAudio.ForeColor = Theme.SubText;
            _lblAudio.Location = new Point(12, 38);
            _lblAudio.AutoSize = true;
            Controls.Add(_lblAudio);

            Panel audioGroup = MakeGroup(96, 34, 212, 96);
            _rbPc = MakeRadio(audioGroup, "电脑端（默认，手机静音）", 0, 0);
            _rbPhone = MakeRadio(audioGroup, "手机端", 0, 24);
            _rbBoth = MakeRadio(audioGroup, "两端同时（需 Android 13+）", 0, 48);
            _rbOnly = MakeRadio(audioGroup, "仅音频（不传画面）", 0, 72);
            _rbPc.CheckedChanged += OnAudioChanged;
            _rbPhone.CheckedChanged += OnAudioChanged;
            _rbBoth.CheckedChanged += OnAudioChanged;
            _rbOnly.CheckedChanged += OnAudioChanged;

            _ckScreenOff = MakeCheck("熄屏投屏（手机黑屏但继续投屏）", 12, 134, 290);
            _ckScreenOff.CheckedChanged += delegate
            {
                if (_suppress) return;
                _cfg.TurnScreenOff = _ckScreenOff.Checked;
                _cfg.Save();
                Raise(TurnScreenOffChanged);
            };

            _ckAwake = MakeCheck("保持常亮（配合熄屏使用更省电）", 12, 158, 290);
            _ckAwake.CheckedChanged += delegate
            {
                if (_suppress) return;
                _cfg.StayAwake = _ckAwake.Checked;
                _cfg.Save();
                Raise(StayAwakeChanged);
            };

            _ckCamera = MakeCheck("摄像头模式（手机当电脑摄像头）", 12, 182, 290);
            _ckCamera.CheckedChanged += delegate
            {
                if (_suppress) return;
                _cfg.CameraMode = _ckCamera.Checked;
                _cfg.Save();
                _lblCameraHint.Visible = _ckCamera.Checked;
                Raise(CameraChanged);
            };

            _lblCameraHint = new Label();
            _lblCameraHint.Text = "摄像头模式下音频走手机麦克风、无法反向控制手机";
            _lblCameraHint.ForeColor = Color.FromArgb(230, 180, 110);
            _lblCameraHint.Location = new Point(28, 204);
            _lblCameraHint.Size = new Size(270, 16);
            _lblCameraHint.Visible = false;
            Controls.Add(_lblCameraHint);

            _lblTheme = new Label();
            _lblTheme.Text = "界面主题";
            _lblTheme.ForeColor = Theme.SubText;
            _lblTheme.Location = new Point(12, 228);
            _lblTheme.AutoSize = true;
            Controls.Add(_lblTheme);

            Panel themeGroup = MakeGroup(96, 224, 150, 22);
            _rbDark = MakeRadio(themeGroup, "深色", 0, 0);
            _rbLight = MakeRadio(themeGroup, "浅色", 60, 0);
            _rbDark.CheckedChanged += OnThemeChanged;
            _rbLight.CheckedChanged += OnThemeChanged;

            _lblClose = new Label();
            _lblClose.Text = "关闭按钮";
            _lblClose.ForeColor = Theme.SubText;
            _lblClose.Location = new Point(12, 256);
            _lblClose.AutoSize = true;
            Controls.Add(_lblClose);

            Panel closeGroup = MakeGroup(96, 252, 200, 22);
            _rbCloseTray = MakeRadio(closeGroup, "收进托盘", 0, 0);
            _rbCloseExit = MakeRadio(closeGroup, "直接退出", 80, 0);
            _rbCloseTray.CheckedChanged += OnCloseActionChanged;
            _rbCloseExit.CheckedChanged += OnCloseActionChanged;

            // 全局快捷键：隐藏侧边按钮栏 / 隐藏到托盘
            _lblHotkey = new Label();
            _lblHotkey.Text = "快捷键（点击输入框后按下组合键，Esc 清除）";
            _lblHotkey.ForeColor = Theme.SubText;
            _lblHotkey.Font = new Font("Microsoft YaHei UI", 8.5F);
            _lblHotkey.Location = new Point(12, 282);
            _lblHotkey.AutoSize = true;
            Controls.Add(_lblHotkey);

            // 按钮栏只在程序窗口激活时生效（局部）；托盘是系统级全局热键
            AddHotkeyRow("按钮栏·局部", 306, out _hkBar, delegate
            {
                _cfg.BarHotkey = _hkBar.Value;
                _cfg.Save();
                Raise(HotkeysChanged);
            });

            AddHotkeyRow("托盘·全局", 334, out _hkTray, delegate
            {
                _cfg.TrayHotkey = _hkTray.Value;
                _cfg.Save();
                Raise(HotkeysChanged);
            });

            SyncFromConfig();
        }

        void AddHotkeyRow(string caption, int y, out HotkeyBox box, Action onChanged)
        {
            var lbl = new Label();
            lbl.Text = caption;
            lbl.ForeColor = Theme.SubText;
            lbl.Location = new Point(12, y + 4);
            lbl.AutoSize = true;
            Controls.Add(lbl);

            var b = new HotkeyBox();
            b.Location = new Point(100, y);
            b.Size = new Size(196, 24);
            b.ValueChanged += delegate { if (onChanged != null) onChanged(); };
            Controls.Add(b);
            box = b;
        }

        /// <summary>
        /// 每个选项组一个独立容器：WinForms 会把同一容器里的 RadioButton 当成一组自动互斥，
        /// 全挂在 Form 上会导致选主题时把「关闭按钮」「音频输出」的选中状态一起清掉。
        /// </summary>
        Panel MakeGroup(int x, int y, int width, int height)
        {
            var p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(width, height);
            p.BackColor = Theme.CardBack;
            Controls.Add(p);
            return p;
        }

        RadioButton MakeRadio(Panel group, string text, int x, int y)
        {
            var r = new RadioButton();
            r.Text = text;
            r.ForeColor = Theme.Text;
            r.Location = new Point(x, y);
            r.AutoSize = true;
            r.TabStop = false;
            group.Controls.Add(r);
            return r;
        }

        CheckBox MakeCheck(string text, int x, int y, int width)
        {
            var c = new CheckBox();
            c.Text = text;
            c.ForeColor = Theme.Text;
            c.Location = new Point(x, y);
            c.Size = new Size(width, 20);
            c.TabStop = false;
            Controls.Add(c);
            return c;
        }

        static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        void OnAudioChanged(object sender, EventArgs e)
        {
            if (_suppress) return;
            var rb = sender as RadioButton;
            if (rb == null || !rb.Checked) return;

            AudioMode mode = AudioMode.Pc;
            if (rb == _rbPhone) mode = AudioMode.Phone;
            else if (rb == _rbBoth) mode = AudioMode.Both;
            else if (rb == _rbOnly) mode = AudioMode.Only;

            if (mode == _cfg.Audio) return;
            _cfg.Audio = mode;
            _cfg.Save();
            Raise(AudioChanged);
        }

        public void SyncFromConfig()
        {
            _suppress = true;
            _rbPc.Checked = _cfg.Audio == AudioMode.Pc;
            _rbPhone.Checked = _cfg.Audio == AudioMode.Phone;
            _rbBoth.Checked = _cfg.Audio == AudioMode.Both;
            _rbOnly.Checked = _cfg.Audio == AudioMode.Only;
            _ckScreenOff.Checked = _cfg.TurnScreenOff;
            _ckAwake.Checked = _cfg.StayAwake;
            _ckCamera.Checked = _cfg.CameraMode;
            _rbDark.Checked = !_cfg.LightTheme;
            _rbLight.Checked = _cfg.LightTheme;
            _rbCloseTray.Checked = _cfg.CloseToTray;
            _rbCloseExit.Checked = !_cfg.CloseToTray;
            _hkBar.Value = _cfg.BarHotkey;
            _hkTray.Value = _cfg.TrayHotkey;
            _lblCameraHint.Visible = _cfg.CameraMode;
            _suppress = false;
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            if (_suppress) return;
            var rb = sender as RadioButton;
            if (rb == null || !rb.Checked) return;

            bool light = rb == _rbLight;
            if (light == _cfg.LightTheme) return;
            _cfg.LightTheme = light;
            _cfg.Save();
            Raise(ThemeChanged);
        }

        void OnCloseActionChanged(object sender, EventArgs e)
        {
            if (_suppress) return;
            var rb = sender as RadioButton;
            if (rb == null || !rb.Checked) return;

            bool toTray = rb == _rbCloseTray;
            if (toTray == _cfg.CloseToTray) return;
            _cfg.CloseToTray = toTray;
            _cfg.Save();
            Raise(CloseActionChanged);
        }

        public void ApplyTheme()
        {
            BackColor = Theme.CardBack;
            _lblTitle.ForeColor = Theme.Text;
            _lblAudio.ForeColor = Theme.SubText;
            _lblTheme.ForeColor = Theme.SubText;
            _lblClose.ForeColor = Theme.SubText;
            if (_lblHotkey != null) _lblHotkey.ForeColor = Theme.SubText;
            if (_hkBar != null) _hkBar.ApplyTheme();
            if (_hkTray != null) _hkTray.ApplyTheme();
            ApplyColors(Controls);
            Invalidate();
        }

        void ApplyColors(Control.ControlCollection controls)
        {
            foreach (Control c in controls)
            {
                if (c is Panel)
                {
                    c.BackColor = Theme.CardBack;
                    ApplyColors(c.Controls);
                }
                else if (c is Label)
                {
                    if (c != _lblCameraHint) c.ForeColor = (c == _lblTitle) ? Theme.Text : Theme.SubText;
                }
                else if (c is RadioButton || c is CheckBox)
                {
                    c.ForeColor = Theme.Text;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var rim = new System.Drawing.Drawing2D.LinearGradientBrush(r,
                Color.FromArgb(Theme.Light ? 205 : 62, 255, 255, 255),
                Color.FromArgb(Theme.Light ? 130 : 16, 255, 255, 255),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            using (var pen = new Pen(rim))
            {
                e.Graphics.DrawRectangle(pen, r);
            }
            using (var hl = new Pen(Color.FromArgb(Theme.Light ? 220 : 80, 255, 255, 255)))
            {
                e.Graphics.DrawLine(hl, 1, 1, Width - 2, 1);
            }
        }
    }
}
