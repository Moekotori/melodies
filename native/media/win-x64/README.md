# Windows bundled tools

This directory is copied to packaged Windows resources as `resources/tools`.

Expected files for a Windows package:

```text
ffmpeg.exe
avcodec-62.dll
avformat-62.dll
avutil-60.dll
swresample-6.dll
FFMPEG-LICENSE.txt
ffmpeg-manifest.json
```

`ffmpeg.exe` is required for the Windows release gate. Large binaries stay out of git, so prepare the pinned FFmpeg build from `ffmpeg-manifest.json` before packaging:

```bash
npm run prepare:win-ffmpeg
npm run verify:ffmpeg
```

The pinned BtbN LGPL shared archive also supplies the FFmpeg development root under `.electron-cache/ffmpeg/development/`. The native audio host links against that same root, so do not replace only `ffmpeg.exe` or source FFmpeg DLLs from another installation.

Pin the last build of a month: [BtbN retains those builds for two years, but only the last 14 daily builds](https://github.com/BtbN/FFmpeg-Builds#release-retention-policy). Keep the dated release URL, archive/binary SHA256 values, development root/cache key, and `THIRD_PARTY_NOTICES.md` in sync when updating. Do not use the floating `latest` release with fixed hashes.
