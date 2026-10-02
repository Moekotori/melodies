# Original PCM channel test signals

`pcm-3point0.wav`, `pcm-5point1.wav` and `pcm-7point1.wav` were authored for ECHO's
native channel regression. They contain no recording, third-party sample or
licensed music. Their format is WAVEFORMATEXTENSIBLE, IEEE float32, 48 kHz,
512 frames, with masks `0x7` (3.0), `0x60f` (5.1 side) and `0x63f` (7.1).

For zero-based frame `f` and channel `c`, each stored sample is the float32 value
of `(c + 1) * 0.01 + (f % 31) * 0.0001`. The native test compares actual decoder
output with the stored sample bytes, not a second implementation of decoding.
The distinct channel amplitudes detect routing mistakes and accidental downmix.
