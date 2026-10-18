# Editor tool icons

`osu-circle.png`, `osu-slider.png` and `osu-spinner.png` are transparent PNG symbols generated with the built-in OpenAI image generation tool. The shared editor draws their charcoal button frames, active state and localized labels. Catch tools retain their fruit, FSlider and banana artwork.

The shipped osu! symbols are 512×512 RGBA images (1 MiB decoded each), matching
the tool palette asset budget. Keep these runtime copies at that size so the
chrome and skin textures can remain in the shared scene cache.

## Generation prompts

### osu-circle.png

Use case: icon-symbol. Create one production PNG toolbar symbol for the dark FruitsAtelier rhythm-game editor. Transparent background with real alpha, no tile, no frame, no labels, no watermark, no shadows outside the symbol. Flat polished vector-like raster graphic, bold legible silhouette at 48 pixels, mint teal #59D3C3 and off-white #F6F6F2 with restrained darker teal shading. Square image, symbol centered with 15% padding on all sides. Subject: Circle placement tool, one clean off-white filled hit circle, mint teal thick outer ring and a small clean white glossy highlight. Front view, perfect circular geometry, no fruit, no leaf, no letters or numbers.

### osu-slider.png

Use case: icon-symbol. Create one production PNG toolbar symbol for the dark FruitsAtelier rhythm-game editor. Transparent background with real alpha, no tile, no frame, no labels, no watermark, no shadows outside the symbol. Flat polished vector-like raster graphic, bold legible silhouette at 48 pixels, mint teal #59D3C3 and off-white #F6F6F2 with restrained darker teal shading. Square image, symbol centered with 15% padding on all sides. Subject: Legacy Slider placement tool, one gently bent thick mint teal slider capsule rising from lower-left to upper-right, with a clearly visible circular off-white endpoint at each end outlined in mint teal. Smooth white curved central guide stroke. Front view, no fruit, no letters or numbers.

### osu-spinner.png

Use case: icon-symbol. Create one production PNG toolbar symbol for the dark FruitsAtelier rhythm-game editor. Transparent background with real alpha, no tile, no frame, no labels, no watermark, no shadows outside the symbol. Flat polished vector-like raster graphic, bold legible silhouette at 48 pixels, mint teal #59D3C3 and off-white #F6F6F2 with restrained darker teal shading. Square image, symbol centered with 15% padding on all sides. Subject: Spinner placement tool, a bold circular mint teal rotation arrow surrounding an off-white circular center disk, a second short concentric mint arc indicating spinning. Strong circular silhouette, front view, no fruit, no letters or numbers.
