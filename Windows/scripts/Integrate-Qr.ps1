# 下载并校验二维码依赖库 QRCoder / ZXing.Net 到 libs\（幂等，校验失败报错退出）
# 使用 -Force 强制重新下载
param(
    [switch]$Force
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$PSScriptRootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $PSScriptRootDir
$LibsDir = Join-Path $ProjectDir "libs"
$LicDir = Join-Path $ProjectDir "licenses"
New-Item -ItemType Directory -Force -Path $LibsDir | Out-Null
New-Item -ItemType Directory -Force -Path $LicDir | Out-Null

$HashFile = Join-Path $LibsDir "hash.sha256"

# 库定义：nupkg 地址、dll 候选路径（按优先级）、许可证来源（nupkg 内优先，仓库 raw 兜底）
$packages = @(
    @{
        Id = "QRCoder"; Version = "1.4.3"; OutDll = "QRCoder.dll"
        Url = "https://www.nuget.org/api/v2/package/QRCoder/1.4.3"
        DllPaths = @("lib/net40/QRCoder.dll")
        LicenseNames = @("LICENSE.txt", "LICENSE", "License.txt")
        LicenseUrls = @(
            "https://raw.githubusercontent.com/codebude/QRCoder/v1.4.3/LICENSE.txt",
            "https://raw.githubusercontent.com/codebude/QRCoder/master/LICENSE.txt"
        )
        LicenseOut = "QRCoder.LICENSE.txt"
    },
    @{
        Id = "ZXing.Net"; Version = "0.16.9"; OutDll = "ZXing.dll"
        Url = "https://www.nuget.org/api/v2/package/ZXing.Net/0.16.9"
        DllPaths = @("lib/net48/ZXing.dll", "lib/net47/ZXing.dll", "lib/net461/ZXing.dll", "lib/net45/ZXing.dll", "lib/net40/ZXing.dll")
        LicenseNames = @("COPYING", "COPYING.txt", "LICENSE.txt", "LICENSE")
        LicenseUrls = @(
            "https://raw.githubusercontent.com/micjahn/ZXing.Net/v0.16.9.0/COPYING",
            "https://raw.githubusercontent.com/micjahn/ZXing.Net/master/COPYING"
        )
        LicenseOut = "ZXing.Net.LICENSE.txt"
    }
)

# GitHub raw 镜像回退（国内网络环境）
$GhMirrors = @("", "https://mirror.ghproxy.com/", "https://ghproxy.net/", "https://gh-proxy.com/")

function Invoke-Download([string]$url, [string]$outFile) {
    Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing -TimeoutSec 120
}

function Invoke-DownloadWithMirror([string]$url, [string]$outFile) {
    $lastErr = $null
    foreach ($m in $GhMirrors) {
        $u = $m + $url
        try {
            Write-Host "尝试: $u"
            Invoke-Download $u $outFile
            return
        } catch {
            $lastErr = $_.Exception.Message
            Write-Host "失败: $lastErr"
        }
    }
    throw "所有下载源均失败: $lastErr"
}

# 读取 hash.sha256 → @{ 文件名 = 哈希 }
function Read-HashFile {
    $map = @{}
    if (Test-Path $HashFile) {
        foreach ($line in (Get-Content $HashFile)) {
            if ($line -match "^([0-9a-f]{64})\s+(.+)$") {
                $map[$Matches[2].Trim()] = $Matches[1]
            }
        }
    }
    return $map
}

function Write-HashFile($map) {
    $lines = @()
    foreach ($k in ($map.Keys | Sort-Object)) {
        $lines += ($map[$k] + "  " + $k)
    }
    Set-Content -Path $HashFile -Value $lines -Encoding ASCII
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($pkg in $packages) {
    $dllOut = Join-Path $LibsDir $pkg.OutDll
    $hashes = Read-HashFile

    # 幂等：dll 存在且哈希与记录一致则跳过；不一致则报错退出
    if ((Test-Path $dllOut) -and $hashes.Contains($pkg.OutDll) -and -not $Force) {
        $actual = (Get-FileHash -Path $dllOut -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $hashes[$pkg.OutDll]) {
            throw "libs\$($pkg.OutDll) 哈希校验失败！期望 $($hashes[$pkg.OutDll]) 实际 $actual（可用 -Force 重新下载）"
        }
        Write-Host "$($pkg.Id) $($pkg.Version) 已存在且哈希校验通过，跳过下载"
        continue
    }

    Write-Host "===== 集成 $($pkg.Id) $($pkg.Version) ====="
    $tmpDir = Join-Path $env:TEMP ("yuntipro-qr-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
    try {
        # 1. 下载 nupkg
        $nupkg = Join-Path $tmpDir ($pkg.Id + ".nupkg")
        Write-Host "下载 $($pkg.Url) ..."
        Invoke-Download $pkg.Url $nupkg

        # 2. 解压 nupkg（zip），按候选路径取 dll
        $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
        $dllEntry = $null
        $licEntry = $null
        try {
            foreach ($p in $pkg.DllPaths) {
                $dllEntry = $zip.Entries | Where-Object { $_.FullName -ieq $p } | Select-Object -First 1
                if ($dllEntry) { break }
            }
            if (-not $dllEntry) {
                $avail = ($zip.Entries | Where-Object { $_.FullName -like "lib/*" -and $_.Name -ieq $pkg.OutDll } | ForEach-Object { $_.FullName }) -join ", "
                throw "nupkg 内未找到可用 dll，候选: $($pkg.DllPaths -join ', ')；实际: $avail"
            }
            Write-Host "nupkg 内命中: $($dllEntry.FullName)"
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($dllEntry, $dllOut, $true)

            # 3. 许可证：优先取 nupkg 内的许可文件
            foreach ($ln in $pkg.LicenseNames) {
                $licEntry = $zip.Entries | Where-Object { $_.Name -ieq $ln } | Select-Object -First 1
                if ($licEntry) { break }
            }
            if ($licEntry) {
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($licEntry, (Join-Path $LicDir $pkg.LicenseOut), $true)
                Write-Host "许可证取自 nupkg: $($licEntry.FullName) -> licenses\$($pkg.LicenseOut)"
            }
        } finally {
            $zip.Dispose()
        }

        # 4. 许可证兜底：从官方仓库 raw 下载（带镜像回退）
        $licOut = Join-Path $LicDir $pkg.LicenseOut
        if (-not (Test-Path $licOut)) {
            $licTmp = Join-Path $tmpDir "license.txt"
            $ok = $false
            foreach ($lu in $pkg.LicenseUrls) {
                try {
                    Invoke-DownloadWithMirror $lu $licTmp
                    $ok = $true
                    break
                } catch {
                    Write-Host "许可证下载失败: $($_.Exception.Message)"
                }
            }
            if (-not $ok) { throw "无法获取 $($pkg.Id) 许可证文本（nupkg 与仓库均失败）" }
            Copy-Item $licTmp $licOut -Force
            Write-Host "许可证取自仓库 -> licenses\$($pkg.LicenseOut)"
        }

        # 5. 计算 dll SHA-256 并写入 hash.sha256（作为后续校验的期望值）
        $hash = (Get-FileHash -Path $dllOut -Algorithm SHA256).Hash.ToLowerInvariant()
        $hashes = Read-HashFile
        $hashes[$pkg.OutDll] = $hash
        Write-HashFile $hashes
        Write-Host "libs\$($pkg.OutDll) SHA-256: $hash"

        # 6. 回读校验：写盘结果必须可复算一致
        $verify = (Get-FileHash -Path $dllOut -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($verify -ne $hash) { throw "libs\$($pkg.OutDll) 写入后哈希复核失败" }
        Write-Host "$($pkg.Id) $($pkg.Version) 集成完成"
    } finally {
        Remove-Item -Recurse -Force $tmpDir -ErrorAction SilentlyContinue
    }
}

Write-Host "===== 二维码依赖库就绪 ====="
Get-Content $HashFile | ForEach-Object { Write-Host $_ }
