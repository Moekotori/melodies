param([string]$Configuration = 'Release', [string]$FfmpegRoot = $env:ECHO_FFMPEG_ROOT)
$ErrorActionPreference = 'Stop'
$echoRoot = Split-Path -Parent $PSScriptRoot
$echoRid = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
# CMake rejects generated OUTPUT paths containing '#'; this workspace name contains it.
$echoWorkspaceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($echoRoot))).Substring(0,12)
$echoNativeBuild = Join-Path ([IO.Path]::GetTempPath()) ("echo-scanner-" + $echoWorkspaceHash + "-" + $echoRid)
$echoNativeOutput = Join-Path $echoRoot ('artifacts/native/scanner/' + $echoRid)
$echoNativeSource = Join-Path $echoRoot 'native/vendor/echo-library-scanner'
New-Item -ItemType Directory -Path $echoNativeOutput -Force | Out-Null
$echoConfigureArgs = @()
if ($FfmpegRoot) {
    $echoFfmpegSdk = (Resolve-Path -LiteralPath $FfmpegRoot).Path
    if (-not (Test-Path -LiteralPath (Join-Path $echoFfmpegSdk 'include/libavformat/avformat.h'))) { throw 'FFmpeg development headers are missing.' }
    $echoConfigureArgs += "-DECHO_FFMPEG_ROOT=$echoFfmpegSdk"
}
if ($IsWindows) {
    & cmake -S $echoNativeSource -B $echoNativeBuild -G 'Visual Studio 17 2022' -A x64 @echoConfigureArgs
} else {
    & cmake -S $echoNativeSource -B $echoNativeBuild -DCMAKE_BUILD_TYPE=$Configuration @echoConfigureArgs
}
if ($LASTEXITCODE -ne 0) { throw 'Native scanner configuration failed.' }
& cmake --build $echoNativeBuild --config $Configuration --parallel 2
if ($LASTEXITCODE -ne 0) { throw 'Native scanner build failed.' }
$echoNativeName = if ($IsWindows) { 'echo-native-scanner.exe' } else { 'echo-native-scanner' }
$echoCandidates = @((Join-Path $echoNativeBuild "$Configuration/$echoNativeName"), (Join-Path $echoNativeBuild $echoNativeName))
$echoBuiltScanner = $echoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $echoBuiltScanner) { throw 'Native scanner artifact missing.' }
Copy-Item -LiteralPath $echoBuiltScanner -Destination (Join-Path $echoNativeOutput $echoNativeName)
if ($IsWindows -and $FfmpegRoot) {
    Get-ChildItem -LiteralPath (Join-Path $echoFfmpegSdk 'bin') -Filter '*.dll' | Copy-Item -Destination $echoNativeOutput
}
Write-Output (Join-Path $echoNativeOutput $echoNativeName)
