using System;
using System.IO;
using System.Windows.Forms;

namespace PhoneLink
{
    /// <summary>统一的路径解析：绿色版目录下内核位于 tools\scrcpy</summary>
    static class Paths
    {
        public static string AppDir
        {
            get { return Application.StartupPath; }
        }

        /// <summary>scrcpy 内核目录（含 scrcpy.exe、scrcpy-server、adb.exe）</summary>
        public static string ScrcpyDir
        {
            get
            {
                string a = Path.Combine(AppDir, "tools", "scrcpy");
                if (File.Exists(Path.Combine(a, "scrcpy.exe"))) return a;

                // 开发期回退：源码目录旁的原始解压目录
                string b = Path.Combine(AppDir, "tools", "scrcpy-win64-v5.0.1");
                if (File.Exists(Path.Combine(b, "scrcpy.exe"))) return b;

                return a;
            }
        }

        public static string ScrcpyExe { get { return Path.Combine(ScrcpyDir, "scrcpy.exe"); } }
        public static string AdbExe { get { return Path.Combine(ScrcpyDir, "adb.exe"); } }
    }
}
