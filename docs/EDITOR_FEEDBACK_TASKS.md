# Editor feedback tasks and shortcut decisions

This table defines the accepted editor feedback scope. The current user-facing bindings are maintained in [the shortcut manual](KEY_BINDINGS.md); interaction details are in [editing controls](EDITOR_UI.md).

| ID | Outcome | Acceptance |
| --- | --- | --- |
| E01 | Refresh New Combo numbering and grouping on the first edit. | Canvas, object timeline, preview, testplay and export agree; undo and redo restore grouping. |
| E02 | Replace fruit circles coincident with a completed new slider head. | Match both time and X exactly; preserve other fruits; use one undo transaction. |
| E03 | Align existing editor commands and tool keys with the accepted shortcut map. | All four number keys finish valid drafts or cancel insufficient drafts, then select their tool. Pressing 3 prepares another slider. Shift+1–9 adjusts Snap during drafting. Modifier combinations do not fall through to unrelated commands. |
| E04 | Drag the visible lower half of a slider-stream endpoint. | Hit testing respects overlapping stream sprites; selecting and dragging an endpoint retains the slider parent and supports undo. |
| E05 | Preserve playback time on empty canvas clicks. | Clear selection without seeking, both paused and playing; retain explicit time controls and box selection. |
| E06 | Limit Ctrl+M to 1/3, 1/4, 1/6 and 1/8. | Enter at 1/3 from another divisor; direct Snap controls retain all subdivisions. |
| E07 | Separate wheel snapping, seeking and zoom. | Ctrl changes complete Snap choices; Shift seeks four times as far; Alt zooms canvas; Alt zooms the upper timeline; Ctrl+Alt cycles tools on canvas/upper timeline. |
| E08 | Start testplay with a lead-in. | Persistent 0–5 second setting, default 1; immediately begin from the selected position minus the lead-in, clamped to zero, and return to the selected position on exit. |
| E09 | Expose Master, Music and Effect as vertical volume bars. | Left-to-right channel order; mouse and Alt arrows; 120 ms fade-in, 300 ms idle delay, 150 ms fade-out; hover/drag/held adjustment stays visible; changes persist without committing drafts. |
| E10 | Show bookmarks on the left canvas axis and in the canvas. | Distinct red timing and blue bookmark ticks/lines, nonoverlapping labels and clustered hover details; dense display does not merge stored entries. |
| E11 | Use blue canvas time labels. | Ordinary time labels remain distinct from red timing labels and lines. |
| E12 | Revisit upward-facing slider display. | Deferred; excluded from this change. |

## Accepted shortcut map

| Keys | Behavior |
| --- | --- |
| 1 / 2 / 3 / 4 | Select / Fruit / FSlider / Banana Shower; finish or cancel an active FSlider draft before switching. |
| Shift+1–9 | Set Snap to 1/1–1/9, including during a draft. |
| Ctrl+1–4 | Set horizontal grid to 4 / 8 / 16 / 32. |
| Ctrl+M | Cycle 1/3 → 1/4 → 1/6 → 1/8. |
| Ctrl+wheel | Adjust every supported Snap divisor on canvas and timelines. |
| Shift+wheel | Seek four times the normal wheel distance. |
| Alt+wheel over canvas | Zoom canvas. |
| Alt+wheel over upper timeline | Zoom object timeline. |
| Ctrl+Alt+wheel over canvas/upper timeline | Cycle placement tools. |
| Ctrl+= / Ctrl+− | Add / remove slider reverses. |
| Ctrl+L / Ctrl+Shift+I / Ctrl+J / Ctrl+G | Point curve toggle / point insertion / slider extension / path reversal. |
| Ctrl+O / Ctrl+Shift+O | Choose difficulty / open file or project. |
| Ctrl+Alt+E | Open export choices. |
| F4 / F5 | Song Setup / testplay. |
| Ctrl+Left / Right | Previous / next bookmark. |
| Ctrl+Shift+Left / Right | Horizontal object nudge. |
| J / K | Time nudge by one Snap subdivision. |
| Ctrl+Up / Down | Playback speed ±25 percentage points, clamped to 10–150%. |
| Ctrl+Shift+Up / Down | Playback speed ±5 percentage points. |
| Shift during upper timeline drag | Bypass beat snapping; Ctrl-click toggles selection. |
| Alt+Left / Right | Select volume channel. |
| Alt+Up / Down | Adjust selected volume by five percentage points. |
| Double-click slider | Retain slider editing. |

Existing save, undo/redo, selection, clipboard, clone, horizontal flip, hitsound flags, grid/DS toggles, locking, playback, bookmarks, tool aliases and testplay controls remain documented in the complete user manual. Canvas Alt+wheel distance-multiplier adjustment is deferred because this editor uses multiple DS presets.

## Reference and deliberate differences

Use [osu!stable's shortcut reference](https://osu.ppy.sh/wiki/en/Client/Keyboard_shortcuts) for the baseline and osu!lazer source when behavior is ambiguous. The source audit is pinned to ppy/osu commit `20e82fb18cd5ec2068f4551bb1fe0b1defc61cd1`:

- [ComposeBlueprintContainer.CurrentTool](https://github.com/ppy/osu/blob/20e82fb18cd5ec2068f4551bb1fe0b1defc61cd1/osu.Game/Screens/Edit/Compose/Components/ComposeBlueprintContainer.cs) commits active placement when changing tools. This editor also finishes when pressing 3 while already drawing a slider.
- [GlobalActionContainer](https://github.com/ppy/osu/blob/20e82fb18cd5ec2068f4551bb1fe0b1defc61cd1/osu.Game/Input/Bindings/GlobalActionContainer.cs) defines the default bindings. The accepted mappings above take precedence: this editor retains Ctrl+Left/Right bookmarks and Ctrl+Alt+wheel tool cycling.
- [VolumeOverlay](https://github.com/ppy/osu/blob/20e82fb18cd5ec2068f4551bb1fe0b1defc61cd1/osu.Game/Overlays/VolumeOverlay.cs) selects volume meters with previous/next actions and changes the selected meter with increase/decrease actions. This editor opens and adjusts on the same keypress, and uses the specified vertical layout and timeout.

These are behavioral references; no external implementation is copied into these changes.

## Shortcut audit

See the [current compatibility review](KEY_BINDINGS_REVIEW.md) for remaining conflicts, missing keys and implementation candidates. The Timing page, T tap tempo, timing precision modifiers, F3 and F6 are implemented; their bindings are covered by the [shortcut manual](KEY_BINDINGS.md#timing-page-and-f6-dialog).
