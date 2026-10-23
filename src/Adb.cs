using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace PhoneLink
{
    class DeviceInfo
    {
        public string Serial = "";
        public string State = "";
        public string Model = "";

        public bool IsUsb
        {
            get { return Serial.IndexOf(':') < 0; }
        }

        public bool IsReady
        {
            get { return State == "device"; }
        }

        public override string ToString()
        {
            string kind = IsUsb ? "USB" : "无线";
            string name = string.IsNullOrEmpty(Model) ? Serial : (Model + "  (" + Serial + ")");
            return kind + " · " + name + " · " + State;
        }
    }

    /// <summary>adb 命令封装（使用内核目录里自带的 adb.exe）</summary>
    static class Adb
    {
        public static string Run(string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo(Paths.AdbExe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Paths.ScrcpyDir
            };
            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return stdout + stderr;
                }
                return (stdout + stderr).Trim();
            }
        }

        public static string Run(string arguments)
        {
            return Run(arguments, 20000);
        }

        public static void StartServer()
        {
            Run("start-server", 20000);
        }

        /// <summary>解析 adb devices -l 输出</summary>
        public static List<DeviceInfo> ListDevices()
        {
            var list = new List<DeviceInfo>();
            string output = Run("devices -l", 15000);
            foreach (string raw in output.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("List of devices")) continue;
                if (line.StartsWith("*")) continue;

                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var d = new DeviceInfo();
                d.Serial = parts[0];
                d.State = parts[1];
                for (int i = 2; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("model:"))
                    {
                        d.Model = parts[i].Substring("model:".Length);
                    }
                }
                list.Add(d);
            }
            return list;
        }

        public static string Connect(string ipPort)
        {
            return Run("connect " + ipPort, 20000);
        }

        public static string Disconnect(string ipPort)
        {
            return Run("disconnect " + ipPort, 15000);
        }

        public static string EnableTcpip(string serial, int port)
        {
            string target = string.IsNullOrEmpty(serial) ? "" : ("-s " + serial + " ");
            return Run(target + "tcpip " + port, 20000);
        }

        public static string Pair(string ipPort, string code)
        {
            return Run("pair " + ipPort + " " + code, 40000);
        }

        public static string GetDeviceIp(string serial)
        {
            string target = string.IsNullOrEmpty(serial) ? "" : ("-s " + serial + " ");
            string output = Run(target + "shell ip -f inet addr show wlan0", 15000);
            foreach (string raw in output.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                int i = line.IndexOf("inet ", StringComparison.Ordinal);
                if (i >= 0)
                {
                    string rest = line.Substring(i + 5).Trim();
                    int slash = rest.IndexOf('/');
                    if (slash > 0) return rest.Substring(0, slash);
                }
            }
            return "";
        }

        /// <summary>结束可能残留的 scrcpy 服务端进程，释放摄像头等资源</summary>
        public static void KillServer(string serial)
        {
            string target = string.IsNullOrEmpty(serial) ? "" : ("-s " + serial + " ");
            Run(target + "shell pkill -f com.genymobile.scrcpy.Server", 5000);
        }

        /// <summary>发送按键事件。多设备同时在线时必须带 -s，否则 adb 会报错</summary>
        public static void KeyEvent(string serial, int keyCode)
        {
            string target = string.IsNullOrEmpty(serial) ? "" : ("-s " + serial + " ");
            Run(target + "shell input keyevent " + keyCode, 8000);
        }

        /// <summary>设置手机是否保持常亮（adb 方式，免重启会话）</summary>
        public static string StayOn(string serial, bool on)
        {
            string target = string.IsNullOrEmpty(serial) ? "" : ("-s " + serial + " ");
            return Run(target + "shell svc power stayon " + (on ? "true" : "false"), 10000);
        }
    }

    static class KeyCodes
    {
        public const int VolumeUp = 24;
        public const int VolumeDown = 25;
    }
}
