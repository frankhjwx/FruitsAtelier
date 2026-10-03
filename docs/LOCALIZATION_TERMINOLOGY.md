# Localization terminology

## Official references

Use osu! terminology for equivalent gameplay and editor concepts. Consult the
[official localization workflow](https://github.com/ppy/osu/wiki/Localising-the-client)
and the client resources in
[ppy/osu-resources at e4010bddabce61374aacfa676d0175d5788b14b4](https://github.com/ppy/osu-resources/tree/e4010bddabce61374aacfa676d0175d5788b14b4/osu.Game.Resources/Localisation).
The relevant resource groups are `Common`, `Editor`, `EditorSetup`,
`BeatmapStatisticStrings`, `Osu/OsuEditor`, `Catch/CatchEditor`, `SkinSettings`,
and `Web/Beatmapsets` (AR, CS, HP labels).

The [osu!stable language tables](https://m1.ppy.sh/release/Localisation/en.txt)
provide the classic editor menu vocabulary. The source language identifiers are
`en`, `zh-CHS`, `zh-CHT`, `ja`, `ko`, `ru`, `es`, `fr`, `pl`, `nl`, `ph`, `id`,
and `th`. These live files are not revisioned. Prefer stable's corresponding
editor menu entry for classic Timing actions; use lazer for equivalent metadata
and generic editor labels. Do not reuse text for a different operation merely
because its English label matches. For example, the Common "Finish" button is
not the finish hitsound.

## Dedicated names and compact labels

- Keep `FSlider` (Fruit Atelier Slider), `Stream`, and `Stack` in English. FA's
  Stream and Stack pattern types are distinct from osu!'s juice-stream object
  and object-stacking setting.
- Use the official Fruit tool translation in explanatory text where one exists;
  otherwise use `Fruit`. Keep the compact Fruit tool label in English.
- Keep `AR`, `CS`, `OD`, `HP`, `DPB`, and `NC` unchanged. Full descriptions may
  be translated, but retain the abbreviation beside the description.
- Keep preview mod identifiers `NM`, `Easy`, and `Hard Rock` in English.
- Compact object badges, tool identifiers, parameter labels, and pattern tabs
  retain English identifiers. Translate surrounding instructions and messages.
- Preserve keyboard key names, shortcuts, extensions, protocol and file-format
  identifiers, numeric units, and composite-format placeholders.

## Reference glossary

The Fruit column is the official `CatchEditor.fruit_tool` value when present;
`Fruit` denotes the English fallback. Slider uses `OsuEditor.slider_tool` when
available and the stable editor's slider term otherwise. Timing and New combo
use `Editor.timing` and `Editor.new_combo`; missing dedicated New combo entries
retain English. Grid Snap follows stable's `EditorMenuItem_Compose_GridSnapping`.

| Language | Fruit (full label) | Slider | Timing | New combo | Grid Snap |
| --- | --- | --- | --- | --- | --- |
| English (`en`) | Fruit | Slider | Timing | New combo | Grid Snap |
| 繁體中文 (`zh-TW`) | Fruit | 滑條 | 時間軸 | New combo | 網格校準 |
| 日本語 (`ja`) | Fruit | スライダー | タイミング | New combo | グリッドにスナップ |
| 한국어 (`ko`) | Fruit | 슬라이더 | 타이밍 | New combo | 격자 배치 |
| Русский (`ru`) | Фрукт | Слайдер | Тайминг | Новое комбо | Прикреплять ноты к сетке |
| Español (`es`) | Fruit | Slider | Tiempo | New combo | Activar fijado de rejilla |
| Français (`fr`) | Fruit | Slider | Timing | Nouveau combo | Alignement auto des éléments sur la grille |
| Polski (`pl`) | Owoc | Slider | Rytmika | New combo | Przyciąganie do siatki |
| Nederlands (`nl`) | Fruit | Slider | Timing | New combo | Rastersnapping |
| Filipino (`fil`) | Fruit | Slider | Timing | New combo | Snapping ng grid |
| Bahasa Indonesia (`id`) | Fruit | Slider | Timing | Kombo baru | Snap ke Petak |
| ไทย (`th`) | Fruit | สไลเดอร์ | เวลา | New combo | แนบช่องตาราง |
| 简体中文 (`zh-CN`) | 单独大果 | 滑条 | 时间轴 | 新连击 | 对齐到网格 |

`Web/Beatmapsets.show.stats.ar`, `.cs`, and `.drain` supply the full AR, CS,
and HP labels; their compact abbreviations remain English. The `.accuracy`
statistic is not the Overall Difficulty parameter and must not replace OD.

The OD label follows the [official Overall difficulty article](https://github.com/ppy/osu-wiki/tree/a3a3e16888e0c11af509e59d5cd818a128278fd7/wiki/Beatmap/Overall_difficulty)
at osu!wiki revision `a3a3e16888e0c11af509e59d5cd818a128278fd7`: Chinese uses
`判定严度`, Spanish uses `Dificultad general`, and Russian uses the article’s
`общая сложность` terminology. Other tables retain `Overall Difficulty (OD)`
when no localized term was verified.

## Maintenance

Check each new label against the official resource key and its UI meaning.
Missing upstream translations do not justify inventing a localized dedicated
name. Translate ordinary FA instructions as complete sentences and keep all
argument indices and formatting specifications intact. Review long descriptions
in context, especially plural nouns and narrow dialogs. Language tables contain
translations assisted by machine translation; official terminology and structural
checks do not substitute for native-speaker review of prose. All non-English
interfaces are marked unproofread and disclose AI-assisted machine translation
when selected, including the existing Simplified Chinese interface.

Official resource attribution and licence: [third-party notices](../THIRD_PARTY_NOTICES.md).
