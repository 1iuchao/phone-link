# Build script (ASCII only on purpose: avoids Windows PowerShell ANSI encoding issues).
# Compiles src\**\*.cs into dist\ into a WinForms exe, then copies the scrcpy kernel beside it.
# This machine has no .NET SDK, so we use the Roslyn csc.exe shipped with VS Build Tools,
# targeting the built-in .NET Framework 4.8.x runtime (no runtime install required).
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$dist   = Join-Path $root 'dist'

function Find-Csc {
    $direct = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
    if (Test-Path $direct) { return $direct }
    $hit = Get-ChildItem 'C:\Program Files (x86)\Microsoft Visual Studio' -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -match 'Roslyn' } | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    throw 'Roslyn csc.exe not found. Please install Visual Studio Build Tools.'
}

$csc = Find-Csc
$fw  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

$refNames = @('System.dll', 'System.Core.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll',
              'System.Net.Http.dll', 'System.Runtime.Serialization.dll')
$refs = @()
foreach ($n in $refNames) {
    $p = Join-Path $fw $n
    if (-not (Test-Path $p)) { throw ('Missing reference assembly: ' + $p) }
    $refs += ('/r:' + $p)
}

$sources = Get-ChildItem $srcDir -Recurse -Filter *.cs | Sort-Object FullName | ForEach-Object { $_.FullName }
if (-not $sources) { throw ('No .cs files found under: ' + $srcDir) }

# --- icon: build a tiny generator from src\AppIcon.cs, emit app.ico, embed it into the exe ---
$buildDir  = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$iconGenExe = Join-Path $buildDir 'IconGen.exe'
$iconPath   = Join-Path $buildDir 'app.ico'
$iconArgs = @('/nologo', '/target:exe', '/langversion:latest', ('/out:' + $iconGenExe)) + $refs +
            @((Join-Path $srcDir 'AppIcon.cs'), (Join-Path $root 'tools\IconGen.cs'))
& $csc @iconArgs
if ($LASTEXITCODE -ne 0) { throw 'Icon generator compile failed, exit code ' + $LASTEXITCODE }
Remove-Item $iconPath -Force -ErrorAction SilentlyContinue
& $iconGenExe $iconPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $iconPath)) { throw 'Icon generation failed' }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exeName = [char]0x624B + [char]0x673A + [char]0x4E92 + [char]0x8054 + '.exe'   # "shou ji hu lian"
$exePath = Join-Path $dist $exeName

Write-Host ('Compiler : ' + $csc)
Write-Host ('Sources  : ' + $sources.Count)
Write-Host ('Output   : ' + $exePath)

$cscArgs = @('/nologo', '/target:winexe', '/langversion:latest', '/optimize+', '/platform:anycpu', ('/out:' + $exePath), ('/win32icon:' + $iconPath)) + $refs + $sources
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw ('Compile failed, exit code ' + $LASTEXITCODE) }

$kernelSrc = Join-Path $root 'tools\scrcpy-win64-v5.0.1'
$kernelDst = Join-Path $dist 'tools\scrcpy'
if (Test-Path $kernelSrc) {
    if (Test-Path $kernelDst) { Remove-Item $kernelDst -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $kernelDst | Out-Null
    Copy-Item (Join-Path $kernelSrc '*') $kernelDst -Recurse -Force
    Remove-Item (Join-Path $kernelDst 'open_a_terminal_here.bat') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $kernelDst 'scrcpy-noconsole.vbs') -Force -ErrorAction SilentlyContinue
    Write-Host ('Kernel   : copied to ' + $kernelDst)
} else {
    Write-Host ('WARN     : kernel folder not found: ' + $kernelSrc)
}

Write-Host 'Build finished.'
