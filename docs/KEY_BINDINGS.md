# Keyboard and mouse shortcut manual

This reference describes FruitsAtelier 0.9.0. Bindings depend on the active page,
focused field, selected object and drawing state. For differences from osu!stable
and proposed additions, see the [compatibility review](KEY_BINDINGS_REVIEW.md).

## Reading the tables

- `Ctrl` also accepts Command on macOS. `Alt` corresponds to Option. Some Mac
  keyboards require Fn for function keys.
- Number shortcuts use the main number row. Slider reverse shortcuts use the
  main `=` and `-` keys, without Shift; keypad plus/minus are not aliases.
- Unless stated otherwise, editing shortcuts apply in Compose, outside text
  fields and dialogs, after releasing a drag and finishing a drawing draft.
- Selection-dependent commands require a compatible selection. Lock Notes blocks
  moving, reshaping and deleting existing objects, but allows selection, playback,
  placement, flags and undo/redo.
- Only testplay movement and dash bindings are configurable in Settings.

## Files and pages

| Keys | Action |
| --- | --- |
| Ctrl+O | Choose a difficulty in the current project. |
| Ctrl+Shift+O | Open a supported beatmap, archive or project file. |
| Ctrl+S | Save the project; also update linked exported difficulties. |
| Ctrl+Alt+E | Open export choices from Compose or Timing, outside focused Timing fields and modal dialogs. |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous difficulty. |
| F1 | Return from Timing to Compose / Details. |
| F3 | Open the Timing page. |
| F4 | Open Song Setup. |
| F5 | Start testplay at the playhead with the configured lead-in. |
| F6 | Open Timing and Control Points. |
| Esc | Dismiss the current field, popup, menu or gesture first. From idle Compose, request return to Library; from Timing, return to Compose. Unsaved changes can prompt. |

F2 has no Compose page action. It has a separate meaning during testplay.
Windows Alt+F4 requests closing the application window; use Esc to return to
Library. Finish a text edit with Enter before using page or file commands.

## Tools and selection

| Keys | Action |
| --- | --- |
| 1 | Select tool. |
| 2 / F | Fruit tool. |
| 3 / B | FSlider tool. |
| 4 / N | Banana Shower tool. |
| Ctrl+A | Select all parent objects. |
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / copy / paste selected parent objects. |
| Ctrl+D | Clone the selection, positioning its earliest start one measure after the last selected start. |
| Delete | Delete selected objects or selected slider controls. On macOS, Backspace also works outside text fields. |
| Ctrl+Z | Undo. |
| Ctrl+Y / Ctrl+Shift+Z | Redo. |
| Ctrl+H | Mirror selected objects around X=256. |
| Ctrl+Shift+Left / Ctrl+Shift+Right | Move selected objects by one X unit. |
| J / K | Move selected objects earlier / later by one current Snap subdivision, using the earliest selected start's BPM. |
| L | Toggle Lock Notes. |

Object paste is confined to the same difficulty session and aligns the earliest
copied start to the playhead. Copy also writes an osu! timestamp reference to the
system clipboard. Slider controls use their own editing selection; the object
clipboard works on complete parent objects.

During an FSlider draft, 1–4 finish valid geometry (or cancel insufficient geometry)
before changing tools. Pressing 3 prepares another slider. Shift+1–9 remains
available during drafting. An unfinished Banana Shower is cancelled on a tool
change. Other shortcuts may be blocked until the draft or drag ends.

## Playback, navigation and bookmarks

| Keys | Action |
| --- | --- |
| Space / C | Play / pause. With beat snapping enabled, pausing aligns to the nearest current Snap grid line; see [pause behavior](EDITOR_UI.md#playback-pause-snapping). |
| X | Seek to song start and play. |
| Home | Seek to song start without forcing playback. |
| Z | Seek to the first object's start; if already at or before it, seek to zero. |
| V / End | Seek to the end of the last object by start order; if already at or after it, seek to song end. |
| Left / Right | Seek earlier / later by one full beat during playback. While paused, move to the preceding / following Snap grid line, including timing boundaries. |
| Shift+Left / Shift+Right | Seek four full beats during playback. While paused, move four Snap grid lines in the chosen direction. |
| Up / Down | Previous / next timing point, including inherited points. |
| Ctrl+Up / Ctrl+Down | Increase / decrease speed by 25 percentage points, within 10%–150%. |
| Ctrl+Shift+Up / Ctrl+Shift+Down | Increase / decrease speed by 5 percentage points. |
| Ctrl+B | Add a bookmark at the playhead. |
| Ctrl+Shift+B | Remove the nearest bookmark within two seconds. |
| Ctrl+Left / Ctrl+Right | Previous / next bookmark, even with objects selected. |
| Alt+Left / Alt+Right | Open volume controls and select Master, Music or Effect. |
| Alt+Up / Alt+Down | Open volume controls and change the selected channel by five percentage points. |

Click the current timestamp to open Jump to time. It accepts milliseconds,
`mm:ss:ms`, and copied osu! timestamp references. There is no keyboard shortcut to
open it. Enter seeks; Esc cancels. Volume shortcuts also work during testplay.

## Snapping, combos and hitsounds

| Keys | Action |
| --- | --- |
| Shift+1 through Shift+9 | Set Snap to 1/1 through 1/9. |
| Ctrl+M | Cycle 1/3, 1/4, 1/6, 1/8; start at 1/3 from another divisor. |
| Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4 | Set horizontal grid spacing to 4 / 8 / 16 / 32. |
| G | Cycle those horizontal grid spacings. |
| T | Toggle Grid Snap in Compose. In Timing, T is tap tempo. |
| Y | Toggle Distance Snap. |
| Hold Alt | Temporarily invert Distance Snap. |
| Hold Shift | Temporarily invert horizontal Grid Snap, unless Alt is also held. |
| Q | Toggle New Combo for the selection or placement state. |
| W / E / R | Toggle Whistle / Finish / Clap for the selection or placement state. |

The full Snap choices are 1/1–1/9, 1/12 and 1/16. The last two are accessible from
the Snap slider or Ctrl+wheel. Beat snapping is independent of Slider Tick Rate.
Object sampleset selection is not bound to Shift+Q/W/E/R or Ctrl+Q/W/E/R.

## Slider editing

| Keys | Action |
| --- | --- |
| Enter | Finish a slider draft. |
| Esc | Cancel a draft or active gesture. |
| Ctrl+L | Toggle the selected control between straight and curved. |
| Ctrl+Shift+I | Insert a control on the curve under the pointer in Compose. |
| Ctrl+= / Ctrl+- | Add / remove one reverse on the selected FSlider. |
| Ctrl+G | Reverse the selected FSlider's first-span horizontal trajectory while retaining its time range and repeats. |
| Ctrl+J | Extend the selected FSlider to the pointer at a valid later canvas time. |
| Ctrl+Shift+F | Open slider-to-stream conversion, or Change snapping for a stream selection. |

In the stream dialog, Left/Up and Right/Down decrease and increase the chosen
subdivision. Enter applies; Esc cancels. Converting to a stream retains an editable
slider parent and exports fruit objects.

Ctrl+L, Ctrl+G, Ctrl+J and Ctrl+Shift+I have editor-specific meanings; do not assume
osu!stable behavior. Ctrl++ (Ctrl+Shift+= on a US keyboard) and keypad plus/minus
currently do not adjust reverses.

## Mouse combinations

| Input and location | Action |
| --- | --- |
| Wheel over canvas, object timeline or overview | Up seeks earlier; down seeks later. One notch is a Snap subdivision while paused, or a whole beat while playing. |
| Shift+wheel | Seek four times the normal wheel distance. During marquee selection, wheel seeking remains single-step. |
| Ctrl+wheel over canvas or either timeline | Cycle all supported Snap choices. |
| Alt+wheel over canvas | Zoom around the pointer's time. |
| Alt+wheel over object timeline | Zoom that timeline independently. |
| Alt+wheel over Timing waveform | Zoom the waveform time scale. Ctrl+wheel on the waveform currently does nothing. |
| Ctrl+Alt+wheel over canvas or object timeline | Cycle the four tools. |
| Middle-button drag over canvas | Pan the canvas. |
| Ctrl+click object / Ctrl+marquee | Toggle selection / add objects to selection; slider control actions take priority when applicable. |
| Shift+drag object in upper timeline | Move without beat snapping. |
| Ctrl+click bottom overview | Toggle a bookmark at the clicked time; clicking near an existing bookmark removes it. |
| Shift+drag bottom overview | Create a break interval. Esc cancels. |
| Right-click a break in bottom overview | Remove that break. |
| Ctrl+click while drawing a slider | Add a straight segment. |
| Ctrl+click an existing slider control | Make it straight. |
| Ctrl+click within an editable slider's time range | Insert a control at the pointer; may change its shape. |
| Shift while adjusting a DS preset or Prev DS slider | Use 0.01 steps instead of 0.1. |
| Shift while dragging a Song Setup difficulty slider | Use 0.1 steps instead of whole units. |

Ctrl+Shift+wheel has no current canvas zoom binding. Alt+wheel over the canvas
changes zoom rather than DS presets. For right-click drafting, deletion and
double-click slider editing, see [Editing Controls](EDITOR_UI.md#fsliders).

## Timing page and F6 dialog

| Context and keys | Action |
| --- | --- |
| Timing page: T | Register a tempo tap. Apply timing commits the measured BPM and offset. |
| Timing page: hold Ctrl during metronome playback | Use three ticks per beat for Snap divisors divisible by three, two for other even divisors, otherwise one. |
| Compose or Timing: Ctrl+P / Ctrl+Shift+P | Open the timing draft and add a red / green point at the playhead. |
| Compose or Timing: Ctrl+I | Delete the current timing section, protecting the first red point. |
| F6 list: Ctrl+A | Select all visible rows. |
| F6 list: Ctrl+click / Shift+click | Toggle rows / select a range. |
| F6 list: Up/Down, Page Up/Down, Home/End | Navigate rows, eight rows per page step, or first/last row. Shift extends selection; Ctrl toggles the destination row. |
| F6 list: Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste `.osu` timing-row text. |
| F6 list: Delete / Ctrl+I | Delete selected rows, protecting the first red point. |
| F6 list: Ctrl+P / Ctrl+Shift+P | Add a red / green point to the draft. |
| F6 list: Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Undo / redo / redo draft changes outside a text field. |
| F6: Tab | Focus the first numeric field; while editing, Tab / Shift+Tab commits and moves to the next / previous field. |
| F6: Enter | Commit the focused field; when no field is active, apply the dialog as one undo step. |
| F6: Esc | Cancel the focused field first; otherwise discard and close the dialog. |
| Timing command confirmation: Enter / Esc | Apply / cancel. |

When using BPM stepper buttons, normal/Ctrl/Shift steps are 1/0.25/5 BPM.
Offset steps are 2/1/10 ms. Ctrl takes precedence if both Ctrl and Shift are held.
Slider velocity is preserved on import but has no editable precision shortcut.

Playback, time navigation, bookmarks, speed, undo/redo and difficulty switching
remain available on the Timing page outside its fields. Use the visible Snap bar
or Shift+1–9 / Ctrl+M there. Ctrl+Shift+Left/Right is inactive in Timing; return
to Compose before moving selected objects.

## Testplay

| Keys | Action |
| --- | --- |
| Left / Right | Move catcher, by default. |
| Hold Shift | Dash, by default. |
| Tab | Toggle autoplay. |
| Ctrl+P | Pause / resume the session. |
| Ctrl+B / Ctrl+Shift+B | Add a bookmark / remove the nearest within two seconds at the live position. |
| Esc / F1 | Exit and return to the selected testplay start position. |
| F2 | Exit at the current testplay position. |
| Alt+arrows | Select and adjust volume channels as above. |

Change movement and dash in **Settings → Testplay keys**: click a binding, press
the new key, then Apply. Esc cancels capture; reusing an assigned key swaps the
two assignments. Esc, Tab, F1 and F2 are reserved. Supported keys include letters,
digits, arrows/navigation keys, modifiers, keypad keys, punctuation and F3–F24.
OS/media keys are not supported. System shortcuts can intercept some combinations.
Prefer bindings that do not overlap Ctrl+P, Ctrl+B or Alt+arrows while held.

## Text fields, Library and other dialogs

| Context and keys | Action |
| --- | --- |
| Text: Ctrl+A, Ctrl+C/X/V | Select all, copy/cut selection, paste at the caret. |
| Text: Left/Right, Home/End | Move caret; hold Shift to extend selection. |
| Text: Backspace / Delete | Remove selection or the previous / next character. |
| Compose numeric field: Enter / Esc | Commit / cancel. Tab / Shift+Tab commits and cycles fields. |
| Prev DS or X input: Enter / Tab / Esc | Commit / commit / cancel the live adjustment. Ctrl+Z ends and undoes a changed preview. |
| Song Setup: Tab / Shift+Tab | Cycle editable fields on the active tab. |
| Song Setup: Enter / Esc | Apply / discard; Esc first closes an open countdown dropdown. |
| Library: Ctrl+F | Focus and select the search text. |
| Library: F5 | Rescan the library. |
| Library: Enter | Leave a text field; with no field focused, open the selected map. |
| Settings: Esc | Cancel key capture or field focus first, then close without applying pending drafts. |
| Export: Up / Down | Cycle export modes when no filename field is focused. |
| Export: Tab | Toggle filename focus for modes that accept a name. |
| Export: Enter | Leave filename editing first; with no field focused, submit the export. |
| Export: Esc | Close export choices. |
| Language dropdown in Settings | Up/Down chooses a language; Enter applies; Esc closes only the dropdown. Other background shortcuts are blocked while it is open. |
| DS configuration: Enter / Esc in base field | Leave base-field editing. Use Apply or Cancel for the dialog draft. |
| DS configuration: Esc outside base field | Cancel an active drag first; otherwise close the dialog. |
| Volume settings dialog: Esc | Close the dialog. |
| Error dialog: Enter / Esc | Dismiss the error. |
| Unsaved-change confirmation: Enter / Esc | Cancel the pending operation; does not choose Save or Discard. |
| Slider import/conversion panel: Esc | Decline import conversion, cancel a running conversion, or dismiss its errors, as applicable. |
| Updates page: Esc | Leave the page. |

Text fields do not currently implement word navigation/deletion or general text
undo/redo. Dialogs consume background editing keys. Some Compose page/file keys
are processed before its numeric-field handler, so explicitly commit the field
before invoking those commands.

## References

- [User manual](USER_MANUAL.md)
- [Editing controls and mouse gestures](EDITOR_UI.md)
- [Shortcut compatibility review and possible additions](KEY_BINDINGS_REVIEW.md)
- [osu!stable default shortcut reference](https://osu.ppy.sh/wiki/en/Client/Keyboard_shortcuts)
