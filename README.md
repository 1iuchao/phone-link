# 手机互联 · PhoneLink

Windows 上的 Android 手机投屏工具：把手机屏幕搬进电脑窗口，**鼠标键盘反向控制，中文可以正常输入**。

绿色版、双击即用、不安装、不依赖 .NET SDK 或额外运行时（目标 .NET Framework 4.8，Windows 10/11 自带）。

底层内核是 [scrcpy](https://github.com/Genymobile/scrcpy)，本项目的价值在于把「命令行 + 一堆参数 + 输入法打架」这套东西收进一个能日常用的窗口里。

---

## 功能

| 能力 | 说明 |
|---|---|
| 有线投屏 | USB 数据线，即插即用，无需 root |
| 无线投屏 | 局域网 adb over TCP/IP，支持 Android 11+ 系统「无线调试」扫码/配对码配对 |
| 反向控制 | 鼠标点击拖动、键盘输入，走 scrcpy UHID 模式 |
| **中文输入** | 进投屏窗口自动切英文键盘布局，离开自动切回中文，语言栏不被污染 |
| 音频路由 | 三档：电脑端出声 / 手机端出声 / 双端同时出声 |
| 熄屏投屏 | 手机黑屏但可操作，省电 |
| 保持常亮 | adb 层面设 stay-on，切换不重连 |
| 摄像头模式 | 手机摄像头当电脑摄像头用（Android 12+） |
| 音量控制 | 工具栏 −/+，支持悬停滚轮，80ms 节流合并连续滚动 |
| 窗口置顶 | 一键钉在最前 |
| 断线重连 | 优先发 `Alt+Shift+R` 重置视频采集，失败降级为重启会话 |
| 状态记忆 | 上次设备、连接方式、各项开关写入配置文件 |

界面为无边框自绘窗口（圆角 + 玻璃质感），深色主题，顶部胶囊式工具栏。

---

## 环境要求

**运行**：Windows 10/11 x64，.NET Framework 4.8（系统自带），Android 手机开启 USB 调试。

**编译**：Visual Studio Build Tools 自带的 Roslyn 编译器（不需要完整 VS，也不需要 .NET SDK）：

```
C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe
```

`build.ps1` 会自动在该路径（及 `Program Files (x86)\Microsoft Visual Studio` 下递归）查找 `csc.exe`。

---

## 获取内核

仓库**不包含** scrcpy 二进制（26MB，且属第三方）。编译前先下载并解压到 `tools\`：

1. 打开 [scrcpy releases](https://github.com/Genymobile/scrcpy/releases)
2. 下载 `scrcpy-win64-v5.0.1.zip`
3. 解压为 `tools\scrcpy-win64-v5.0.1\`（保持目录名，`build.ps1` 按此路径拷贝）

国内下载慢可以用镜像前缀：`https://gh-proxy.com/` + 原始 release 地址。

> 参数表与 scrcpy 版本强相关。换内核版本后需复核 `ScrcpySession.cs` 里的启动参数。

---

## 编译

双击 `build.cmd`，或：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物：`dist\手机互联.exe`，同目录 `dist\tools\scrcpy\` 是拷贝过去的内核。**整个 `dist` 目录一起拷走就能用**。

构建流程：
1. 先编译 `tools\IconGen.cs` + `src\AppIcon.cs` 生成 `build\app.ico`
2. 收集 `src\**\*.cs`，用 csc 编译为 `/target:winexe` 并嵌入图标
3. 拷贝 scrcpy 内核到 `dist\tools\scrcpy\`（剔除两个用不上的启动脚本）

---

## 使用

1. 手机 → 开发者选项 → 打开「USB 调试」
2. 插线，运行 `手机互联.exe`，在连接面板选中设备 → 投屏
3. 无线：手机「无线调试」显示配对码 → 应用内填 IP/端口/配对码 → 配对后连接，之后可拔线

配置文件与日志在 `%APPDATA%\手机互联\`（`config.ini`、`log.txt`）。

---

## 目录结构

```
手机互联/
├─ src/                        源码（约 4600 行 C#）
│  ├─ Program.cs               入口、单实例互斥、异常兜底
│  ├─ MainForm.cs              宿主窗口：工具栏 + 投屏客户区 + 窗口跟随
│  ├─ ScrcpySession.cs         scrcpy 进程生命周期、重启、启动参数生成
│  ├─ Adb.cs                   adb 封装（devices / connect / tcpip / pair / keyevent / svc）
│  ├─ ConnectForm.cs           连接面板（USB / 无线 / 配对）
│  ├─ SettingsPanel.cs         设置弹层（音频 / 熄屏 / 常亮 / 摄像头）
│  ├─ ImeAutoSwitch.cs         输入法自动切换（双向还原）
│  ├─ Hotkey.cs / HotkeyBox.cs 全局快捷键
│  ├─ JobObject.cs             进程树随主程序退出，杜绝残留
│  ├─ WindowTools.cs           窗口定位 / 跟随 / 置顶
│  ├─ Native.cs                P/Invoke 声明
│  └─ Theme.cs / Glass.cs / CapsuleButton.cs / GlassPanel.cs / HeroMark.cs / VolumeCapsule.cs
│                              自绘 UI 基础件
├─ tools/
│  ├─ IconGen.cs               图标生成器
│  ├─ ime-autoswitch.ps1       输入法切换的独立验证脚本
│  └─ scrcpy-win64-v5.0.1/     内核（自行下载，不入库）
├─ docs/技术方案.md            设计文档：选型依据、实测结论、分阶段计划、风险清单
├─ 开源方案调研报告.md          投屏方案横评（scrcpy / Escrcpy / AirPlay 等）
├─ build.ps1 / build.cmd
└─ dist/                       编译产物（不入库）
```

---

## 已知限制

- **音频转发需 Android 11+**；Android 10 及以下自动禁用音频
- 双端同时出声用 `--audio-dup`，需 Android 13+，且少数 App 主动退出音频捕获
- 切换音频档位、切换摄像头模式**必须重启 scrcpy 会话**（约 1 秒）
- 摄像头模式下音频强制走手机麦克风，反向控制不可用
- 无线投屏受 Wi-Fi 质量影响，抖动可能断连
- 目标窗口若以管理员权限运行，非提权进程的 `PostMessage` 会被 UIPI 拦截
- 输入法切换是**全局 HKL**，因此进出投屏窗口都必须还原，异常退出有残留风险

---

## 致谢

- [scrcpy](https://github.com/Genymobile/scrcpy) — Apache-2.0，本项目的投屏内核
- [Escrcpy](https://github.com/viarotel-org/escrcpy) — 界面交互上的参考
