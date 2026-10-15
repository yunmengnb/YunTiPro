param([switch]$WithNative)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (-not $env:ANDROID_HOME) { $env:ANDROID_HOME=Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
if (-not (Test-Path (Join-Path $env:ANDROID_HOME 'platforms\android-35\android.jar'))) { throw 'Install Android SDK platform 35 and configure ANDROID_HOME.' }
if ($WithNative) { & (Join-Path $PSScriptRoot 'Integrate-Native.ps1') }
Push-Location $root
try {
    & (Join-Path $root 'gradlew.bat') :app:assembleDebug :app:testDebugUnitTest :app:lintDebug --console=plain
    if ($LASTEXITCODE -ne 0) { throw 'Build or tests failed.' }
} finally { Pop-Location }
