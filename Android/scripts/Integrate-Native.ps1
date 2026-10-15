$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$libs = Join-Path $root 'app\libs'
New-Item -ItemType Directory -Force $libs | Out-Null
function Download([string]$url, [string]$dest) {
    & curl.exe --fail --location --header "Accept: application/octet-stream" --retry 1 --connect-timeout 20 --max-time 240 --output $dest $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
}
$items = @(
    @{ Name='libv2ray.aar'; Url='https://api.github.com/repos/2dust/AndroidLibXrayLite/releases/assets/629683560'; Hash='15db760bfbc24c0492ee997784b48a7b5e1bc5032950d617cd252fc2195f7ac7' },
    @{ Name='hev-socks5-tunnel.aar'; Url='https://api.github.com/repos/heiher/hev-socks5-tunnel/releases/assets/592194996'; Hash='15ec8ed121663b562c99caa5bb602d1009f24e5b09e733438b81988f12feaaab' }
)
foreach ($item in $items) {
    $dest = Join-Path $libs $item.Name
    if ((Test-Path $dest) -and (Get-FileHash $dest -Algorithm SHA256).Hash.ToLower() -eq $item.Hash) { continue }
    $temp = "$dest.download"
    if (-not ((Test-Path $temp) -and (Get-FileHash $temp -Algorithm SHA256).Hash.ToLower() -eq $item.Hash)) { Download $item.Url $temp }
    if ((Get-FileHash $temp -Algorithm SHA256).Hash.ToLower() -ne $item.Hash) { throw "SHA256 mismatch: $($item.Name)" }
    Move-Item -Force $temp $dest
    Write-Host "Verified $($item.Name)"
}
$licenses = Join-Path $root 'app\src\main\assets\licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
Download 'https://raw.githubusercontent.com/2dust/AndroidLibXrayLite/v26.10.10/LICENSE' (Join-Path $licenses 'AndroidLibXrayLite.txt')
Download 'https://raw.githubusercontent.com/heiher/hev-socks5-tunnel/2.18.0/LICENSE' (Join-Path $licenses 'hev.txt')
Download 'https://raw.githubusercontent.com/XTLS/Xray-core/701af60772cd/LICENSE' (Join-Path $licenses 'Xray.txt')
Write-Host 'Native artifacts installed. Rebuild APK. Keep corresponding upstream source available when distributing.'
