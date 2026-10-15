# Generate local-only release credentials without exposing passwords in arguments or output.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root 'signing'
$store = Join-Path $directory 'yunti-pro-release.p12'
$config = Join-Path $directory 'release.properties'
if ((Test-Path $store) -and (Test-Path $config)) {
    Write-Output 'Existing signing files retained; nothing overwritten.'
    return
}
if ((Test-Path $store) -or (Test-Path $config)) { throw 'Incomplete existing signing configuration; refusing to overwrite.' }
$keytool = Join-Path $env:JAVA_HOME 'bin\keytool.exe'
if (-not (Test-Path $keytool)) { throw 'JAVA_HOME must point to a JDK containing keytool.' }
[void][IO.Directory]::CreateDirectory($directory)
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $directory -AclObject $acl
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 48
try {
    $rng.GetBytes($bytes)
    $password = [Convert]::ToBase64String($bytes)
    $env:YUNTI_RELEASE_PASSWORD = $password
    # Persist first so a failed keytool operation cannot lose a generated key's password.
    $text = "# Local release credentials - never commit or share.`nstoreFile=signing/yunti-pro-release.p12`nstorePassword=$password`nkeyAlias=yunti-pro-release`nkeyPassword=$password`n"
    $stream = [IO.File]::Open($config, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $data = [Text.Encoding]::ASCII.GetBytes($text)
        $stream.Write($data, 0, $data.Length)
    } finally { $stream.Dispose() }
    & $keytool -genkeypair -keystore $store -storetype PKCS12 -alias yunti-pro-release -keyalg RSA -keysize 3072 -sigalg SHA256withRSA -validity 36500 -dname 'CN=Yunti Pro, OU=Android, O=Yimengyun, C=CN' -storepass:env YUNTI_RELEASE_PASSWORD -keypass:env YUNTI_RELEASE_PASSWORD -noprompt
    if ($LASTEXITCODE -ne 0) { throw 'Key generation failed; local credentials retained for recovery.' }
    Write-Output 'Persistent release keystore created with private local credentials.'
} finally {
    $env:YUNTI_RELEASE_PASSWORD = $null
    $password = $null
    $text = $null
    if ($data) { [Array]::Clear($data, 0, $data.Length) }
    [Array]::Clear($bytes, 0, $bytes.Length)
    $rng.Dispose()
}
