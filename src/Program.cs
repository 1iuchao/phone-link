using System;
using System.Threading;
using System.Windows.Forms;

namespace PhoneLink
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "PhoneLink_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("手机互联已在运行。", "手机互联", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Native.EnablePerMonitorDpi();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("界面线程异常: " + e.Exception);
                    MessageBox.Show("发生异常：\n" + e.Exception.Message, "手机互联", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("未处理异常: " + e.ExceptionObject);
                };

                Application.Run(new MainForm());
            }
        }
    }
}
