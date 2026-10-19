#!/usr/bin/env bash
# Development build: FFmpeg development libraries must be installed on this machine.
# Release redistribution/signing of FFmpeg and its dependencies is a separate step.
set -euo pipefail
workspace="$(cd "$(dirname "$0")/.." && pwd)"
case "$(uname -s)" in
  Darwin) platform=osx ;;
  Linux) platform=linux ;;
  *) echo 'Use build-native-audio.ps1 on Windows.' >&2; exit 1 ;;
esac
case "$(uname -m)" in
  arm64|aarch64) arch=arm64 ;;
  x86_64) arch=x64 ;;
  *) echo 'Unsupported architecture.' >&2; exit 1 ;;
esac
build="$workspace/misc/native-build/$platform-$arch"
runtime="$workspace/native/runtime/$platform-$arch"
options=(-DCMAKE_BUILD_TYPE=Release -DECHO_ENABLE_CUDA_DSP=OFF)
if [[ -n "${ECHO_FFMPEG_ROOT:-}" ]]; then
  options+=("-DECHO_FFMPEG_ROOT=$ECHO_FFMPEG_ROOT")
fi
python3 "$workspace/scripts/sync-audio-engine.py" --check
cmake -S "$workspace/native/audio-host" -B "$build" -G Ninja "${options[@]}"
cmake --build "$build" --target echo-audio-host echo-track-analysis --parallel "${ECHO_BUILD_JOBS:-4}"
mkdir -p "$runtime"
cp "$build/echo-audio-host" "$build/echo-track-analysis" "$runtime/"
cp "$build/native-identity/Release/receipt-echo-audio-host.json" "$runtime/echo-audio-host.build.json"
echo "Native development host staged in $runtime. Rebuild Echo.Desktop to copy it."
