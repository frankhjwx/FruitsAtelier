# FruitsAtelier Branding

The approved identity is an italic FA monogram with a single leaf above the A. Vector paths were reconstructed from the supplied reference; the previous cherry artwork is not the active identity.

- `mark.svg` / `mark.png`: purple standalone mark for application chrome and compact placements.
- `app-icon.ico`: Windows executable and window icon, generated directly from `mark.png` with transparent padding and preserved proportions. Contains 16–256 px representations.
- `app-icon.svg` / `.png` / `.icns`: white-on-charcoal macOS icon. ICNS includes 16–1024 px representations.
- `wordmark-en.svg` / `.png`: mark on the left and FruitsAtelier on the right.
- `wordmark-zh.svg` / `.png`: mark on the left and 水果工坊 on the right.

SVG wordmarks adapt their text colour to the browser's light/dark preference. PNG wordmarks use dark text on transparency. The brand names are FruitsAtelier in English and 水果工坊 in Simplified Chinese; do not translate existing user-authored project or song names.

Vector and PNG/ICO generation: run `scripts/Generate-Branding.cjs` with Node.js and `sharp` available on NODE_PATH. On macOS, package the generated iconset with:

```bash
iconutil -c icns artifacts/branding/app-icon.iconset -o assets/branding/app-icon.icns
```

The desktop projects bundle the standalone mark and application icons. Windows embeds ICO in the executable and uses it for the window; the macOS publishing script installs ICNS and localized application display names. Both README pages use the corresponding horizontal SVG wordmark.
