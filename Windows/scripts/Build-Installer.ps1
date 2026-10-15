$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$BuildDir = Join-Path $ProjectDir "build"
$InstallerDir = Join-Path $ProjectDir "installer"
$OutputDir = Join-Path $ProjectDir "dist"
$StageDir = Join-Path $env:TEMP "YunTiPro-Installer-Stage"
$Payload = Join-Path $InstallerDir "payload.zip"
$csc = Join-Path $ProjectDir "tools\Microsoft.Net.Compilers.Toolset.4.8.0\tasks\net472\csc.exe"
$fx = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"

& (Join-Path $ProjectDir "scripts\Build.ps1")
if ($LASTEXITCODE -ne 0) { throw "Application build failed" }
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=YunTi Pro Windows" } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) { throw "Code-signing certificate not found" }
$appExe = Join-Path $BuildDir "YunTiPro.exe"
Set-AuthenticodeSignature -FilePath $appExe -Certificate $cert -HashAlgorithm SHA256 | Out-Null
if ((Get-AuthenticodeSignature $appExe).Status -ne "Valid") { throw "Application signing failed" }

Remove-Item $StageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "core") | Out-Null
Copy-Item (Join-Path $BuildDir "YunTiPro.exe") $StageDir -Force
Copy-Item (Join-Path $BuildDir "QRCoder.dll") $StageDir -Force
Copy-Item (Join-Path $BuildDir "ZXing.dll") $StageDir -Force
Copy-Item (Join-Path $ProjectDir "core\xray.exe") (Join-Path $StageDir "core") -Force
Copy-Item (Join-Path $ProjectDir "core\geoip.dat") (Join-Path $StageDir "core") -Force
Copy-Item (Join-Path $ProjectDir "core\geosite.dat") (Join-Path $StageDir "core") -Force
Copy-Item (Join-Path $ProjectDir "core\hash.sha256") (Join-Path $StageDir "core") -Force
Copy-Item (Join-Path $ProjectDir "core\SHA256.txt") (Join-Path $StageDir "core") -Force
Copy-Item (Join-Path $ProjectDir "core\sing-box") (Join-Path $StageDir "core") -Recurse -Force
Copy-Item (Join-Path $ProjectDir "core\wintun") (Join-Path $StageDir "core") -Recurse -Force

Remove-Item $Payload -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $Payload -CompressionLevel Optimal
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$output = Join-Path $OutputDir "YunTiPro-Setup-1.1.3.exe"
$refs = @(
    (Join-Path $fx "System.dll"),
    (Join-Path $fx "System.Core.dll"),
    (Join-Path $fx "System.Drawing.dll"),
    (Join-Path $fx "System.Windows.Forms.dll"),
    (Join-Path $fx "System.IO.Compression.dll"),
    (Join-Path $fx "System.IO.Compression.FileSystem.dll"),
    (Join-Path $fx "Microsoft.CSharp.dll")
)
$args = @(
    "/target:winexe", "/platform:anycpu", "/langversion:latest", "/optimize", "/nologo",
    "/out:`"$output`"", "/win32icon:`"$(Join-Path $ProjectDir 'assets\app.ico')`"",
    "/resource:`"$Payload`",payload.zip"
) + ($refs | ForEach-Object { "/reference:`"$_`"" }) + @((Join-Path $InstallerDir "Installer.cs"))
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed" }
Set-AuthenticodeSignature -FilePath $output -Certificate $cert -HashAlgorithm SHA256 | Out-Null
if ((Get-AuthenticodeSignature $output).Status -ne "Valid") { throw "Installer signing failed" }
Remove-Item $Payload -Force
Remove-Item $StageDir -Recurse -Force
Write-Host "Installer built and signed: $output"
