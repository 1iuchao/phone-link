# 临时验证脚本：焦点进入 scrcpy 投屏窗口时切英文键盘布局，离开时恢复中文布局。
# 不改系统语言列表，不改变你的输入法清单（仍是微信输入法 + 微软拼音）。
# 常驻运行：无投屏时空转、开有投屏时才工作；脚本自身退出或被杀掉后系统会自动回到中文布局。
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ImeAutoSwitchNative {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
'@

$WM_INPUTLANGCHANGEREQUEST = 0x0050

$hkl_en = [ImeAutoSwitchNative]::LoadKeyboardLayout('00000409', 0)
$hkl_zh = [ImeAutoSwitchNative]::LoadKeyboardLayout('00000804', 0)

function Set-ForegroundLayout([IntPtr]$hkl) {
    $fg = [ImeAutoSwitchNative]::GetForegroundWindow()
    if ($fg -eq [IntPtr]::Zero) { return }
    $procId = 0
    $tid = [ImeAutoSwitchNative]::GetWindowThreadProcessId($fg, [ref]$procId)
    $cur = ([ImeAutoSwitchNative]::GetKeyboardLayout($tid)).ToInt64() -band 0xFFFF
    $want = $hkl.ToInt64() -band 0xFFFF
    if ($cur -ne $want) {
        [ImeAutoSwitchNative]::PostMessage($fg, $WM_INPUTLANGCHANGEREQUEST, [IntPtr]::Zero, $hkl) | Out-Null
    }
}

$last = [IntPtr]::Zero
$hadMirror = $false

while ($true) {
    $scrcpy = Get-Process -Name scrcpy -ErrorAction SilentlyContinue

    if (-not $scrcpy) {
        if ($hadMirror) {
            Set-ForegroundLayout $hkl_zh
            $hadMirror = $false
            $last = [IntPtr]::Zero
        }
        Start-Sleep -Milliseconds 500
        continue
    }

    $hadMirror = $true

    $fg = [ImeAutoSwitchNative]::GetForegroundWindow()
    if ($fg -ne $last) {
        $last = $fg
        $isMirror = [bool]($scrcpy | Where-Object { $_.MainWindowHandle -eq $fg })
        if ($isMirror) {
            Set-ForegroundLayout $hkl_en
        } else {
            Set-ForegroundLayout $hkl_zh
        }
    }

    Start-Sleep -Milliseconds 150
}
