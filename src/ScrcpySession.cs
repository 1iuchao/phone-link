using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace PhoneLink
{
    /// <summary>scrcpy 启动参数生成</summary>
    static class ScrcpyArgs
    {
        public const string WindowTitle = "手机互联";

        /// <summary>无边框窗口的初始位置（由宿主窗口客户区决定）</summary>
        public static string Build(Config cfg, DeviceInfo device, System.Drawing.Rectangle bounds)
        {
            if (device == null || string.IsNullOrEmpty(device.Serial))
            {
                throw new InvalidOperationException("没有可用的设备（未指定序列号）");
            }

            // 仅音频：不建视频流、不建窗口，只把声音转发到电脑
            bool audioOnly = cfg.Audio == AudioMode.Only;

            var sb = new StringBuilder();
            // 仅音频模式没有窗口，不需要虚拟键盘
            if (!audioOnly) sb.Append("--keyboard=uhid ");

            // 连接方式：始终显式指定序列号，避免多设备时 scrcpy 报错
            sb.Append("--serial=").Append(device.Serial);

            if (audioOnly)
            {
                // --no-video 本身仍会建一个空窗口（scrcpy 默认的图标窗口），
                // 必须再加 --no-window，否则会多弹出一个和主面板无关的独立窗口
                sb.Append(" --no-video --no-window");
            }
            else
            {
                sb.Append(" --window-borderless");
                sb.Append(" --no-window-aspect-ratio-lock");
                sb.Append(" --window-title=").Append(WindowTitle);
                sb.Append(" --window-x=").Append(bounds.X);
                sb.Append(" --window-y=").Append(bounds.Y);
                sb.Append(" --window-width=").Append(bounds.Width);
                sb.Append(" --window-height=").Append(bounds.Height);
            }

            // 视频源 / 音频档位
            if (audioOnly)
            {
                // 只走默认的电脑端音频输出
            }
            else if (cfg.CameraMode)
            {
                sb.Append(" --video-source=camera");
                // 摄像头流默认方向与手机传感器差 90°，旋转 90° 后画面才是正的
                sb.Append(" --capture-orientation=90");
                if (cfg.Audio == AudioMode.Phone) sb.Append(" --no-audio");
            }
            else
            {
                if (cfg.Audio == AudioMode.Phone) sb.Append(" --no-audio");
                else if (cfg.Audio == AudioMode.Both) sb.Append(" --audio-dup");
            }

            if (cfg.StayAwake) sb.Append(" --stay-awake");
            if (cfg.TurnScreenOff) sb.Append(" --turn-screen-off");

            return sb.ToString();
        }
    }

    /// <summary>scrcpy 会话：进程生命周期 + 窗口句柄发现</summary>
    class ScrcpySession : IDisposable
    {
        public Process Process { get; private set; }
        public string LastArguments { get; private set; }

        IntPtr _window = IntPtr.Zero;
        IntPtr _job = IntPtr.Zero;
        Thread _watcher;
        volatile bool _watching;
        /// <summary>会话代次：旧进程的退出事件不能影响新会话</summary>
        volatile int _generation;
        /// <summary>本次会话是否因摄像头被占用而失败</summary>
        volatile bool _cameraBusy;
        /// <summary>视频真实尺寸（scrcpy 日志里的 Texture 尺寸），用于把窗口调成同比例、消除黑边</summary>
        volatile int _videoWidth;
        volatile int _videoHeight;

        public bool CameraBusy { get { return _cameraBusy; } }

        public System.Drawing.Size VideoSize
        {
            get { return new System.Drawing.Size(_videoWidth, _videoHeight); }
        }

        /// <summary>投屏窗口就绪（句柄可用）</summary>
        public event Action WindowReady;
        /// <summary>scrcpy 进程退出</summary>
        public event Action ProcessExited;
        /// <summary>拿到视频真实尺寸（首次出现或旋转后尺寸变化时触发）</summary>
        public event Action VideoSizeKnown;

        public IntPtr Window
        {
            get
            {
                if (_window != IntPtr.Zero && !Native.IsWindow(_window)) _window = IntPtr.Zero;
                return _window;
            }
        }

        public bool IsRunning
        {
            get
            {
                try { return Process != null && !Process.HasExited; }
                catch { return false; }
            }
        }

        public void Start(string arguments)
        {
            Stop();
            int gen = _generation;
            _cameraBusy = false;
            _videoWidth = 0;
            _videoHeight = 0;

            LastArguments = arguments;
            Log.Write("启动 scrcpy: " + arguments);

            var psi = new ProcessStartInfo(Paths.ScrcpyExe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Paths.ScrcpyDir
            };

            var proc = new Process();
            proc.StartInfo = psi;
            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { CaptureOutput(e.Data); };
            proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { CaptureOutput(e.Data); };
            proc.Exited += delegate
            {
                int code = -1;
                try { code = proc.ExitCode; } catch { }
                Log.Write("scrcpy 进程已退出，退出码 " + code);
                if (gen != _generation) return; // 已被新会话/停止取代，忽略旧进程的退出
                var h = ProcessExited;
                if (h != null) h();
            };
            Process = proc;
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // 放进 kill-on-close 的 Job：本程序异常退出时系统会自动结束 scrcpy，避免孤儿窗口
            _job = JobObject.Create();
            if (_job != IntPtr.Zero && !JobObject.Assign(_job, proc.Handle))
            {
                Log.Write("scrcpy 加入 Job 失败，将退化为仅靠正常退出时清理");
                JobObject.Close(_job);
                _job = IntPtr.Zero;
            }

            _watching = true;
            // 仅音频模式（--no-video）不会有 scrcpy 窗口，没必要一直去枚举顶层窗口
            if (arguments.IndexOf("--no-video", StringComparison.Ordinal) >= 0)
            {
                _watching = false;
                Log.Write("仅音频模式：不等待投屏窗口");
                return;
            }
            _watcher = new Thread((ThreadStart)delegate { WatchWindow(proc, gen); });
            _watcher.IsBackground = true;
            _watcher.Name = "ScrcpyWindowWatcher";
            _watcher.Start();
        }

        void CaptureOutput(string data)
        {
            if (data == null) return;
            Log.Write("[scrcpy] " + data);
            if (data.IndexOf("CAMERA_IN_USE", StringComparison.Ordinal) >= 0 ||
                data.IndexOf("Higher-priority client using camera", StringComparison.Ordinal) >= 0)
            {
                _cameraBusy = true;
            }
            if (data.IndexOf("Texture", StringComparison.Ordinal) >= 0) ParseTextureSize(data);
        }

        /// <summary>解析 "INFO: Texture (D3D11VA): 1264x2800" 这一行</summary>
        void ParseTextureSize(string line)
        {
            int colon = line.LastIndexOf(':');
            if (colon < 0) return;

            string tail = line.Substring(colon + 1).Trim();
            string[] parts = tail.Split('x');
            if (parts.Length != 2) return;

            int w, h;
            if (!int.TryParse(parts[0], out w) || !int.TryParse(parts[1], out h)) return;
            if (w <= 0 || h <= 0) return;
            if (w == _videoWidth && h == _videoHeight) return;

            _videoWidth = w;
            _videoHeight = h;
            var handler = VideoSizeKnown;
            if (handler != null) handler();
        }

        void WatchWindow(Process proc, int gen)
        {
            int pid;
            try { pid = proc.Id; }
            catch { return; }

            for (int i = 0; i < 100 && _watching && gen == _generation; i++) // 最多等待约 20 秒
            {
                try { if (proc.HasExited) return; }
                catch { return; }

                IntPtr h = WindowTools.FindTopLevelWindow(pid);
                if (h != IntPtr.Zero)
                {
                    if (gen != _generation) return; // 会话已被替换，丢弃这个旧窗口
                    _window = h;
                    Log.Write("找到 scrcpy 窗口 HWND=0x" + h.ToInt64().ToString("X"));
                    var ready = WindowReady;
                    if (ready != null) ready();
                    return;
                }
                Thread.Sleep(200);
            }
            Log.Write("等待 scrcpy 窗口超时");
        }

        /// <summary>停止会话。kill=true 时强制结束进程。</summary>
        public void Stop()
        {
            _generation++;   // 让旧进程的退出事件失效
            _watching = false;
            _window = IntPtr.Zero;
            try
            {
                if (_watcher != null) _watcher.Join(500);
            }
            catch
            {
            }
            _watcher = null;

            try
            {
                if (Process != null && !Process.HasExited)
                {
                    Process.Kill();
                    Process.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Log.Write("结束 scrcpy 进程失败: " + ex.Message);
            }
            finally
            {
                JobObject.Close(_job);
                _job = IntPtr.Zero;
                try { if (Process != null) Process.Dispose(); } catch { }
                Process = null;
                Log.Write("scrcpy 会话已停止");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
