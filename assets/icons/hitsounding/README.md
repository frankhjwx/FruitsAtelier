# Hitsounding tool icons

Four symbols generated with the built-in OpenAI ImageGen tool, stored as 512×512
RGBA runtime PNGs. The editor draws the frames, active states, localized labels,
shortcuts and bank colours. Each decoded symbol uses 1 MiB, matching Mapping's
tool icon budget.

## Generation prompts

For draw, soft, normal and drum, use this shared prompt with the subject below:

Use case: productivity-visual. Create one production toolbar PNG symbol for
FruitsAtelier dark rhythm editor, matching its bold simple mint teal #59D3C3 and
off-white #F6F6F2 flat vector-like raster icons. [Subject] Square, centered, 15%
padding, readable at 40 pixels. Genuine transparent background, no tile, frame,
text, letters, numbers, watermark or cast shadow. Single icon only.

- draw.png: Draw tool: one chunky diagonal white and mint teal paintbrush, simple
  tip and single short mint stroke.
- soft.png: Soft sound brush: one soft rounded feather with three broad white
  barbs and mint teal stem.
- normal.png: Normal sound brush: one bold white circular sound disc with a mint
  teal center and two short symmetric sound-wave arcs.
- drum.png: Drum sound brush: one small cylindrical snare drum, white drumhead,
  mint teal body and two white drumsticks.

Select uses the existing `../tools/select.png` Mapping asset.
