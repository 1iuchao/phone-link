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

            var hint = MakeLabel("配对成功后，抄手机「无线调试」页显示的 IP 和端口（随机 5 位数）填到上方，别填 5555", 110, 62);
            hint.Size = new Size(300, 32);
            gbPair.Controls.Add(hint);

            _status = new Label();
            _status.Location = new Point(12, 348);
            _status.Size = new Size(320, 46);
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

                if (_list.Items.Count == 0)
                {
                    SetStatus("没有检测到设备。按顺序查：① 手机「USB 调试」是否已打开（设置→系统→开发者选项）"
                            + " ② 插线后手机上是否点了「允许 USB 调试」 ③ 数据线是否只支持充电");
                    return;
                }

                _list.SelectedIndex = 0;

                int ready = 0, unauthorized = 0, offline = 0;
                foreach (DeviceInfo d in devices)
                {
                    if (d.IsReady) ready++;
                    else if (d.State == "unauthorized") unauthorized++;
                    else offline++;
                }

                if (ready > 0)
                {
                    SetStatus("检测到 " + ready + " 个可用设备");
                }
                else if (unauthorized > 0)
                {
                    SetStatus("设备已识别但未授权：请在手机上点「允许 USB 调试」，建议勾选「一律允许使用这台计算机进行调试」");
                }
                else
                {
                    SetStatus("检测到 " + devices.Count + " 个设备，状态为 " + devices[0].State
                            + "。请拔插数据线，或在手机上重新确认调试授权");
                }
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

                // USB 已通，就走最稳的 5555：顺手把手机切到 TCP 模式，IP 和端口一次填好
                string tcp = Adb.EnableTcpip(usb.Serial, 5555);
                bool tcpOk = tcp.IndexOf("restarting in TCP mode", StringComparison.OrdinalIgnoreCase) >= 0;
                _txtWireless.Text = ip + ":5555";
                _txtWireless.SelectionStart = _txtWireless.Text.Length;
                if (tcpOk)
                {
                    SetStatus("IP " + ip + "，已顺带开好 5555 端口。现在可以拔线，点「用已知 IP 连接」");
                }
                else
                {
                    SetStatus("读到 IP " + ip + "，但开 5555 端口失败：" + tcp);
                }
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
                if (output.IndexOf("restarting in TCP mode", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetStatus("已在手机 5555 端口开启无线调试。现在可以拔掉数据线，点「用已知 IP 连接」");
                }
                else
                {
                    SetStatus("开启失败：" + output);
                }
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

                // 端口留空：USB 还连着就顺手把 5555 打开，别让用户卡在半路
                if (endpoint.EndsWith(":"))
                {
                    DeviceInfo usb = FirstUsbReady();
                    if (usb == null)
                    {
                        SetStatus("端口没填。要么插上数据线让我自动开 5555，要么去手机「无线调试」页抄那个随机端口");
                        return;
                    }
                    string tcp = Adb.EnableTcpip(usb.Serial, 5555);
                    if (tcp.IndexOf("restarting in TCP mode", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        SetStatus("开启 5555 端口失败：" + tcp);
                        return;
                    }
                    endpoint += "5555";
                    _txtWireless.Text = endpoint;
                    System.Threading.Thread.Sleep(800);
                }

                string output = Adb.Connect(endpoint);
                bool ok = output.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0 &&
                          output.IndexOf("cannot", StringComparison.OrdinalIgnoreCase) < 0 &&
                          output.IndexOf("failed", StringComparison.OrdinalIgnoreCase) < 0;
                if (!ok)
                {
                    SetStatus(FriendlyConnectError(output, endpoint));
                    return;
                }
                SetStatus(output);

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

        /// <summary>把 adb connect 的原始报错翻成能照着做的提示</summary>
        static string FriendlyConnectError(string raw, string endpoint)
        {
            string host = endpoint;
            string port = "5555";
            int idx = endpoint.LastIndexOf(':');
            if (idx > 0 && idx < endpoint.Length - 1)
            {
                host = endpoint.Substring(0, idx);
                port = endpoint.Substring(idx + 1);
            }

            if (raw.IndexOf("10061", StringComparison.Ordinal) >= 0 ||
                raw.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("积极拒绝", StringComparison.Ordinal) >= 0)
            {
                if (port == "5555")
                {
                    return "端口拒绝（10061）：手机 " + host + ":5555 上没有程序在监听。"
                         + "插上数据线再点「连接」，会自动帮你开 5555；"
                         + "不想插线就去手机「无线调试」页抄那个随机端口填进来";
                }
                return "端口拒绝（10061）：手机 " + host + ":" + port + " 上没有程序在监听。"
                     + "确认手机「无线调试」开关是打开的、端口抄的是页面上那个、电脑与手机在同一 WiFi";
            }

            if (raw.IndexOf("10060", StringComparison.Ordinal) >= 0 ||
                raw.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("超时", StringComparison.Ordinal) >= 0)
            {
                return "连接超时：" + host + " 完全没响应。多半不是同一网段，或手机/路由器防火墙拦了";
            }

            if (raw.IndexOf("cannot resolve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("unknown host", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("名称", StringComparison.Ordinal) >= 0)
            {
                return "IP 解析失败：" + host + " 不是有效地址，检查有没有打错";
            }

            if (raw.IndexOf("10048", StringComparison.Ordinal) >= 0 ||
                raw.IndexOf("10049", StringComparison.Ordinal) >= 0)
            {
                return "本机端口被占用或地址无效：" + endpoint;
            }

            return string.IsNullOrEmpty(raw) ? ("连接 " + endpoint + " 没有返回结果，重试一次") : raw;
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
