# Slider interaction reference

Reference: MIT-licensed [ppy/osu revision 429b1c0](https://github.com/ppy/osu/tree/429b1c028d1921b81e174ddbd7eff32550e75237).
The relevant source is under `osu.Game.Rulesets.Osu/Edit/Blueprints/Sliders`.
This document distinguishes lazer behavior from the time-based editor's adaptations.

| Interaction | lazer source behavior | FruitsAtelier behavior |
| --- | --- | --- |
| First click | `SliderPlacementBlueprint` commits the head and enters ControlPoints state. | Starts a single undo transaction and fixes the first time–X point. |
| Moving the pointer | A temporary cursor point follows the pointer; overlapping the last fixed point removes that cursor. | The preview endpoint is temporary, including removal when it returns to the last fixed point. |
| Further clicks | Detach the cursor to make it permanent. Clicking the last fixed point again starts a segment, regardless of double-click timing. | Same fixed-point/preview distinction and repeated-click segmentation. |
| Default curve type | Each new segment progresses through linear, perfect curve, Bezier as its total point count grows. | Two points give a line, three an AR-referenced arc, four or more a Bezier. |
| Invalid perfect curve | `PathControlPointVisualiser.EnsureValidPathTypes` falls back to Bezier for excess controls or an excessive arc bounding box. | Falls back when the arc violates the playfield or forward-time constraints. Explicit arc commands retain validation errors. |
| Finish | Right-button release ends ControlPoints placement. | Right-button release completes the preview in one transaction; Enter is also supported. |
| Existing slider | A single selected slider exposes its control visualiser without a separate placement-tool requirement. | Controls are available in Select and Slider tools. |
| Ctrl insertion | `SliderSelectionBlueprint.addControlPoint` finds the nearest control-polygon edge, inserts, selects, and permits immediate dragging. | Insertion uses time order, which fixes the eligible control-polygon interval. New points can be dragged immediately. |
| Ctrl selection | Mouse-down preserves existing selected points; Ctrl mouse-up deselects only if no drag occurred. | Ctrl-drag preserves and moves the selected controls; Ctrl-click toggles membership. |
| Right click | Opens a context menu; quick deletion is a separate selection-blueprint operation. | Directly deletes the pointed control, by design. Delete handles batch selection. |
| Segment type | Context menus, Tab/Shift+Tab and Alt+number change types; S starts a new segment during placement. | Context menus select the supported types. Repeated endpoint clicks start segments; double-clicking an existing interior point toggles a boundary. |
| Freehand placement | Dragging from the initial head enters Drawing state and fits a B-spline on release. | Not implemented in osu legacy mode. Pen tool mode retains its handle-drag gesture. |
| Tail length | The tail marker changes expected path length independently of path nodes; Shift can adjust velocity instead. | No independent expected-length marker: endpoint time determines span duration. |
| Repeats | Timeline duration edits and path geometry are separate operations. | Reverses property and menu actions edit repeat count. No extra canvas repeat-drag marker. |

## Coordinate boundary

lazer's slider controls live in a spatial XY plane. Here, vertical position is time,
so a path must define a single X at each time. Backtracking control times and loops
cannot be copied directly. Zoom is a viewport transformation; it never rewrites
control points. Circular arcs retain their creation-time AR reference ratio.

Mode switches, hover, and selection preserve saved geometry. Both tools edit the
same model. Arcs and higher-degree curves only convert to cubic pen segments when
a pen operation requires handles, within the bounds documented in
[Project Model](PROJECT_MODEL.md). Undo restores the exact original segment.

Interaction documentation belongs here and in [Editing Controls](EDITOR_UI.md).
The canvas and properties panel show controls and values rather than instructional
paragraphs or explanations of internal conversion behavior.
