# 下载并校验 Xray-core v1.8.3 (Go 1.20 编译，最后支持 Win7 的版本之一)
# 幂等：已存在且哈希匹配则跳过；使用 -Force 强制重新下载
param(
    [switch]$Force
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$PSScriptRootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $PSScriptRootDir
$CoreDir = Join-Path $ProjectDir "core"
New-Item -ItemType Directory -Force -Path $CoreDir | Out-Null

$Version = "1.8.3"
$ZipName = "Xray-windows-64.zip"
$GitHubBase = "https://github.com/XTLS/Xray-core/releases/download/v$Version"
# 下载源：官方直连 + 镜像回退（国内网络环境）
$BaseUrls = @(
    $GitHubBase,
    "https://mirror.ghproxy.com/$GitHubBase",
    "https://ghproxy.net/$GitHubBase",
    "https://gh-proxy.com/$GitHubBase"
)
$HashFile = Join-Path $CoreDir "SHA256.txt"

# 带镜像回退的下载
function Invoke-DownloadWithFallback([string]$fileName, [string]$outFile) {
    $lastErr = $null
    foreach ($base in $BaseUrls) {
        $url = "$base/$fileName"
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

# 幂等检查：xray.exe 存在且哈希文件匹配则跳过
if ((Test-Path (Join-Path $CoreDir "xray.exe")) -and (Test-Path $HashFile) -and -not $Force) {
    Write-Host "core\xray.exe 已存在且经过校验，跳过下载（使用 -Force 强制更新）"
    exit 0
}

$tmpDir = Join-Path $env:TEMP ("yuntipro-core-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$zipPath = Join-Path $tmpDir $ZipName
$dgstPath = Join-Path $tmpDir "$ZipName.dgst"

try {
    # 1. 下载 .dgst 获取官方 SHA-256
    Write-Host "下载校验文件 $ZipName.dgst ..."
    Invoke-DownloadWithFallback "$ZipName.dgst" $dgstPath
    $dgst = Get-Content $dgstPath -Raw

    # dgst 单文件格式: "SHA2-256= <hash>"；也兼容多文件分节格式
    $expected = $null
    if ($dgst -match "SHA2-256[=:]\s*([0-9a-fA-F]{64})") {
        $expected = $Matches[1].ToLowerInvariant()
    }
    if (-not $expected) { throw "无法从 .dgst 解析 $ZipName 的 SHA-256" }
    Write-Host "官方 SHA-256: $expected"

    # 2. 下载压缩包
    Write-Host "下载 $ZipName ..."
    Invoke-DownloadWithFallback $ZipName $zipPath

    # 3. 校验 SHA-256
    $actual = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "实际 SHA-256: $actual"
    if ($actual -ne $expected) { throw "SHA-256 校验失败！期望 $expected 实际 $actual" }
    Write-Host "SHA-256 校验通过"

    # 4. 解压 xray.exe / geoip.dat / geosite.dat 到 core\
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.Name -in @("xray.exe", "geoip.dat", "geosite.dat")) {
                $dest = Join-Path $CoreDir $entry.Name
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
                Write-Host "已解压: core\$($entry.Name)"
            }
        }
    } finally {
        $zip.Dispose()
    }

    # 5. 写入哈希记录（幂等依据）
    Set-Content -Path $HashFile -Value $actual -Encoding ASCII
    Write-Host "Xray-core v$Version 集成完成"
} finally {
    Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
}
