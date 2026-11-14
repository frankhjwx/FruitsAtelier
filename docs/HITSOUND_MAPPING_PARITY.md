# Mapping and Hitsounding command coverage

This reference compares the Catch Mapping workspace with Hitsounding. It describes
factory bindings outside focused text fields and modal dialogs. Default and lazer
editing are distinguished where their actions differ. Shared menu availability is
not equivalent to a Hitsounding-specific operation: Edit menu commands act on parent
objects, whereas Hitsounding keyboard clipboard/delete commands act on the active
Notes, sound or timing selection. Sample Library focus has its own command scope.

## Time navigation and canvas scrolling

| Mapping input / action | Hitsounding behavior |
| --- | --- |
| Space / C: play or pause | Space works; C alias is absent. |
| Left / Right: previous / next Snap step when paused, beat when playing | Supported; uses the active Default or lazer keyboard seek semantics. |
| Shift+Left / Right: four steps or beats | Supported, including playback; lazer retains its coarse seek semantics. |
| X: seek song start and play | Missing shortcut. Transport buttons remain available. |
| Home: seek song start | Missing shortcut. |
| Z: first object, then song start | Missing shortcut. |
| V / End: last object end, then song end | Missing shortcuts. Default Mapping uses the last parent by start order; lazer uses the maximum end. |
| Up / Down: next / previous timing point (reversed in lazer) | Missing shortcuts. Timing Helper can select a nearby/affecting point and explicitly Jump. |
| Ctrl+Left / Right without selection: bookmark in Default, parent start in lazer | Missing navigation shortcuts. |
| Alt+Left / Right in lazer: previous / next bookmark | Changes volume channel in Hitsounding, not bookmark position. |
| Ctrl+Shift+Left / Right in lazer: slider/sample boundaries | Missing. Mapping includes head, first-span midpoint, reverses, tail and adjacent parent boundaries. |
| Ctrl+B / Ctrl+Shift+B: add/remove bookmark | Missing keyboard handlers. Shared bookmark and overview controls remain available. |
| Ctrl+Up / Down: speed by 25 percentage points | Missing shortcuts; shared speed buttons work. |
| Ctrl+Shift+Up / Down: speed by 5 percentage points | Missing shortcuts. |
| Click timestamp: Jump to time | Shared dialog works; neither workspace has a dedicated default opening shortcut. |
| Click/drag shared bottom transport | Available. This is distinct from dragging the Hitsounding lanes. |
| Wheel over editing surface | Supported: Snap subdivision paused, full beat playing. Hitsounding wheel does not use lazer's BPM-dependent seek calculation. |
| Shift+wheel: coarse navigation | Missing on Hitsounding lanes; Shift is ignored there. Shared bottom overview has its own handler. |
| Ctrl+wheel: change beat division | Missing on Hitsounding lanes; it seeks instead. Snap slider remains available. |
| Alt+wheel: zoom | Hitsounding has pointer-anchored horizontal time zoom. It does not zoom Catch's spatial axis. |
| Ctrl+Alt+wheel: cycle tools in Default | Missing; Hitsounding treats Alt as time zoom. Lazer Mapping has no tool-cycle action. |
| Middle drag: canvas pan | Missing on Hitsounding lanes. Default Mapping pans time and, when unlocked, X; lazer canvas middle drag pans X. |
| Drag selection/objects to viewport edge | Mapping has edge scrolling. Hitsounding clamps the marquee and has no lane-edge auto-scroll. |
| Wheel/zoom while holding object or endpoint drag | Hitsounding can seek/zoom, but lacks Mapping's continuation/rebasing path. A stationary pointer does not refresh Notes/addition ghosts after navigation. |
| Space/C while holding a drag | Sound gestures can use Space; C is absent. Notes gestures consume keys other than Escape. |
| Z/V while holding a drag | Missing. |
| Select objects beyond the viewport by marquee + navigation | Hitsounding sound marquee retains its time anchor and follows playback; Notes marquee and held-note navigation do not have full Mapping parity. |
| Reverse canvas wheel preference | Catch-specific; not applied to Hitsounding lanes. |
| - / =: change canvas time spacing | Hitsounding uses these for horizontal timeline zoom. |
| Independent X/Y scale, uniform-scale lock, reset/follow viewport | Mapping canvas controls; Hitsounding has its own span and play-line-position controls, without a spatial axis. |

## Selection and content editing

| Mapping input / action | Hitsounding behavior |
| --- | --- |
| 1: Select | Supported. |
| 2/F: fruit placement | 2 selects Draw; unlocked Notes can create a centered fruit. F alias is absent. |
| 3/B: slider; 4/N: banana placement | No corresponding lane drawing tools. Existing parents can still be copied in Notes. |
| Click to select | Supported, without seeking. Notes requires its lane unlocked. |
| Ctrl+click: toggle selection | Supported for Notes parents, sounds, SB sounds and timing points. |
| Empty-space marquee | Supported. Notes and sound selections use separate gestures. Sound marquee excludes locked lanes. |
| Ctrl+marquee: add to selection | Notes preserves the earlier parent selection; sound marquee does not retain the prior sound set. |
| Shift+click range in lazer timeline | No equivalent pivot-to-target range selection. Notes Shift-click adds a parent instead. |
| Double-click timeline object: select and seek | Missing on Hitsounding lanes; double-click dispatch returns before Mapping's timeline handler. |
| Ctrl+A | Works for an active unlocked Notes selection. Sound and canvas timing selections do not have a select-all handler, despite the Hitsounding catalog entry. Sample Library and F6 have separate select-all commands. |
| Drag one or multiple parents in time | Unlocked Notes supports it with translucent ghosts and release commit. Slider head/tail hits resize that endpoint instead of moving the group. |
| Drag one or multiple Whistle/Finish/Clap tags | Supported in Select: shared time offset, preserved lanes, ghosts, atomic release, Escape and undo. All destination fruit events must exist. |
| Drag one or multiple SB sounds | Supported with one snapped offset; no destination note required. |
| Mixed SB and note-addition drag | Not a unified gesture: dragging an addition retains additions; dragging an SB sound retains SB samples. |
| Normal / SliderTick movement | These are tied to their notes; move Notes instead. |
| J / K: nudge selection earlier/later by Snap | Missing for Notes, additions and SB selections. |
| Ctrl+Left / Right: nudge selected X | Missing; Hitsounding has no X-position axis. |
| Ctrl+Shift+Left / Right: horizontal grid nudge in Default | Missing; no X-position axis. |
| Shift+timeline drag: disable beat snapping | Missing for Notes and SB dragging. Additions target existing fruit events. |
| Ctrl+D: clone after selection | Missing. Clipboard paste remains available. |
| Ctrl+C/X/V | Supported with contextual payloads. Notes paste creates parents in empty intervals or transfers sounds to a fully matching rhythm. Sound paste transfers to matching fruit events. SB paste preserves relative time and resources. Timing copy/paste uses original timestamps. |
| Delete | Supported contextually; Normal cannot be deleted, timing deletion protects red points. |
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Supported with shared undo history. |
| Right-click / Shift+right-click / middle-click | Hitsounding right-click deletes additions, SB or unlocked Notes; timing has its own menu. It does not inherit lazer's object menu/Shift-delete/middle-delete scheme. |
| L: Lock Notes | Missing shortcut. Per-lane lock buttons exist, including Notes. |
| Ctrl+H: horizontal mirror | Missing shortcut; geometric Mapping operation. |
| Ctrl+G: reverse in time | Missing shortcut; shared Edit menu reverse remains available for compatible parents. |
| Ctrl+Shift+R: rotation dialog | Missing shortcut; shared Edit menu rotation remains available for compatible parents. |
| Ctrl+, / Ctrl+.: quick rotation | Missing shortcuts. |
| Slider head/tail drag | Supported in unlocked Notes with preview and release commit. Read-only slider restrictions still apply. |
| Ctrl+plus/minus: slider reverse count | Supported with compatible Notes selected. Without that context it does not provide Mapping UI scaling. |
| Ctrl+plus/minus: UI scaling outside a slider selection | Missing in Hitsounding; settings remain available. |
| Enter / right-click: finish slider draft | No slider drawing draft in Hitsounding. |
| Shift+F: slider actions | Missing shortcut. |
| Ctrl+Shift+I: insert control; Ctrl+J: extend FSlider | Missing lane editing operations. Lazer Mapping reserves Ctrl+J. |
| Control point/handle editing, straight/curved controls, curve segment insertion | No geometric editing on Hitsounding lanes. Shared Edit actions may still operate on a compatible parent/control selection. |
| Split slider, reverse path, clear internal nodes, convert legacy sliders | Shared Edit menu entries remain; these are parent operations, not sound-tag transformations. |
| Ctrl+Shift+F / Ctrl+Shift+M: Stream/Stack / Merge inspector | Shared early handlers remain available; they operate on compatible parent selections. |
| Ctrl+F: Banana Shower edit | Shared early handler remains when a compatible banana selection exists. |
| Droplet settings, Stream conversion, AiMod, Mapset Verifier | Shared menus/docks remain available subject to their normal selection guards. |

## Snapping, timing and sound commands

| Mapping input / action | Hitsounding behavior |
| --- | --- |
| Shift+1–9: Snap divisor | Missing shortcuts; some modified keys can fall through to unrelated Hitsounding actions. Use the Snap slider. |
| Ctrl+M: cycle 1/3, 1/4, 1/6, 1/8 | Missing shortcut. |
| Ctrl+1–4 / G: spatial grid size | Missing lane equivalent; no spatial axis. |
| T: Grid Snap; Y: Distance Snap (T/Y in lazer) | No Hitsounding placement equivalents. |
| Hold Shift/Alt: temporary Grid/Distance Snap inversion | No corresponding sound/Notes gesture behavior. |
| Anchor Snap, object snap, DS presets and collinear spacing | Mapping geometry features; no sound-lane equivalents. |
| Q: New Combo | Q cycles the Hitsounding sample bank. No New Combo keyboard equivalent. |
| W/E/R: Whistle/Finish/Clap flags | Available, but toggle the selected event rather than Mapping's parent/placement workflow. |
| Shift+Q/W/E/R: Normal bank; Ctrl+Q/W/E/R: addition bank | Hitsounding uses Alt+Q/W/E/R for Normal and Ctrl+Alt+Q/W/E/R for additions. A/S/D and Q are its own bank brushes. |
| Ctrl+P / Ctrl+Shift+P: red/green timing point | Missing shortcuts in the lane scope. Timing menu and F6 remain; Hitsounding 5 creates a green point. |
| Ctrl+I: delete current timing section | Missing shortcut in lane scope. Shared Timing menu remains. |
| Timing reset/resnap/move/recalculate/delete-all/preview point | Shared Timing menu remains. |
| Metronome / meter controls | Shared Timing menu remains. |
| Red/green timing selection | Hitsounding supports Ctrl/marquee selection and Timing Helper inspection. Green groups can be dragged; red points cannot be dragged. |
| Timing copy/paste | Hitsounding supports selected points across difficulties; Ctrl+X does not provide the canvas timing selection with F6's cut behavior. |

## Shared application commands

| Mapping input / action | Hitsounding behavior |
| --- | --- |
| Ctrl+S; Ctrl+Tab / Ctrl+Shift+Tab | Save and difficulty switching supported. |
| Ctrl+Alt+E | Shared export handler supported, although no Hitsounding catalog entry exposes remapping. |
| Ctrl+O / Ctrl+Shift+O | Missing shortcut handlers; file menu/difficulty controls remain. |
| Ctrl+L: previous save | Missing shortcut; Edit menu still offers previous-save rollback and version history. |
| F3 / F4 / F5 / F6 | Shared handlers open Timing, Song Setup, Testplay and Control Points. No Hitsounding-specific catalog entries. |
| Ctrl+Shift+A | Missing AiMod shortcut; Edit menu remains. |
| Tab: Catch/osu! canvas switch | Shared early handler still toggles the stored Mapping canvas and cancels selection; it does not switch the visible Hitsounding lane layout. |
| Esc | Cancels the active Hitsounding gesture/menu first, otherwise returns to Library. |
| Alt+arrows | Volume channel/level controls work, including Alt+Left/Right in lazer Hitsounding. |
| F12 / Alt+Enter / Ctrl+Shift+F8 | Shared screenshot, fullscreen and diagnostics commands. |
| File/difficulty menus, layouts, settings, skins, shared transport and dock controls | Available subject to their individual guards; switching workspace does not remove these application features. |

## Implementation references

The command catalog is `EditorView.Keybindings.Catalog.cs`; routing and early-return
boundaries are in `EditorView.Keybindings.Input.cs` and `EditorView.Input.cs`.
Mapping keyboard behavior is owned by `EditorView.Shortcuts.cs` and
`EditorView.LazerInput.cs`; timeline gestures by `EditorView.ObjectTimeline.cs`.
Hitsounding handlers are in the `EditorView.Hitsounding*` partials. Shared menus
are in `EditorView.Drawing.cs`. Consult these together: a catalog entry alone does
not establish that a command has a working handler in the active selection scope.
