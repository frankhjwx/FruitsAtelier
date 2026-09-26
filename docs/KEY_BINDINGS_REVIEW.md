# Shortcut compatibility review

This reference compares FruitsAtelier 0.8.6 with the
[official osu!stable default shortcut reference](https://osu.ppy.sh/wiki/en/Client/Keyboard_shortcuts),
not lazer or a user's customized bindings. Scope includes shared keyboard
dispatch, mouse modifiers, text fields, modal dialogs, Library, testplay, and
Windows/macOS input adapters.

The [shortcut manual](KEY_BINDINGS.md) documents implemented behavior. Proposals
below are not implemented bindings.

## Context routing

- Timing retains the Compose selection but blocks Ctrl+Shift+Left/Right object
  movement. Plain Ctrl+Left/Right still navigates bookmarks; Ctrl+Shift+Up/Down
  still adjusts playback speed.
- Compose accepts Shift+Left/Right for accelerated seeking and Shift+1–9 for
  Snap. Other Shift variants do not invoke unmodified transport or nudge commands.
- F6 accepts Delete and Ctrl+I for row deletion, without Shift or Alt. Unsupported
  modifier combinations are consumed. Ctrl+Shift+P creation, Ctrl+Shift+Z redo
  and modified row navigation retain their own meanings.
- Ctrl+Alt+E opens export from Compose and Timing outside focused Timing fields
  and modal dialogs. Adding Shift does not invoke export.
- The Settings language dropdown owns keyboard input while open: Up/Down selects,
  Enter applies, and Esc closes only the dropdown. Background Library shortcuts
  remain blocked until it closes.

Sources: [KeyDown](../src/FruitsAtelier.App/Editor/EditorView.Input.cs),
[TimingKey](../src/FruitsAtelier.App/Editor/EditorView.Timing.cs),
[HandleLegacyKey](../src/FruitsAtelier.App/Editor/EditorView.Shortcuts.cs).
Regression coverage: [ShortcutRoutingTests](../tests/FruitsAtelier.App.Tests/ShortcutRoutingTests.cs).

## Remaining usability gaps

### P3: Snap wheel changes depend unexpectedly on the active surface

Ctrl+wheel changes Snap over Compose canvas/timelines, but over the Timing waveform
an earlier branch returns without handling Ctrl. Shift+1–9, Ctrl+M and the visible
Snap bar provide alternatives. Share Snap-wheel dispatch across the surfaces if
the intended behavior is global. Source: [Wheel](../src/FruitsAtelier.App/Editor/EditorView.Input.cs).

### P3: Reverse count has narrow physical-key support

Only virtual keys 187 (`=`) and 189 (`-`) with Ctrl and without Shift are handled.
Ctrl+Shift+= and keypad Add/Subtract are ignored. The Mac adapter emits distinct
keypad codes too. Add these aliases if desired; preserve the documented Ctrl+=
binding. This is an accessibility/usability addition, not a stable compatibility
requirement. Sources: [KeyDown](../src/FruitsAtelier.App/Editor/EditorView.Input.cs),
[MacInput](../src/FruitsAtelier.Mac/EditorControl.cs).

## Differences from osu!stable

The following compact comparison uses the official reference above. The remaining
explanations and feasibility assessment derive from this repository's code.

| Keys / gesture | stable | FruitsAtelier |
| --- | --- | --- |
| Ctrl+L | Reload | Point curvature |
| Ctrl+J | Vertical flip | Extend FSlider |
| Ctrl+G | Reverse selection | Reverse one FSlider path |
| Ctrl+Shift+I | Import sample | Insert control in Compose |
| Ctrl+arrows | Contextual nudge / navigation | Bookmarks or speed; horizontal nudge uses Ctrl+Shift |
| Alt+wheel, canvas | Distance multiplier | Zoom |
| Alt+Shift+wheel | Fine distance multiplier | Unbound |
| Wheel up | Forward | Earlier |
| Ctrl+Shift+B | Remove current bookmark | Nearest within two seconds |
| Ctrl+P in Timing | Add red point | Also opens F6 draft |
| Double-click object | Seek | Slider editing; no general fruit seek |
| Alt+F4 | Leave editor | Close Windows application |

The horizontal-nudge mapping, slider editing chords and canvas Alt+wheel are
deliberate editor choices described in
[shortcut decisions](EDITOR_FEEDBACK_TASKS.md). They are compatibility differences,
not evidence of two handlers simultaneously executing. The FSlider chords are
retained. Any future migration requires moving the existing commands and updating
hints and documentation together.

Normal save, clipboard, undo/redo, clone, horizontal mirror, tools 1–4, playback,
bookmarks, timing creation/deletion, sound flags and snap/grid toggles already have
counterparts. T is correctly contextual: Grid Snap in Compose, tap tempo in Timing.
F5 is also contextual: testplay in the editor, scan in Library. Testplay F1/F2 and
Ctrl+P are isolated from Compose commands. These are legitimate context reuse.

## Missing keys that can reasonably be implemented

### Existing behavior can be reused

| Candidate | Existing building block | Required work / recommendation |
| --- | --- | --- |
| Ctrl+N: clear objects | Select-all and transactional deletion | Add a confirmation and a dedicated clear-all command; respect Lock Notes, drafts and undo. A shortcut alone is insufficient because the confirmation does not exist. |
| Ctrl+Shift+wheel: canvas zoom alias | `ZoomCanvasAt` | Small dispatch addition if this previously documented alias is still desired. Preserve Ctrl+wheel Snap and Ctrl+Alt+wheel tools. |
| Ctrl++ / keypad reverse aliases | `ChangeReverseCount` | Small exact-chord addition, with Windows/Mac mapping checks. |
| Double-click fruit / slider endpoint to seek | `SeekTo`, hit testing | Feasible, but must arbitrate against point editing, child selection and double-click boundary conversion. |
| Dedicated key to open Jump to time | `OpenTimeJump` | Small addition once an unused chord is chosen; not an existing stable editor requirement. |
| Stop playback key | Transport Stop behavior | Expose a chosen free chord if useful; X already restarts playback and Home only seeks. |
| Ctrl+Backspace and word navigation in fields | Shared caret/selection model | Add word-boundary movement/deletion centrally, preserving selection and clipboard behavior. |
| Keyboard navigation in Library and menus | Existing visible rows and menu actions | Add focus and traversal; arrows, Page Up/Down and Enter are not a complete Library navigation system today. |

Sources: [shortcut operations](../src/FruitsAtelier.App/Editor/EditorView.Shortcuts.cs),
[input dispatch](../src/FruitsAtelier.App/Editor/EditorView.Input.cs),
[time jump](../src/FruitsAtelier.App/Editor/EditorView.TimeJump.cs),
[transport](../src/FruitsAtelier.App/Editor/EditorView.TransportControls.cs),
[text input](../src/FruitsAtelier.App/Editor/EditorView.TextInput.cs),
[Library](../src/FruitsAtelier.App/Editor/EditorView.Library.cs).

### Feasible, but needs a content operation or explicit product decision

| Candidate | Why it is not just a binding |
| --- | --- |
| Ctrl+L / Ctrl+Shift+L reload | Define project versus linked `.osu` authority, partial/full reload, unsaved confirmation and per-difficulty state. Ctrl+L must first move to another chord. |
| Shift+Q/W/E/R and Ctrl+Q/W/E/R sample banks | Add selection-wide sample-bank editing, including imported source context and slider edge overrides, undo and export preservation. Timing-point bank controls do not implement object-bank edits. |
| Ctrl+Shift+I sample import | Needs an import dialog, asset copying/name policy and persistence; also conflicts with control insertion. |
| Ctrl+G selection reversal | Current path reversal affects one FSlider. Define group time/order reversal for mixed fruits, showers and repeated sliders. |
| Ctrl+Shift+S scaling | Define separate X/time scaling, pivot, range validation and slider-handle/repeat behavior. |
| Alt / Alt+Shift+wheel DS adjustment | DS uses multiple simultaneous presets. Choose which preset or aggregate is adjusted; existing Alt+wheel zoom would need a migration. |
| Ctrl-modified slider-velocity precision | Add editable SV first and define how it interacts with time-based FSliders and export. |

### No matching feature today

F2 Design, I sprite library, Design W/A/S/D, Ctrl+Shift+A AiMod, Ctrl+, / Ctrl+.
rotation, Ctrl+Shift+R rotation dialog, Ctrl+Shift+D polygon generation, and Ctrl+J
geometric vertical flip need new functionality. In this editor the vertical axis
is time, so geometric rotation/vertical flipping cannot safely be mapped onto
canvas coordinates as an alias. Chat, client screenshot sharing, boss key and
client rendering/debug shortcuts are outside the editor compatibility scope.

## Context and platform considerations for follow-up work

- Compose handles several page/file commands before its ordinary numeric-field
  handler; Timing fields consume keys earlier. Any dispatch refactor must define
  whether a focused valid field is committed or blocks the command. File commands
  already use `PrepareFileOperation` downstream; do not assume saving drops edits.
- Testplay capture prevents duplicate movement assignments and reserves Esc, Tab,
  F1 and F2, but allows modifiers and P/B/arrows. Those can overlap pause, bookmarks
  or volume chords. Windows has a separate raw-input worker; Mac feeds gameplay
  after shortcut dispatch. Test both routes before promising identical behavior
  for such configurations.
- F1 from Timing and normal modal Esc handling are implemented. Do not list F3,
  F4, F6, T tap tempo or timing precision modifiers as missing.

Physical-key aliases and consistent Snap-wheel handling can be added independently.
Clear-all and sample-bank editing require content operations. Larger transformation
and resource-import features need their own behavior specification.
