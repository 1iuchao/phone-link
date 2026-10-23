using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>连接窗口：设备列表 / USB 投屏 / 无线连接 / 无线调试配对</summary>
    class ConnectForm : Form
    {
        readonly Config _cfg;

        ListBox _list;
        Label _status;

        TextBox _txtWireless;
        TextBox _txtPairHost;
        TextBox _txtPairCode;

        public DeviceInfo ResultDevice { get; private set; }

        public ConnectForm(Config cfg)
        {
            _cfg = cfg;

            Text = "连接手机";
            Icon = AppIcon.Get();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(440, 400);
            BackColor = Color.FromArgb(36, 36, 36);
            ForeColor = Color.Gainsboro;

            var lblList = new Label();
            lblList.Text = "检测到的设备（adb devices）";
            lblList.Location = new Point(12, 10);
            lblList.AutoSize = true;
            Controls.Add(lblList);

            _list = new ListBox();
            _list.Location = new Point(12, 32);
            _list.Size = new Size(320, 110);
            _list.BackColor = Color.FromArgb(28, 28, 28);
            _list.ForeColor = Color.Gainsboro;
            _list.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_list);

            var btnRefresh = MakeButton("刷新", 326, 32, 100, delegate { RefreshDevices(); });
            var btnConnectSel = MakeButton("投屏选中设备", 326, 64, 100, delegate { ConnectSelected(); });
            Controls.Add(btnRefresh);
            Controls.Add(btnConnectSel);

            var gbWireless = new GroupBox();
            gbWireless.Text = "无线连接（手机与电脑同一 WiFi）";
            gbWireless.ForeColor = Color.Gainsboro;
            gbWireless.Location = new Point(12, 152);
            gbWireless.Size = new Size(414, 96);
            Controls.Add(gbWireless);

            _txtWireless = new TextBox();
            _txtWireless.Location = new Point(14, 26);
            _txtWireless.Size = new Size(200, 23);
            _txtWireless.BackColor = Color.FromArgb(28, 28, 28);
            _txtWireless.ForeColor = Color.Gainsboro;
            _txtWireless.Text = _cfg.LastWireless;
            gbWireless.Controls.Add(_txtWireless);

            gbWireless.Controls.Add(MakeButton("从 USB 读取手机 IP", 222, 25, 120, delegate { FillIpFromUsb(); }));
            gbWireless.Controls.Add(MakeButton("用已知 IP 连接", 14, 58, 110, delegate { ConnectWireless(); }));
            gbWireless.Controls.Add(MakeButton("先开启无线调试端口", 130, 58, 140, delegate { EnableTcpip(); }));

            var gbPair = new GroupBox();
            gbPair.Text = "无线调试配对（Android 11+，无需先插线）";
            gbPair.ForeColor = Color.Gainsboro;
            gbPair.Location = new Point(12, 254);
            gbPair.Size = new Size(414, 96);
            Controls.Add(gbPair);

            _txtPairHost = new TextBox();
            _txtPairHost.Location = new Point(14, 26);
            _txtPairHost.Size = new Size(150, 23);
            _txtPairHost.BackColor = Color.FromArgb(28, 28, 28);
            _txtPairHost.ForeColor = Color.Gainsboro;
            gbPair.Controls.Add(_txtPairHost);
            gbPair.Controls.Add(MakeLabel("IP:配对端口", 170, 29));

            _txtPairCode = new TextBox();
            _txtPairCode.Location = new Point(240, 26);
            _txtPairCode.Size = new Size(70, 23);
            _txtPairCode.BackColor = Color.FromArgb(28, 28, 28);
            _txtPairCode.ForeColor = Color.Gainsboro;
            gbPair.Controls.Add(_txtPairCode);
            gbPair.Controls.Add(MakeLabel("配对码", 314, 29));
            gbPair.Controls.Add(MakeButton("开始配对", 14, 56, 90, delegate { Pair(); }));

            var hint = MakeLabel("配对成功后，回到上方「无线连接」填手机的 IP 与无线调试端口（通常是 5555 或无线调试页显示的端口）", 110, 62);
            hint.Size = new Size(300, 32);
            gbPair.Controls.Add(hint);

            _status = new Label();
            _status.Location = new Point(12, 358);
            _status.Size = new Size(320, 32);
            _status.ForeColor = Color.Silver;
            Controls.Add(_status);

            Controls.Add(MakeButton("关闭", 340, 358, 86, delegate { DialogResult = DialogResult.Cancel; }));

            RefreshDevices();
            Theme.Apply(this);
        }

        Label MakeLabel(string text, int x, int y)
        {
            var l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.ForeColor = Color.Silver;
            return l;
        }

        Button MakeButton(string text, int x, int y, int width, EventHandler onClick)
        {
            var b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(width, 26);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            b.BackColor = Color.FromArgb(48, 48, 48);
            b.ForeColor = Color.Gainsboro;
            b.TabStop = false;
            b.Click += onClick;
            return b;
        }

        void SetStatus(string text)
        {
            _status.Text = text;
            Log.Write("[连接窗口] " + text);
        }

        void RefreshDevices()
        {
            try
            {
                Adb.StartServer();
                List<DeviceInfo> devices = Adb.ListDevices();
                _list.Items.Clear();
                foreach (DeviceInfo d in devices) _list.Items.Add(d);
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
                SetStatus("检测到 " + devices.Count + " 个设备");
            }
            catch (Exception ex)
            {
                SetStatus("检测设备失败：" + ex.Message);
            }
        }

        void ConnectSelected()
        {
            var d = _list.SelectedItem as DeviceInfo;
            if (d == null)
            {
                SetStatus("请先在列表里选择一个设备");
                return;
            }
            if (!d.IsReady)
            {
                SetStatus("设备状态为 " + d.State + "，无法投屏（请在手机上允许 USB 调试）");
                return;
            }
            ResultDevice = d;
            DialogResult = DialogResult.OK;
        }

        DeviceInfo FirstUsbReady()
        {
            foreach (DeviceInfo d in Adb.ListDevices())
            {
                if (d.IsUsb && d.IsReady) return d;
            }
            return null;
        }

        void FillIpFromUsb()
        {
            try
            {
                DeviceInfo usb = FirstUsbReady();
                if (usb == null)
                {
                    SetStatus("没有检测到 USB 连接的手机，请先插数据线");
                    return;
                }
                string ip = Adb.GetDeviceIp(usb.Serial);
                if (string.IsNullOrEmpty(ip))
                {
                    SetStatus("读取手机 IP 失败，请确认手机已连 WiFi");
                    return;
                }
                _txtWireless.Text = ip + ":5555";
                SetStatus("已读取手机 IP：" + ip);
            }
            catch (Exception ex)
            {
                SetStatus("读取 IP 失败：" + ex.Message);
            }
        }

        void EnableTcpip()
        {
            try
            {
                DeviceInfo usb = FirstUsbReady();
                if (usb == null)
                {
                    SetStatus("开启无线调试端口需要先用 USB 连接一次手机");
                    return;
                }
                string output = Adb.EnableTcpip(usb.Serial, 5555);
                SetStatus("已开启无线调试端口：" + output);
            }
            catch (Exception ex)
            {
                SetStatus("开启失败：" + ex.Message);
            }
        }

        void ConnectWireless()
        {
            try
            {
                string endpoint = _txtWireless.Text.Trim();
                if (endpoint.Length == 0)
                {
                    SetStatus("请填写手机 IP:端口");
                    return;
                }
                if (endpoint.IndexOf(':') < 0) endpoint += ":5555";

                string output = Adb.Connect(endpoint);
                SetStatus(output);
                if (output.IndexOf("connected", StringComparison.OrdinalIgnoreCase) < 0 &&
                    output.IndexOf("already", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                _cfg.LastWireless = endpoint;
                _cfg.Save();

                foreach (DeviceInfo d in Adb.ListDevices())
                {
                    if (d.Serial == endpoint && d.IsReady)
                    {
                        ResultDevice = d;
                        DialogResult = DialogResult.OK;
                        return;
                    }
                }
                SetStatus("已连接 " + endpoint + "，但设备状态还不是 device，请在手机上确认授权后重试");
            }
            catch (Exception ex)
            {
                SetStatus("无线连接失败：" + ex.Message);
            }
        }

        void Pair()
        {
            try
            {
                string host = _txtPairHost.Text.Trim();
                string code = _txtPairCode.Text.Trim();
                if (host.Length == 0 || code.Length == 0)
                {
                    SetStatus("请填写 IP:配对端口 与 配对码");
                    return;
                }
                string output = Adb.Pair(host, code);
                SetStatus(output.Length == 0 ? "配对命令已执行" : output);
                if (output.IndexOf("Successfully paired", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetStatus("配对成功。请到手机无线调试页查看该 WiFi 下的 IP 与端口，填到上方「无线连接」后点连接");
                }
            }
            catch (Exception ex)
            {
                SetStatus("配对失败：" + ex.Message);
            }
        }
    }
}
