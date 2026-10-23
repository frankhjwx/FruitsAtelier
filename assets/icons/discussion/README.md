# Discussion icons

The SVG masters are unmodified Font Awesome Free 5.15.4 icons from
FortAwesome/Font-Awesome commit `7d3d774145ac38663f6d1effc6def0334b68ab7e`.
Source: https://github.com/FortAwesome/Font-Awesome/tree/7d3d774145ac38663f6d1effc6def0334b68ab7e/svgs

Regular: circle, check-circle, clock, sticky-note.
Solid: bullhorn, heart, exclamation-circle, tasks, reply, chevron-down,
chevron-up, sync-alt, thumbs-up, thumbs-down.

Copyright Fonticons, Inc. Icons are licensed under CC BY 4.0; the upstream
licence notice is retained in `LICENCE.txt`. PNGs are transparent 128px renderings
with white fills, tinted at runtime. Both platform renderers use the same PNGs.

Type-to-icon mapping follows the public osu!web discussion UI at commit
`1798caf054d175cb6ead549d4b2daf58f85ed203`,
`resources/js/beatmap-discussions/discussion-type.ts`.
No osu!web implementation code is included.

Regenerate PNGs with `node scripts/Generate-DiscussionIcons.cjs` from the project
root, with `sharp` available on `NODE_PATH`.
