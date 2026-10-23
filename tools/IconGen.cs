using System;
using System.IO;

namespace PhoneLink
{
    /// <summary>构建期小工具：把 AppIcon 生成的多尺寸 ICO 写到文件，供 csc /win32icon 嵌入 exe。</summary>
    static class IconGen
    {
        static int Main(string[] args)
        {
            try
            {
                string path = args.Length > 0 ? args[0] : "app.ico";
                File.WriteAllBytes(path, AppIcon.BuildIcoBytes());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("IconGen failed: " + ex.Message);
                return 1;
            }
        }
    }
}
