# 下载并校验 TUN 模式核心组件：
#   - sing-box v1.3.6（Go 1.20.6 编译，Go 1.21+ 不支持 Win7，故锁定此版本）
#   - wintun 0.14.1（官方 wintun.net，取 amd64 的 wintun.dll）
# 期望 SHA-256 固定写死在本脚本中，校验失败立即报错退出。
# 幂等：core\hash.sha256 记录已安装产物哈希，匹配则跳过；使用 -Force 强制重新下载。
param(
    [switch]$Force
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$PSScriptRootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $PSScriptRootDir
$CoreDir = Join-Path $ProjectDir "core"
$HashFile = Join-Path $CoreDir "hash.sha256"
$LicenseDir = Join-Path $ProjectDir "licenses"
New-Item -ItemType Directory -Force -Path $CoreDir | Out-Null
New-Item -ItemType Directory -Force -Path $LicenseDir | Out-Null

# ---------- 固定版本与期望哈希（预先真实下载核算后写入，勿凭空修改） ----------
$SingBoxVersion = "1.3.6"
$SingBoxZipName = "sing-box-$SingBoxVersion-windows-amd64.zip"
$SingBoxZipSha256 = "b3481cddc641f045918fe17a3adf2a4c1b06c55dc8c69a34e65d082982615888"
$SingBoxExeSha256 = "76d32160854157d260614ea950028b886f42fabf9b5198ec35dbe915895cdf0d"

$WintunVersion = "0.14.1"
$WintunZipSha256 = "07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51"
$WintunDllSha256 = "e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce"

$SingBoxUrls = @(
    "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/$SingBoxZipName",
    "https://mirror.ghproxy.com/https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/$SingBoxZipName",
    "https://ghproxy.net/https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/$SingBoxZipName",
    "https://gh-proxy.com/https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/$SingBoxZipName"
)
$WintunUrl = "https://www.wintun.net/builds/wintun-$WintunVersion.zip"

function Get-Sha256([string]$path) {
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-Download([string[]]$urls, [string]$outFile) {
    $lastErr = $null
    foreach ($url in $urls) {
        try {
            Write-Host "尝试: $url"
            Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing -TimeoutSec 120
            return
        } catch {
            $lastErr = $_.Exception.Message
            Write-Host "失败: $lastErr"
        }
    }
    throw "所有下载源均失败: $lastErr"
}

function Test-Installed {
    # 幂等检查：产物存在且与 hash.sha256 记录一致
    $sbExe = Join-Path $CoreDir "sing-box\sing-box.exe"
    $wtDll = Join-Path $CoreDir "wintun\wintun.dll"
    if (-not (Test-Path $sbExe) -or -not (Test-Path $wtDll) -or -not (Test-Path $HashFile)) { return $false }
    $record = @{}
    foreach ($line in (Get-Content $HashFile -ErrorAction SilentlyContinue)) {
        if ($line -match "^([0-9a-fA-F]{64})\s+\*?(.+)$") { $record[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant() }
    }
    return ($record["sing-box/sing-box.exe"] -eq (Get-Sha256 $sbExe) -and
            $record["wintun/wintun.dll"] -eq (Get-Sha256 $wtDll))
}

if ((Test-Installed) -and -not $Force) {
    Write-Host "core\sing-box\sing-box.exe 与 core\wintun\wintun.dll 已存在且哈希匹配，跳过下载（使用 -Force 强制更新）"
    exit 0
}

$tmpDir = Join-Path $env:TEMP ("yuntipro-tun-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

try {
    # ================= sing-box =================
    $sbZip = Join-Path $tmpDir $SingBoxZipName
    Write-Host "下载 sing-box v$SingBoxVersion ..."
    Invoke-Download $SingBoxUrls $sbZip

    $actualZipHash = Get-Sha256 $sbZip
    Write-Host "sing-box zip SHA-256: $actualZipHash"
    if ($actualZipHash -ne $SingBoxZipSha256) { throw "sing-box 压缩包 SHA-256 校验失败！期望 $SingBoxZipSha256 实际 $actualZipHash" }
    Write-Host "sing-box 压缩包校验通过"

    $sbExtract = Join-Path $tmpDir "sing-box"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($sbZip, $sbExtract)
    $sbExeSrc = Get-ChildItem -Recurse -Path $sbExtract -Filter "sing-box.exe" | Select-Object -First 1
    if (-not $sbExeSrc) { throw "压缩包内未找到 sing-box.exe" }

    $sbDestDir = Join-Path $CoreDir "sing-box"
    New-Item -ItemType Directory -Force -Path $sbDestDir | Out-Null
    $sbExeDest = Join-Path $sbDestDir "sing-box.exe"
    Copy-Item $sbExeSrc.FullName $sbExeDest -Force

    $actualExeHash = Get-Sha256 $sbExeDest
    Write-Host "sing-box.exe SHA-256: $actualExeHash"
    if ($actualExeHash -ne $SingBoxExeSha256) { throw "sing-box.exe SHA-256 校验失败！期望 $SingBoxExeSha256 实际 $actualExeHash" }

    # 核对编译 Go 版本：Go 1.21+ 不支持 Win7，必须 <= 1.20
    $verOut = & $sbExeDest version 2>&1 | Out-String
    Write-Host $verOut.Trim()
    if ($verOut -match "go1\.(\d+)") {
        $goMinor = [int]$Matches[1]
        if ($goMinor -ge 21) { throw "sing-box 由 Go 1.$goMinor 编译，不支持 Windows 7，需 Go<=1.20 版本" }
    } else {
        throw "无法从 sing-box version 输出解析 Go 版本，拒绝继续"
    }
    Write-Host "Go 版本检查通过（Win7 兼容）"

    # sing-box 许可证（zip 内自带 GPL-3.0 LICENSE）
    $sbLicense = Get-ChildItem -Recurse -Path $sbExtract -Filter "LICENSE*" | Select-Object -First 1
    if ($sbLicense) {
        Copy-Item $sbLicense.FullName (Join-Path $LicenseDir "sing-box.LICENSE.txt") -Force
        Write-Host "已保存 licenses\sing-box.LICENSE.txt"
    }

    # ================= wintun =================
    $wtZip = Join-Path $tmpDir "wintun-$WintunVersion.zip"
    Write-Host "下载 wintun $WintunVersion ..."
    Invoke-Download @($WintunUrl) $wtZip

    $actualWtZipHash = Get-Sha256 $wtZip
    Write-Host "wintun zip SHA-256: $actualWtZipHash"
    if ($actualWtZipHash -ne $WintunZipSha256) { throw "wintun 压缩包 SHA-256 校验失败！期望 $WintunZipSha256 实际 $actualWtZipHash" }
    Write-Host "wintun 压缩包校验通过"

    $wtExtract = Join-Path $tmpDir "wintun"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($wtZip, $wtExtract)
    $wtDllSrc = Get-ChildItem -Recurse -Path $wtExtract -Filter "wintun.dll" |
        Where-Object { $_.FullName -match [regex]::Escape("bin\amd64") } | Select-Object -First 1
    if (-not $wtDllSrc) { throw "压缩包内未找到 bin\amd64\wintun.dll" }

    $wtDestDir = Join-Path $CoreDir "wintun"
    New-Item -ItemType Directory -Force -Path $wtDestDir | Out-Null
    $wtDllDest = Join-Path $wtDestDir "wintun.dll"
    Copy-Item $wtDllSrc.FullName $wtDllDest -Force

    $actualDllHash = Get-Sha256 $wtDllDest
    Write-Host "wintun.dll SHA-256: $actualDllHash"
    if ($actualDllHash -ne $WintunDllSha256) { throw "wintun.dll SHA-256 校验失败！期望 $WintunDllSha256 实际 $actualDllHash" }

    # wintun 许可证（zip 内自带 LICENSE.txt）
    $wtLicense = Get-ChildItem -Recurse -Path $wtExtract -Filter "LICENSE*" | Select-Object -First 1
    if ($wtLicense) {
        Copy-Item $wtLicense.FullName (Join-Path $LicenseDir "wintun.LICENSE.txt") -Force
        Write-Host "已保存 licenses\wintun.LICENSE.txt"
    }

    # ================= 写入哈希记录 =================
    $lines = @(
        "$actualExeHash *sing-box/sing-box.exe",
        "$actualDllHash *wintun/wintun.dll"
    )
    Set-Content -Path $HashFile -Value $lines -Encoding ASCII
    Write-Host "已写入 core\hash.sha256"
    Write-Host "TUN 核心集成完成：sing-box v$SingBoxVersion + wintun $WintunVersion"
} finally {
    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
}
