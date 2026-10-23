using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PhoneLink
{
    /// <summary>音频输出档位</summary>
    enum AudioMode
    {
        /// <summary>电脑端出声（scrcpy 默认，手机静音）</summary>
        Pc = 0,
        /// <summary>手机端出声（--no-audio）</summary>
        Phone = 1,
        /// <summary>两端同时出声（--audio-dup，需 Android 13+）</summary>
        Both = 2,
        /// <summary>只把声音传到电脑，不传画面（--no-video）</summary>
        Only = 3
    }

    /// <summary>配置持久化：%APPDATA%\手机互联\config.ini（无第三方依赖，手写键值对）</summary>
    class Config
    {
        public string LastUsbSerial = "";
        public string LastWireless = "";
        public AudioMode Audio = AudioMode.Pc;
        public bool TurnScreenOff = false;
        public bool StayAwake = false;
        public bool CameraMode = false;
        public bool TopMost = false;
        public bool LightTheme = false;
        /// <summary>点「✕」时收进托盘（false = 直接退出）</summary>
        public bool CloseToTray = true;

        /// <summary>全局快捷键：隐藏 / 显示左侧按钮栏（默认 Ctrl+Alt+H）</summary>
        public Hotkey BarHotkey = new Hotkey(Native.MOD_CONTROL | Native.MOD_ALT, System.Windows.Forms.Keys.H);
        /// <summary>全局快捷键：隐藏到系统托盘（默认 Ctrl+Alt+M，再按一次恢复）</summary>
        public Hotkey TrayHotkey = new Hotkey(Native.MOD_CONTROL | Native.MOD_ALT, System.Windows.Forms.Keys.M);

        static string ConfigPath
        {
            get { return Path.Combine(Log.Dir, "config.ini"); }
        }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (!File.Exists(ConfigPath)) return c;
                foreach (string raw in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "lastUsbSerial": c.LastUsbSerial = v; break;
                        case "lastWireless": c.LastWireless = v; break;
                        case "audio": c.Audio = ParseAudio(v); break;
                        case "turnScreenOff": c.TurnScreenOff = v == "1"; break;
                        case "stayAwake": c.StayAwake = v == "1"; break;
                        case "cameraMode": c.CameraMode = v == "1"; break;
                        case "topMost": c.TopMost = v == "1"; break;
                        case "lightTheme": c.LightTheme = v == "1"; break;
                        case "closeToTray": c.CloseToTray = v == "1"; break;
                        case "barHotkey": c.BarHotkey = Hotkey.Parse(v); break;
                        case "trayHotkey": c.TrayHotkey = Hotkey.Parse(v); break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取配置失败: " + ex.Message);
            }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# 手机互联 配置文件");
                sb.AppendLine("lastUsbSerial=" + LastUsbSerial);
                sb.AppendLine("lastWireless=" + LastWireless);
                sb.AppendLine("audio=" + Audio.ToString().ToLowerInvariant());
                sb.AppendLine("turnScreenOff=" + (TurnScreenOff ? "1" : "0"));
                sb.AppendLine("stayAwake=" + (StayAwake ? "1" : "0"));
                sb.AppendLine("cameraMode=" + (CameraMode ? "1" : "0"));
                sb.AppendLine("topMost=" + (TopMost ? "1" : "0"));
                sb.AppendLine("lightTheme=" + (LightTheme ? "1" : "0"));
                sb.AppendLine("closeToTray=" + (CloseToTray ? "1" : "0"));
                sb.AppendLine("barHotkey=" + BarHotkey.ToString());
                sb.AppendLine("trayHotkey=" + TrayHotkey.ToString());
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Write("保存配置失败: " + ex.Message);
            }
        }

        static AudioMode ParseAudio(string v)
        {
            switch ((v ?? "").ToLowerInvariant())
            {
                case "phone": return AudioMode.Phone;
                case "both": return AudioMode.Both;
                case "only": return AudioMode.Only;
                default: return AudioMode.Pc;
            }
        }
    }
}
