# Editor feedback tasks and shortcut decisions

The first table defines the accepted editor feedback scope. The [feedback backlog](#feedback-backlog) tracks additional proposals, implementation priorities and open design questions. The current user-facing bindings are maintained in [the shortcut manual](KEY_BINDINGS.md); interaction details are in [editing controls](EDITOR_UI.md).

| ID | Outcome | Acceptance |
| --- | --- | --- |
| E01 | Refresh New Combo numbering and grouping on the first edit. | Canvas, object timeline, preview, testplay and export agree; undo and redo restore grouping. |
| E02 | Replace fruit circles coincident with a completed new slider head. | Match both time and X exactly; preserve other fruits; use one undo transaction. |
| E03 | Align existing editor commands and tool keys with the accepted shortcut map. | All four number keys finish valid drafts or cancel insufficient drafts, then select their tool. Pressing 3 prepares another slider. Shift+1–9 adjusts Snap during drafting. Modifier combinations do not fall through to unrelated commands. |
| E04 | Drag the visible lower half of a slider-stream endpoint. | Hit testing respects overlapping stream sprites; selecting and dragging an endpoint retains the slider parent and supports undo. |
| E05 | Preserve playback time on empty canvas clicks. | Clear selection without seeking, both paused and playing; retain explicit time controls and box selection. |
| E06 | Limit Ctrl+M to 1/3, 1/4, 1/6 and 1/8. | Enter at 1/3 from another divisor; direct Snap controls retain all subdivisions. |
| E07 | Separate wheel snapping, seeking and zoom. | Ctrl doubles/halves Snap within the current supported family; Shift seeks four times as far; Alt zooms canvas; Alt zooms the upper timeline; Ctrl+Alt cycles tools on canvas/upper timeline. |
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
| Ctrl+wheel | Double/halve Snap within the current supported family on canvas and timelines; stop at boundaries. |
| Shift+wheel | Seek four times the normal wheel distance. |
| Alt+wheel over bottom timeline | Adjust the current volume channel. |
| Alt+wheel over canvas | Zoom canvas. |
| Alt+wheel over upper timeline | Zoom object timeline. |
| Ctrl+Alt+wheel over canvas/upper timeline | Cycle placement tools. |
| Ctrl+= / Ctrl+− | Add / remove slider reverses. |
| Ctrl+L / Ctrl+Shift+I / Ctrl+J | Confirm rollback to the previous saved version / point insertion / slider extension. |
| Ctrl+G | Reverse selection time order and each selected slider path. A single FSlider reverses within its own interval. |
| Ctrl+O / Ctrl+Shift+O | Choose difficulty / open file or project. |
| Ctrl+Alt+E | Open export choices. |
| F4 / F5 | Song Setup / testplay. |
| Ctrl+Left / Right | Move a Compose selection one X unit; otherwise seek the previous / next bookmark. |
| Ctrl+Shift+Left / Right | Move a Compose selection by the horizontal grid step. |
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
- [GlobalActionContainer](https://github.com/ppy/osu/blob/20e82fb18cd5ec2068f4551bb1fe0b1defc61cd1/osu.Game/Input/Bindings/GlobalActionContainer.cs) defines the default bindings. The accepted mappings above take precedence: this editor uses selection-dependent Ctrl+Left/Right and retains Ctrl+Alt+wheel tool cycling.
- [VolumeOverlay](https://github.com/ppy/osu/blob/20e82fb18cd5ec2068f4551bb1fe0b1defc61cd1/osu.Game/Overlays/VolumeOverlay.cs) selects volume meters with previous/next actions and changes the selected meter with increase/decrease actions. This editor opens and adjusts on the same keypress, and uses the specified vertical layout and timeout.

These are behavioral references; no external implementation is copied into these changes.

## Shortcut audit

See the [current compatibility review](KEY_BINDINGS_REVIEW.md) for remaining conflicts, missing keys and implementation candidates. The Timing page, T tap tempo, timing precision modifiers, F3 and F6 are implemented; their bindings are covered by the [shortcut manual](KEY_BINDINGS.md#timing-page-and-f6-dialog).

## Feedback backlog

Unchecked items are open proposals, not implementation commitments. Complexity is
an initial estimate including interaction, cross-platform behavior, undo and
performance verification. Items within each group are ordered approximately from
lower to higher complexity. Reported bugs require reproduction before their cause
or implementation scope is confirmed. Suggested first versions remain proposals
where a design decision is listed.

### Existing capabilities and discoverability

| Feedback | Current capability | Remaining follow-up |
| --- | --- | --- |
| Shortcut documentation | The [shortcut manual](KEY_BINDINGS.md) lists keyboard and mouse controls. | Review discoverability; global rebinding is F28 below. |
| Right-click a break to remove it | Right-clicking a break in the bottom overview removes it. | Confirm whether the request concerns the upper timeline; add equivalent access there if needed. |
| Retain objects when creating a difficulty | **Create from a difficulty or template → From difficulty** copies objects, timing, hitsounds and settings. See [creating difficulties](EDITOR_UI.md#creating-difficulties-and-map-templates). | Review whether this option is sufficiently discoverable. |
| Testplay retry shortcut | Ctrl+R and a held backtick retry the session. | Custom retry bindings remain open under F09. |
| Difficulty graph | Movement strain already plots normal-mode strain over time. | This does not resolve the request for a defined SR graph; see F29. |

### Recommended near-term work

Start by reproducing F05–F07 and F10; prioritize confirmed editing defects.
Then improve timing readability, selection information, difficulty navigation and
retry bindings. These items have relatively bounded outcomes.

| ID | Task | Complexity | Scope and acceptance considerations |
| --- | --- | --- | --- |
| F01 | [ ] Distinguish major and minor white beat ticks in the Timing panel. | Low | Use clear line width, length or brightness hierarchy, including dense subdivisions. |
| F02 | [ ] Show BPM values at red timing lines on the canvas. | Low | Label the BPM at each timing point and avoid overlapping labels in dense regions. |
| F03 | [ ] Show the time difference in milliseconds for two selected notes. | Low | Start with the absolute difference between start timestamps; explicitly define slider-head, tail and child-event selection behavior. This is a measurement, not an automatic ranking-criteria verdict. |
| F04 | [ ] Add shortcuts for switching difficulties. | Low | Navigate in tab order; assess Ctrl+Alt+Left/Right as candidate bindings and handle conflicts, text focus and active gestures. |
| F05 | [ ] Seek to red or green timing points when double-clicking their F6 list rows. | Low–medium | Reported behavior to reproduce. Locate the selected timestamp while retaining the row selection. |
| F06 | [ ] Prevent fruit placement outside the canvas playfield's left and right boundaries. | Low–medium | Reported behavior to reproduce. Apply the boundary consistently to placement previews and commits under zoom and pan. |
| F07 | [ ] Create a red slider anchor with a double-click when Slider Path is disabled. | Low–medium | Reproduce the reported need for a triple-click; ensure double-click processing does not create an extra anchor. |
| F08 | [ ] Allow disabling caught-fruit stacking in testplay. | Low–medium | Confirm that the request refers to the fruit pile on the catcher; make this a display preference without changing judgments. |
| F09 | [ ] Allow custom testplay retry bindings. | Medium | Extend the existing testplay binding UI; define short-press and hold-to-retry assignments, conflict handling and key-release behavior. |
| F10 | [ ] Auto-scroll when dragging objects against the upper timeline's left or right edge. | Medium | Reported behavior to reproduce. Keep the timeline and canvas coordinated, with stable pointer-to-time mapping, bounded scrolling speed, cancellation and one undo step. |
| F11 | [ ] Allow customization of beat-snap colours. | Medium | Include the hard-to-read 1/16 grey tick, consistent colours across relevant grids, persistence and reset to defaults. |
| F12 | [ ] Add basic map statistics. | Medium | Include total and per-type object counts, maximum combo and hyperdash count. Distinguish source objects from converted Catch events; reuse available difficulty results. PP is a separate task, F30. |

### Useful additions requiring a scoped design decision

Canvas waveforms and testplay seeking are strong candidates after the near-term
work. Resolve their display and session-state contracts before implementation.

| ID | Task | Complexity | Decision required and suggested first scope |
| --- | --- | --- | --- |
| F13 | [ ] Make OD follow AR when adjusting AR. | Low–medium | Decide whether this applies only to new difficulties or to all edits. Consider an explicit AR/OD link setting and define its default without silently overwriting existing OD values. |
| F14 | [ ] Add preset tags. | Low–medium | Clarify beatmap metadata tags versus Pattern Gallery tags. For metadata, consider personal presets with append and deduplication. |
| F15 | [ ] Optionally infer Snap when double-clicking an upper-timeline note. | Medium | Support the requested 1/4-to-1/3 transition while defining timing tolerance and ambiguity between subdivisions. Preserve double-click seeking and make automatic switching optional. |
| F16 | [ ] Extend Object Snap to the canvas centre and other vertical guides. | Medium | Specify eligible guides and precedence relative to Grid Snap and Distance Snap. Start with the centre and explicit grid guides, with visible snap feedback. |
| F17 | [ ] Show following points in testplay. | Medium; meaning unconfirmed | Obtain a concrete visual definition: object-to-object connectors, upcoming landing positions or skin followpoints. Define behavior across combos, breaks and mods. |
| F18 | [ ] Add quick-action buttons for selected objects. | Medium | Choose a small set of frequent actions, placement and occlusion rules; retain the context menu for advanced actions. |
| F19 | [ ] Display an audio waveform alongside the mapping canvas. | Medium | Reuse existing waveform decoding and caching. Consider an optional narrow side strip with opacity control; preserve alignment under zoom and BPM changes to support offset and snap inspection. |
| F20 | [ ] Suggest combo colours from the background palette. | Medium | Define palette size, readability against the background and interaction with skin overrides. Present selectable candidates before one undoable application. |
| F21 | [ ] Seek within testplay using a duration timeline. | Medium–high | Define judgments, combo, catcher state, audio and storyboard behavior after seeking. Consider restarting practice at the target with a short lead-in; decide which position Retry subsequently uses. |
| F22 | [ ] Drag red timing lines directly on the canvas with precise adjustment. | Medium–high | Decide whether dragging changes only timing offsets or also preserves object beat positions by moving objects. Consider a timing-only first version with explicit handles, a precision modifier, cancellation and one undo step. |

### Larger features requiring dedicated design

| ID | Task | Complexity | Design questions and dependencies |
| --- | --- | --- | --- |
| F23 | [ ] Apply New Combo patterns at configured beats while mapping. | Medium–high | Express examples such as every major white tick and third white tick as explicit positions within a measure. Define section origins, meter changes and existing NC handling. Consider previewed batch application to a selection before automatic placement rules. |
| F24 | [ ] Detect spinner traps in Mapset Verifier. | Medium–high; investigation needed | Establish representative examples and inspect the pinned MV engine for an existing check first. If absent, define reachability, mods and false-positive handling before adding a diagnostic. |
| F25 | [ ] Compare rhythm timelines across difficulties. | Medium–high | Start with read-only rows on a shared time axis and click-to-locate. Distinguish intentional omissions in lower difficulties from inconsistent snapping; avoid labeling every difference as an error. |
| F26 | [ ] Gradually increase or decrease spacing in wiggles and repeated groups such as doubles or triplets. | High | Define horizontal versus temporal spacing, group boundaries and which centre or endpoints stay fixed. Reuse Stack width envelopes where applicable; arbitrary selections need grouping, boundary and preview rules. |
| F27 | [ ] Add slider shape transformations, including curved-to-sharp conversion. | High | Specify what remains invariant: duration, endpoints, path length, tick timing or Catch positions. Handle FSliders and Legacy Sliders explicitly. Start with a defined polyline transformation and preview before broadening the tool set. |
| F28 | [ ] Make editor shortcuts globally customizable. | High | Define command identities, context-sensitive conflicts, modifiers, platform differences, persistence, defaults and synchronized shortcut hints. Keep F09 independently deliverable. |
| F29 | [ ] Add an actual difficulty/SR graph comparable to the requested MV view. | High; definition dependent | Decide between strain, independently rated sections and rolling-window star ratings. Define window size, boundaries, mods and cross-map scales; do not relabel strain as SR. |
| F30 | [ ] Add advanced performance statistics including maximum PP. | High; algorithm dependent | Specify algorithm revision, mods, accuracy and FC/SS assumptions. Assess reusable calculation support separately from the basic statistics in F12. |
| F31 | [ ] View multiple difficulties together for comparison or contest review. | High | Choose between rhythm rows, a read-only reference overlay and multiple fully editable canvases. Prefer a read-only first scope; coordinate navigation and scaling. F25 may satisfy the initial rhythm-comparison use case. |
| F32 | [ ] Suggest time signatures from NC placement and cymbal/finish hitsounds. | High; reliability uncertain | Defer implementation until representative examples establish a useful inference rule. Authoring markers do not reliably encode meter; define confidence and user confirmation rather than automatic timing edits. |

### Suggested delivery sequence

1. Reproduce and fix editing defects; improve tick hierarchy, BPM labels, time-difference readouts and difficulty navigation.
2. Improve practice and inspection through retry rebinding, scoped testplay seeking, canvas waveforms and basic statistics.
3. Agree on content-editing semantics for NC patterns, spacing gradients, slider transformations and timing-line dragging.
4. Plan multi-difficulty comparison, global shortcut customization and SR/PP analysis as separate features.

Use [editing performance](EDITING_PERFORMANCE.md) for interactive paths and
[building and testing](TESTING.md) for validation. Update the owning user-facing
documentation when an item ships; recording a proposal here does not establish
that its behavior is implemented.
