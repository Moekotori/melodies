The `seek-vorbis.ogg` and `seek-tta.tta` files are original synthetic test
signals, with no third-party recordings or samples. They were generated with
the repository's pinned FFmpeg 8.1 LGPL toolchain, without modifying FFmpeg.
No additional third-party asset license or notice is required for these signals.

Generate each from a three-second, 997 Hz sine wave at 48 kHz, duplicated to
stereo. Use the following commands with the repository's FFmpeg executable:

```sh
ffmpeg -f lavfi -i sine=frequency=997:sample_rate=48000:duration=3 -ac 2 -c:a libvorbis seek-vorbis.ogg
ffmpeg -f lavfi -i sine=frequency=997:sample_rate=48000:duration=3 -ac 2 -c:a tta seek-tta.tta
```

The fixtures expose backward seeks landing at an earlier Ogg page or TTA block.
The focused `echo-libav-seek-tests` target calls the production streaming decoder
and checks the remaining PCM length and source position, repeated backward seeks,
resampling, cancellation, bounded chunks, and reopening after a pending seek.
