# 构建脚本：使用 Roslyn csc.exe 编译纯代码 WPF 应用，输出 build\YunTiPro.exe
$ErrorActionPreference = "Stop"
$PSScriptRootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $PSScriptRootDir
$SrcDir = Join-Path $ProjectDir "src"
$BuildDir = Join-Path $ProjectDir "build"
$ToolsDir = Join-Path $ProjectDir "tools"

$csc = Join-Path $ToolsDir "Microsoft.Net.Compilers.Toolset.4.8.0\tasks\net472\csc.exe"
if (-not (Test-Path $csc)) { throw "未找到 Roslyn csc.exe，请先安装 Microsoft.Net.Compilers.Toolset" }

# .NET Framework 4.8 运行时引用路径
$fx = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpf = Join-Path $fx "WPF"

# 引用列表
$refs = @(
    "mscorlib.dll","System.dll","System.Core.dll","System.Xaml.dll","System.Security.dll","System.Drawing.dll",
    "System.Web.Extensions.dll","System.Runtime.Serialization.dll","System.Net.Http.dll",
    "PresentationFramework.dll","PresentationCore.dll","WindowsBase.dll","UIAutomationClient.dll"
)

$refArgs = @()
foreach ($r in $refs) {
    $path = if ($r -in @("PresentationFramework.dll","PresentationCore.dll","WindowsBase.dll","UIAutomationClient.dll")) {
        Join-Path $wpf $r
    } else {
        Join-Path $fx $r
    }
    if (-not (Test-Path $path)) { throw "引用程序集缺失: $path" }
    $refArgs += "/reference:`"$path`""
}

# 二维码依赖库（libs\，由 Integrate-Qr.ps1 下载）
$LibsDir = Join-Path $ProjectDir "libs"
$libRefs = @("QRCoder.dll","ZXing.dll")
foreach ($l in $libRefs) {
    $libPath = Join-Path $LibsDir $l
    if (-not (Test-Path $libPath)) { throw "缺少依赖 $libPath，请先运行 scripts\Integrate-Qr.ps1" }
    $refArgs += "/reference:`"$libPath`""
}

# 源码文件
$srcFiles = Get-ChildItem -Path $SrcDir -Filter "*.cs" -Recurse | Select-Object -ExpandProperty FullName
if ($srcFiles.Count -eq 0) { throw "未找到源文件" }

New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null

# 应用图标（不存在则先生成）
$iconPath = Join-Path $ProjectDir "assets\app.ico"
if (-not (Test-Path $iconPath)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRootDir "Generate-Icon.ps1")
}
$iconArgs = @()
if (Test-Path $iconPath) { $iconArgs += "/win32icon:`"$iconPath`"" }

# 编译参数
$args = @(
    "/target:winexe",
    "/platform:x64",
    "/langversion:latest",
    "/optimize",
    "/out:`"$BuildDir\YunTiPro.exe`"",
    "/filealign:512",
    "/nologo"
) + $iconArgs + $refArgs + $srcFiles

Write-Host "编译中..."
# 直接调用 csc 以保留编译输出，便于定位错误
& $csc $args
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码: $LASTEXITCODE"
}
Write-Host "构建成功: $BuildDir\YunTiPro.exe"

# 二维码依赖库复制到 exe 旁，保证运行时可加载
foreach ($l in $libRefs) {
    Copy-Item (Join-Path $LibsDir $l) $BuildDir -Force
}
Write-Host "已同步二维码依赖库到: $BuildDir"

# 将核心复制到 exe 旁，使 build\ 目录可独立分发运行
$CoreDir = Join-Path $ProjectDir "core"
if (Test-Path (Join-Path $CoreDir "xray.exe")) {
    $destCore = Join-Path $BuildDir "core"
    New-Item -ItemType Directory -Force -Path $destCore | Out-Null
    Copy-Item (Join-Path $CoreDir "*") $destCore -Force
    Write-Host "已同步核心到: $destCore"
} else {
    Write-Host "提示: 未找到 core\xray.exe，请先运行 scripts\Integrate-Core.ps1"
}
