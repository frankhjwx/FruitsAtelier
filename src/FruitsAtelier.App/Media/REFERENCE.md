# Video decoding and storyboard images

`ICanvas` receives map time from the editor's audio transport. Video position is
map time minus the event's video offset. Decoding does not run a separate clock;
pause and tempo changes therefore request the same source timestamp as audio.
Negative video times are hidden. Video audio is never decoded or played. Video
opacity rises linearly over its first 500 ms and falls over its last 500 ms,
revealing the background beneath. Cover sizing centres the video and preserves
its aspect ratio. The editor dims the composed background, video and storyboard
layers together.

`VideoPlayback` owns one background decoder, native FFmpeg format/codec/scaler
contexts, and a read-only `FileStream`. The packaged osu! FFmpeg libraries enable
the pipe protocol rather than file URLs. Custom AVIO callbacks supply reads,
length and random-access seeks without enabling additional protocols. Callback
exceptions become native error returns. The MP4 `ignore_editlist` option and
generated-PTS setting follow osu!framework's compatibility choices.

Frames use best-effort timestamps and remain valid until the next presentation
timestamp, including low-frame-rate and variable-frame-rate intervals. Backward
requests and distant forward requests
seek to a preceding keyframe, flush the codec, and discard obsolete-generation
output. A one-second decode preroll preserves reordered frames near keyframe
boundaries. Containers without a usable seek index reopen at the beginning and
decode forward on the worker. Nearby requests can reuse cached frames in either
direction. Decoding
looks ahead by at most 150 ms plus the next presentation frame. A native reference
retains the closest preceding frame while decoding toward a seek target, avoiding
BGRA conversions for the rest of the preroll. It retains at most 12 pooled BGRA
frames and 96 MiB
of array capacity. Conversion fits inside 1920×1080 while preserving aspect ratio.
The render thread leases immutable frame pixels while copying a changed frame to
one persistent bitmap; GPU calls do not hold the decoder state lock. Disposal
cancels reads immediately, publishes no more frames, and releases native resources
on the worker. `Completion` signals that retirement has finished.

Seeking updates the transport immediately. While a new video frame is decoding,
the renderer keeps the last complete bitmap and its opacity in the current
viewport. A timestamp outside the video's presentation range displays no retained
video. A different media path or difficulty retirement releases that bitmap.

`StoryboardImageCache` decodes images on a worker. Preparing a different current
window replaces pending preloads; visible missing images can request priority
loads. Its per-difficulty LRU retains at most 256 MiB and 512 decoded entries.
Individual decoded images are limited to 64 MiB. Decode failures are cached and
reported through `ICanvas.MediaError`. Retiring a difficulty cancels publication
and releases its cache. A first-frame readiness barrier waits for currently
visible textures to upload before opening the map. Known missing or
failed images are terminal results, so they do not block presentation. If the
visible set itself exceeds the cache's byte or entry capacity, presentation
continues with available images and reports a warning.

Windows maintains a separate 256 MiB GPU texture LRU. A retained complete
storyboard frame can pin up to another 256 MiB while the requested frame loads;
the combined texture pool is bounded at 512 MiB. Replacing the retained set
releases its old pins and trims the pool back to 256 MiB. Uploads follow preload
priority with an 8 MiB frame budget; one larger image can use a frame on its own.
Textures are untinted: `ID2D1SpriteBatch` applies per-sprite straight-alpha colour,
opacity and transform. Only consecutive sprites sharing a texture and blend mode
are combined. Draw order is preserved across texture, additive and layer changes.
Each batch holds at most 2048 sprites. The renderer exposes sprite, draw-call and
texture-byte counters for native profiling. The macOS Avalonia implementation
preserves transform, origin, flips, colour and blending with bounded bitmap
caches; it uses individual drawing operations.

## Upstream and redistribution

The independently implemented decoder was compared with
[`VideoDecoder.cs`](https://github.com/ppy/osu-framework/blob/e01524d1492885d8b00ac88b38e7963d76d7d454/osu.Framework/Graphics/Video/VideoDecoder.cs)
and `Video.cs` at osu!framework revision
`e01524d1492885d8b00ac88b38e7963d76d7d454`. Its MIT notice is retained in
`Licenses/osu-framework-native-MIT.txt`.

Both hosts pin `FFmpeg.AutoGen` 4.3.0.1 and
`ppy.osu.Framework.NativeLibs` 2025.806.0-nativelibs. Only the four FFmpeg native
libraries are copied from that package; its BASS and other runtime files are
excluded. The Windows package contains x64 DLLs. The macOS dylibs contain both
x86_64 and arm64 slices and are copied to both application architectures.
The binding is LGPL-3.0; its license and the accompanying GPL-3.0 are distributed
in `Media/Licenses`. FFmpeg's LGPL-2.1 notice is distributed alongside them.
Users may replace the external libraries with ABI-compatible modified builds;
the application does not statically link them. The bundled native libraries are
FFmpeg 4.3.3 (libavcodec/libavformat ABI 58, libavutil 56 and libswscale 5).
The Windows build reports `LGPL version 2.1 or later`, with AVI/FLV/ASF,
MOV/MP4 and Matroska demuxers and the H.264/HEVC/VP8/VP9 and legacy MPEG-4,
FLV, VP6 and WMV2 decoder families enabled. It does not enable every FFmpeg codec.
