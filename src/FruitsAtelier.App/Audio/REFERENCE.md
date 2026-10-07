# Audio transport

Windows release builds expose opt-in recording, output profiles, frame logs,
issue markers and background report export in **Settings > Audio**. Apply saves
pending project edits and restarts automatically when diagnostic options change,
returning to the same Audio settings position with the project, difficulty and
paused playhead restored. Each run gets a capture folder,
with a 64 MiB event-data budget shared by its loggers. Logger shutdown waits at
most 250 ms. editor.log also writes through a bounded background queue, so slow
filesystem or endpoint queries in diagnostic writers cannot hold transport
replacement indefinitely. See the [capture instructions](../../../docs/AUDIO-DIAGNOSTICS.txt).

Windows audio diagnostics are also opt-in through `audio-diagnostics.enabled` beside
the application binary or `FRUITSATELIER_AUDIO_DIAGNOSTICS=1`. Each transport and
hitsound bank writes its own timestamped JSONL file using a bounded background
queue, with a 16 MiB file limit. Logs include default endpoint metadata, command
queue and execution timing, output session lifecycle, source/device/published
positions, UI presentation, and hitsound decoder formats and failures. Clock and
playing UI samples are limited to four per second; state transitions remain
event-driven. Diagnostic reads do not change playback position. See the
[capture instructions](../../../docs/AUDIO-DIAGNOSTICS.txt) for collection.

Each output session records its first three source reads and the first observed
nonzero device position. Periodic snapshots include supplied PCM duration, read
counts, short reads, maximum read duration and interval. UI samples include the
maximum update gap since the previous logged sample. Source duration describes
data supplied to the output, not audible playback; short reads may indicate EOF.
First device progress is sampled by the worker and cannot measure acoustic latency.

At non-default speeds, snapshots also report source-read and tempo-processing
durations separately, input/output frame totals, source EOF, and the last output
RMS and longest near-silent run before gain and hitsound mixing. Silence may be
present in the source or EOF flush; these counters alone do not prove an underrun.
The measurements are enabled only for diagnostic capture.

At playback speeds of 25% and below, SoundTouch uses quick seek, a 30 ms sequence
and 4 ms overlap to reduce choppy low-speed playback. Higher speeds retain the
library defaults. These parameters reference `osu.Framework/Audio/Track/TrackBass.cs`
at osu!framework commit `94724b4385479b2e00bb347c9201ce9d9d13f594`; BASS is not
bundled. Output requests a 10 ms shared-mode WASAPI buffer. Runtime
PCM recording and tempo overrides are not provided. Diagnostic capture accepts
`FRUITSATELIER_AUDIO_PROFILE`: `event-10` (default), `event-50`, `poll-10`, or
`poll-50`. These select event-driven or polling shared-mode WASAPI and a requested
10 or 50 ms buffer. Overrides are ignored without diagnostics. The driver determines
actual buffering; the mixed read-through/device-clock difference is logged separately.
`FRUITSATELIER_AUDIO_LOG_DIRECTORY` directs both audio logs and `editor.log` to a
per-run capture folder while diagnostics are enabled. Diagnostic packages include
launchers that create this folder, check for logs and ZIP it after exit.
Logger startup and write failures are reported in `editor.log` when writable.
Decode duration, MP3 leading-frame correction, output initialization duration,
clock gaps/stalls, slow/short source reads, gains and hitsound scheduling/late/drop
counts supplement the existing lifecycle and tempo counters. Anomalies are rate
limited; audio callback threads enqueue bounded records and never write files.
Source identity includes SHA-256, computed on the load worker after decode.
`FRUITSATELIER_DISPLAY_PROFILE=vsync` or `immediate` enables diagnostic frame logs
and a visible frame/QPC/map-time overlay while audio diagnostics are requested.
The immediate mode uses frame readiness and nonblocking interval-zero submission
for editing as well as testplay. Normal rendering is unchanged without this option.
Logs include render/submit timestamps, audio snapshot age and DXGI frame statistics.
The reported frame age is measured at query time; SyncQPCTime is a synchronization
sample, not independently a physical display latency measurement. See
[DXGI frame statistics](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_frame_statistics).

`AudioTransport` queues load, play, pause, seek and speed operations on one worker. The UI reads its immutable `State` snapshot; it does not call the decoder or output device. `LoadAsync` and `WaitForCommandsAsync` allow callers to await applied operations. `CanPlay` stays true while a loaded device is paused.

Testplay can seek to a negative map position. The decoder remains at source frame
zero while a bounded streaming provider supplies silence after tempo processing
for the remaining preparation time divided by playback speed. Device position,
PCM read-through and hitsound scheduling share the negative origin. Crossing zero
continues through the same output session. Pause, seek and speed changes rebuild
the remaining silence from the consumed map position.

The output uses event-driven shared-mode `WasapiOut` with the system default device and 10 ms requested latency. MP3 decoding uses Windows Media Foundation; OGG Vorbis uses NVorbis; WAV uses NAudio's WAV reader. All streams are converted to 16-bit PCM before output. This version accepts mono and stereo audio.

MP3 loading continuously decodes into a PCM cache before reporting ready. The cache uses 64 KiB chunks, a 512 MiB decoded-data limit, and cancellation checks between reads when another load supersedes it or the transport is disposed. A five-minute 44.1 kHz stereo track needs about 50 MiB. Seeking selects a complete frame; a fractional request rounds down by less than one sample frame. Cached PCM is released when the reader is disposed.

For MPEG-1 Layer III, `Mp3Timeline` aligns the cache origin with legacy BASS playback. Media Foundation includes CBR Info frames in decoded PCM but omits VBR Xing frames. LAME/Lavf/Lavc metadata supplies the encoder delay. The reader removes the encoder delay and one remaining decoder sample, plus the header frame only for Info streams. A tag with both zero encoder delay and zero end padding is treated as missing gapless metadata: remove any included Info frame and retain 528 decoder-delay frames, as BASS does. Without a gapless tag, it restores the 528 decoder-delay frames retained by legacy BASS instead. The mapping applies equally to initial playback, seek and resume; it never changes beatmap timing or hitsound timestamps. Other MPEG frame sizes retain their existing origin. End padding is not trimmed by this origin correction.

Silent waveform comparisons against the user's local osu! BASS decoder (with its legacy gap handling enabled) covered Aoi Tsuki, Algebra and Bassdrop Freaks: the original leading errors were 1729 frames (36.021 ms at 48 kHz, 39.206 ms at 44.1 kHz), and the corrected correlation lag was zero frames. A generated untagged MP3 also aligned at zero frames. BASS and FFmpeg are diagnostic references only, not runtime dependencies. The upstream legacy-gap setting is documented in [osu-framework AudioManager](https://github.com/ppy/osu-framework/blob/master/osu.Framework/Audio/AudioManager.cs).

This is necessary because [Media Foundation's SetCurrentPosition does not guarantee an exact seek](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-imfsourcereader-setcurrentposition), while NAudio's reader reports the requested byte position before reading the returned samples. The cache establishes one continuous decoded timeline for playback, seek and duration.

While playing, `PositionMs` is the seek base plus the WASAPI device position converted through the output format's bytes per second and multiplied by playback speed. Source-reader position is not a playback clock because its buffered reads run ahead of the device. `OutputBufferAheadMs` measures the final mixed PCM read-through position against that device position, so Testplay can submit caught samples before their music frame leaves the writable buffer. Pending seek requests immediately own the displayed position until applied. A seek stops the old output, waits for its playback thread, discards queued buffers, seeks the decoder and opens fresh output buffers. Playing seeks resume; paused seeks remain paused. Pause captures the published position when requested and holds it while the worker stops and rebuilds output at that source frame. In-flight clock samples cannot move this pause point; a later seek still takes precedence. Resume uses the same frame, within source sample rounding. Superseded queued seeks are skipped. EOF reports the duration and replay starts at zero.

The default output is event-driven shared-mode `WasapiOut`; its stop operation joins the playback thread before the session is disposed. Its `Pause()` only suspends filling buffers, so the transport uses stop/rebuild to prevent buffered data and the device clock from advancing during a pause. An ended session is rebuilt from zero. The transport still treats `PlaybackStopped` as the ownership boundary for injected or alternative players: if their callback exceeds three seconds, the session and reader are detached from the active clock and disposed only after the callback. A second timeout retains an unavailable/error state until an explicit reload. Retired resources remain allocated if an alternative driver never completes the callback; diagnostics record the session, playback state and thread-pool counters.

SoundTouch.Net changes song tempo at 10%, 25%, 50%, 75%, and 150% while preserving pitch; 100% bypasses processing. Each output session owns a fresh processor, so seek, pause and speed changes discard all read-ahead state. Speed changes retain the published transport state until replacement output has restarted, so testplay does not interpret the rebuild as playback ending. SoundTouch buffers input until output is available and flushes once at EOF.

Timestamped hitsounds are mixed into the stretched music before the 16-bit conversion and output gain. They share its sample position and WASAPI buffer; the device-free tests compare event placement across output sample rates and channels. See [Hitsounds](../../../docs/HITSOUNDS.md).

Every Media Foundation decoder in this application is created through `MediaFoundationAudioReader`. Its shared lease owns startup and shuts down the process subsystem only after all active decode operations finish. Cached readers retain PCM without retaining a Media Foundation decoder. Additional Media Foundation users in this process must share that lifetime boundary.

Fixed dependencies compatible with the application's .NET 8 target (NAudio and NVorbis are MIT licensed):

- `NAudio.Core`, `NAudio.WinMM`, `NAudio.Wasapi` **2.2.1**. The WinForms package is not used. References: [WasapiOut](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiOut.cs), [MediaFoundationReader](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/MediaFoundationReader.cs), [MediaFoundationApi lifecycle](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/MediaFoundation/MediaFoundationHelpers.cs).
- `NAudio.Vorbis` **1.5.0** and `NVorbis` **0.10.4**. Reference: [VorbisWaveReader](https://github.com/naudio/Vorbis/blob/v1.5.0/NAudio.Vorbis/VorbisWaveReader.cs).

SoundTouch.Net **2.3.2** is LGPL-2.1-or-later and ships as a separate assembly; see the root third-party notices for its exact source revision.

Required licence texts are retained in `Audio/Licenses/` and copied to builds. Transitive versions are in the application lock file. No upstream audio implementation is copied into the application.

`FruitsAtelier.Audio.Tests` links these production audio sources to test the boundary independently of editor rendering. Device tests use real output devices with output PCM gain set to zero, without changing system/device volume or the editor's normal playback gain. Source decoding and sample comparisons still use the original data. The generated four-second WAV contains a low-amplitude 440 Hz stereo tone, but automated playback is silent. Controlled-output tests inject delayed stop callbacks and repeated failures to verify reader ownership and recovery. A generated OGG tone is included in the test fixtures and can be regenerated with FFmpeg:

```powershell
ffmpeg -hide_banner -loglevel warning -y -f lavfi -i 'sine=frequency=440:duration=4:sample_rate=44100' -af 'volume=0.03' -ac 2 -c:a libvorbis tests/FruitsAtelier.Audio.Tests/Fixtures/quiet-tone.ogg
dotnet run --project tests/FruitsAtelier.Audio.Tests -c Release
```

With external MP3 fixtures, the tests compare PCM at forward, backward, zero and end-of-file seeks against a continuous decode, including the first returned sample. They also compare the real device clock against elapsed time across playback, verify frame-aligned paused seeks, and check cancellation and recovery.

FFmpeg is a test-fixture tool only; the application does not require it. The worker samples the device clock every 10 ms while playing; snapshots are not extrapolated between samples. Requested output buffering is 10 ms. Test commands and fixture requirements are in [Testing](../../../docs/TESTING.md).
