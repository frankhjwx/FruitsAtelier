# Default osu! Catch samples

These unmodified samples are from [ppy/osu-resources](https://github.com/ppy/osu-resources)
at commit `d8d01c29ce0f298159aea3644b947d8b4a1882a2`.

The normal, soft, and drum banks are the classic default samples from
`osu.Game.Resources/Skins/Legacy/`. Each bank includes hitnormal, hitwhistle,
hitfinish, hitclap, and slidertick. The banana sample comes from
`osu.Game.Resources/Samples/Gameplay/catch-banana.wav`.
The menu hover/back samples come from `Skins/Legacy/`; `menuhit.mp3`
packages its `click-short-confirm.mp3` confirmation sample. The pause loop comes
from `Samples/Gameplay/pause-loop.mp3`. Interface samples retain their original
encoding and use the same effects-volume output as hitsounds, even with music paused.
Exact source paths and SHA-256 hashes are recorded in `manifest.json`.

Copyright ppy Pty Ltd and contributors. Licensed under Creative Commons
Attribution-NonCommercial 4.0 International; the full text is in `LICENCE.md`.
The samples are copied unchanged into desktop builds. Runtime playback applies
beatmap volume and mixes overlapping events.
