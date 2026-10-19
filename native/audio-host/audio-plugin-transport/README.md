# Native audio plugin PCM transport preview

This isolated native library implements a bounded shared-memory block exchange. It has no VST3 dependency, plugin loader, decoder, device output, delay line, or production host attachment. The ECHO application still advertises `audioTransport: unavailable`.

The SDK ABI lives in [audio-plugin-pcm.h](../../../docs/workshop-sdk/contracts/audio-plugin-pcm.h), with its [machine-readable contract](../../../docs/workshop-sdk/contracts/audio-plugin-pcm.json). Four slots contain a maximum of 512 frames × 8 channels each, in dry and wet Float32 interleaved buffers. The logical region is 131392 bytes; OS backing may be rounded to pages (147456 bytes on the tested macOS system with 16 KiB pages). No audio buffer is allocated in the exchange methods.

`SharedPcmMapping` performs OS work outside realtime callbacks. Windows uses a session-local mapping and a protected DACL for the creator's user SID. POSIX uses an exclusive `shm_open` object with mode 0600, fixed size and owner unlink. Format is immutable for the lifetime of a mapping. Real sessions must use a new unpredictable name for every attachment. Names and PCM remain within authorized native processes.

`HostBlockExchange::submit/receive/cancel` and `AdapterBlockExchange::acquire/complete` use bounded scans and platform atomics. A cancelled processing slot is never reclaimed while the worker holds it. Late results cannot match a new generation or sequence. A complete output block is checked for finite samples before copying; zero-valued plugin output remains valid. Full capacity and pending results return immediately. Never destroy/reset/reinitialize a mapping while another owner still accesses it.

The Windows synchronization choice follows Microsoft's [Interlocked shared-memory guidance](https://learn.microsoft.com/en-us/windows/win32/sync/interlocked-variable-access). The Clang/GCC path requires lock-free 32-bit hardware atomics and is guarded at compile time. An actual two-process probe is still required on each target OS/architecture before claiming that platform verified.

If an adapter crashes with a slot in `Processing`, the slot remains occupied. Stop using that mapping and establish a fresh one on a non-realtime thread after native insertion has been revoked. Recovery must not steal the abandoned slot in a still-live shared region. Each mapping has exactly one serialized host owner and one serialized adapter worker.

## Build and focused probe

From the repository root, using a scratch build directory:

```sh
cmake -S native/audio-host/audio-plugin-transport -B misc/audio-plugin-native-build
cmake --build misc/audio-plugin-native-build --parallel 2
ctest --test-dir misc/audio-plugin-native-build --output-on-failure
```

On Windows with a multi-configuration generator, add `--config Release` to the build and `-C Release` to CTest. The test executable creates two native processes, opens the same mapping separately, exchanges 32 blocks with a synthetic 0.5 gain, checks exact generation/sequence/position/frame metadata and samples, and waits for child exit. Other focused cases exercise wrong tickets, cancellation while processing, duplicate completion after reuse, capacity, actual partial blocks, malformed mapping ABI, nonfinite output and legitimate silence. This is a short correctness probe, not a latency benchmark or a VST3/real-device smoke.

## Integration boundary

This block mailbox does not implement a playback insert. Device callback code must neither wait for wet output nor mute ECHO while an external process opens its own device. A production insert still needs aligned dry/wet scheduling, measured total latency, latency compensation, seeks/device changes invalidating outstanding tickets, plugin tails, native EOF/drain accounting, bounded crash recovery and a real VST3 adapter. No live ECHO capability should claim processing readiness before those pieces are connected and validated.
