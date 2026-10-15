# 编译并运行控制台测试，失败时返回非零退出码
$ErrorActionPreference = "Stop"
$PSScriptRootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $PSScriptRootDir
$ToolsDir = Join-Path $ProjectDir "tools"
$csc = Join-Path $ToolsDir "Microsoft.Net.Compilers.Toolset.4.8.0\tasks\net472\csc.exe"
if (-not (Test-Path $csc)) { throw "未找到 Roslyn csc.exe" }

$fx = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$refs = @(
    "mscorlib.dll","System.dll","System.Core.dll","System.Security.dll","System.Web.Extensions.dll","System.Drawing.dll"
) | ForEach-Object { Join-Path $fx $_ }
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "引用缺失: $r" } }
$refArgs = $refs | ForEach-Object { "/reference:`"$_`"" }

# 二维码依赖库（libs\，由 Integrate-Qr.ps1 下载）
$LibsDir = Join-Path $ProjectDir "libs"
$libRefs = @("QRCoder.dll","ZXing.dll")
foreach ($l in $libRefs) {
    $libPath = Join-Path $LibsDir $l
    if (-not (Test-Path $libPath)) { throw "缺少依赖 $libPath，请先运行 scripts\Integrate-Qr.ps1" }
    $refArgs += "/reference:`"$libPath`""
}

# 被测源码（不含 WPF UI 与入口）
$shared = @(
    "Models.cs","ShareLinkParser.cs","ShareLinkExporter.cs","QrCodeHelper.cs","XrayConfigBuilder.cs","CredentialStore.cs","NetworkTools.cs","SystemProxyManager.cs","AppLogger.cs","XrayCoreManager.cs",
    "SingBoxConfigBuilder.cs","TunCoreManager.cs","InstalledApps.cs","AppServices.cs"
) | ForEach-Object { Join-Path $ProjectDir ("src\" + $_) }
$testSrc = Join-Path $ProjectDir "tests\TestRunner.cs"

$outExe = Join-Path $ProjectDir "build\YunTiPro.Tests.exe"
New-Item -ItemType Directory -Force -Path (Join-Path $ProjectDir "build") | Out-Null

$cscArgs = @("/target:exe","/platform:x64","/langversion:latest","/out:`"$outExe`"","/nologo") + $refArgs + $shared + $testSrc

Write-Host "编译测试程序..."
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "测试编译失败" }

# 依赖库复制到测试程序旁，保证运行时可加载
foreach ($l in $libRefs) {
    Copy-Item (Join-Path $LibsDir $l) (Join-Path $ProjectDir "build") -Force
}

Write-Host "运行测试..."
& $outExe
$code = $LASTEXITCODE
if ($code -ne 0) { Write-Host "测试失败，退出码 $code"; exit $code }
Write-Host "全部测试通过"
exit 0
