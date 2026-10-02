param([Parameter(Mandatory=$true)][string]$FfmpegRoot, [switch]$CpuOnly)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$ffmpeg = (Resolve-Path -LiteralPath $FfmpegRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $ffmpeg 'include/libavcodec/avcodec.h'))) { throw 'FFmpeg development headers are required.' }
$source = Join-Path $workspace 'native/audio-host'
$build = Join-Path $workspace 'native/build/audio-host'
$cuda = if ($CpuOnly) { 'OFF' } else { 'ON' }
& cmake -S $source -B $build -G 'Visual Studio 17 2022' -A x64 "-DECHO_FFMPEG_ROOT=$ffmpeg" "-DECHO_ENABLE_CUDA_DSP=$cuda"
if ($LASTEXITCODE -ne 0) { throw 'Native CMake configure failed.' }
& cmake --build $build --config Release --target echo-audio-host --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Native C++ build failed.' }
$runtime = Join-Path $workspace 'native/runtime/win-x64'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'Release/echo-audio-host.exe') -Destination $runtime
Get-ChildItem -LiteralPath (Join-Path $build 'Release') -Filter '*.dll' | Copy-Item -Destination $runtime
Write-Host 'Native C++ audio host staged. Rebuild Echo.Desktop to copy the runtime.'
