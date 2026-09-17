# Audio transport

`AudioTransport` queues load, play, pause and seek operations on one worker. The UI reads its immutable `State` snapshot; it does not call the decoder or output device. `LoadAsync` and `WaitForCommandsAsync` allow callers to await applied operations. `CanPlay` stays true while a loaded device is paused.

The output uses event-driven shared-mode `WasapiOut` with the system default device and 80 ms requested latency. MP3 decoding uses Windows Media Foundation; OGG Vorbis uses NVorbis; WAV uses NAudio's WAV reader. All streams are converted to 16-bit PCM before output. This version accepts mono and stereo audio.

MP3 loading continuously decodes into a PCM cache before reporting ready. The cache uses 64 KiB chunks, a 512 MiB decoded-data limit, and cancellation checks between reads when another load supersedes it or the transport is disposed. A five-minute 44.1 kHz stereo track needs about 50 MiB. Length and seek positions come from actual decoded PCM frames, including any padding supplied by the decoder. Seeking selects a complete frame; a fractional request rounds down by less than one sample frame. Cached PCM is released when the reader is disposed.

This is necessary because [Media Foundation's SetCurrentPosition does not guarantee an exact seek](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-imfsourcereader-setcurrentposition), while NAudio's reader reports the requested byte position before reading the returned samples. The cache establishes one continuous decoded timeline for playback, seek and duration.

While playing, `PositionMs` is the seek base plus the WASAPI device position converted through the output format's bytes per second. Source-reader position is not a playback clock because its buffered reads run ahead of the device. Pending seek requests immediately own the displayed position until applied. A seek stops the old output, waits for its playback thread, discards queued buffers, seeks the decoder and opens fresh output buffers. Playing seeks resume; paused seeks remain paused. Pause captures the consumed device position and rebuilds output at that frame; the decoder's read-ahead position does not become the resume position. Superseded queued seeks are skipped. EOF reports the duration and replay starts at zero.

The default output is event-driven shared-mode `WasapiOut`; its stop operation joins the playback thread before the session is disposed. Its `Pause()` only suspends filling buffers, so the transport uses stop/rebuild to prevent buffered data and the device clock from advancing during a pause. An ended session is rebuilt from zero. The transport still treats `PlaybackStopped` as the ownership boundary for injected or alternative players: if their callback exceeds three seconds, the session and reader are detached from the active clock and disposed only after the callback. A second timeout retains an unavailable/error state until an explicit reload. Retired resources remain allocated if an alternative driver never completes the callback; diagnostics record the session, playback state and thread-pool counters.

Timestamped hitsounds are mixed into decoded music before the 16-bit conversion and output gain. They share its sample position and WASAPI buffer; the device-free tests compare event placement across output sample rates and channels. See [Hitsounds](../../../docs/HITSOUNDS.md).

Every Media Foundation decoder in this application is created through `MediaFoundationAudioReader`. Its shared lease owns startup and shuts down the process subsystem only after all active decode operations finish. Cached readers retain PCM without retaining a Media Foundation decoder. Additional Media Foundation users in this process must share that lifetime boundary.

Fixed MIT-licensed dependencies, all providing `netstandard2.0` assemblies compatible with the application's .NET 8 target:

- `NAudio.Core`, `NAudio.WinMM`, `NAudio.Wasapi` **2.2.1**. The WinForms package is not used. References: [WasapiOut](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiOut.cs), [MediaFoundationReader](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/MediaFoundationReader.cs), [MediaFoundationApi lifecycle](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/MediaFoundation/MediaFoundationHelpers.cs).
- `NAudio.Vorbis` **1.5.0** and `NVorbis` **0.10.4**. Reference: [VorbisWaveReader](https://github.com/naudio/Vorbis/blob/v1.5.0/NAudio.Vorbis/VorbisWaveReader.cs).

Required licence texts are retained in `Audio/Licenses/` and copied to builds. Transitive versions are in the application lock file. No upstream audio implementation is copied into the application.

`FruitsAtelier.Audio.Tests` links these production audio sources to test the boundary independently of editor rendering. Device tests use real output devices with output PCM gain set to zero, without changing system/device volume or the editor's normal playback gain. Source decoding and sample comparisons still use the original data. The generated four-second WAV contains a low-amplitude 440 Hz stereo tone, but automated playback is silent. Controlled-output tests inject delayed stop callbacks and repeated failures to verify reader ownership and recovery. A generated OGG tone is included in the test fixtures and can be regenerated with FFmpeg:

```powershell
ffmpeg -hide_banner -loglevel warning -y -f lavfi -i 'sine=frequency=440:duration=4:sample_rate=44100' -af 'volume=0.03' -ac 2 -c:a libvorbis tests/FruitsAtelier.Audio.Tests/Fixtures/quiet-tone.ogg
dotnet run --project tests/FruitsAtelier.Audio.Tests -c Release
```

With external MP3 fixtures, the tests compare PCM at forward, backward, zero and end-of-file seeks against a continuous decode, including the first returned sample. They also compare the real device clock against elapsed time across playback, verify frame-aligned paused seeks, and check cancellation and recovery.

FFmpeg is a test-fixture tool only; the application does not require it. The worker samples the device clock every 10 ms while playing; snapshots are not extrapolated between samples. Requested output buffering remains 80 ms. Test commands and fixture requirements are in [Testing](../../../docs/TESTING.md).
