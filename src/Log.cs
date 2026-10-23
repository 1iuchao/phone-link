using System;
using System.IO;
using System.Text;

namespace PhoneLink
{
    /// <summary>极简文件日志，写到 %APPDATA%\手机互联\log.txt</summary>
    static class Log
    {
        static readonly object Gate = new object();

        public static string Dir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "手机互联");
            }
        }

        public static string LogPath
        {
            get { return Path.Combine(Dir, "log.txt"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine;
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
