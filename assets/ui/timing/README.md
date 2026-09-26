# Timing control assets

Generated with the built-in OpenAI ImageGen tool. `controls.png` contains the blue
numeric strip, cyan tap button, orange reset button and dark beat-lamp backgrounds.
`arrows.png` supplies the Timing Panel's left/right buttons. The setup window draws
flat arrows in code. All labels and values are localized and drawn by the editor.
The PNGs retain their generated alpha channels; source rectangles select sprites
without modifying the files. Both desktop projects package these assets.

## Generation prompts

### controls.png

Use case: ui-mockup. Asset type: production sprite atlas for a desktop rhythm game timing editor, inspired by classic osu legacy glossy blue timing controls. Generate ONE 1024x1024 PNG sprite sheet, transparent background. Exact layout: four horizontal strips each 896 pixels wide by 128 pixels high, at x=64 and y=64,320,576,832 respectively. First strip glossy bright blue rounded rectangular number field background with subtle bevel, empty center. Second strip glossy light cyan rounded rectangular tap button background. Third strip warm orange rounded rectangular reset button background. Fourth strip dark charcoal inset rounded rectangular beat lamp background. Each strip is a single clean empty shape occupying the specified rectangle, tiny corner radius 12 px, orthographic straight-on, no perspective. No text, no digits, no symbols, no arrows, no logos, no decorations outside rectangles. Leave space between strips fully transparent. Crisp classic 2007 desktop game UI aesthetic, subtle vertical gradients and thin edge highlights. All labels and arrows will be rendered dynamically in code.

### arrows.png

Production UI sprite atlas for classic osu legacy inspired timing editor. Transparent background, 1024x1024 square. Two identical square dark blue glossy beveled buttons side by side, left button occupies rectangle x=64 y=320 width=384 height=384, right button x=576 y=320 width=384 height=384. Left button centered thick bright white LEFT chevron, right button centered thick bright white RIGHT chevron. Chevron icons like classic osu timing BPM decrement and increment. Orthographic, crisp, subtle dark navy gradients, small rounded corners, thin blue edge. No text, no digits, no other objects, transparency around each button. Buttons aligned exactly same size and vertical position.
