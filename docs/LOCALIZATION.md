# Localization Maintenance

Application GUI text, status messages, and Core user diagnostics come from `FruitsAtelier.Localization.Strings`. The first launch defaults to `en`, independently of the operating system language. The top-bar button switches between available languages. The selection is saved in `FruitsAtelier/language.json` under the system application-data directory and restored on subsequent launches; missing or damaged settings fall back to English. Existing beatmap titles, object names, skin names, and user file contents are data and are not translated or rewritten when switching languages.

English is the project and documentation baseline. Maintain technical documentation and AI-facing instructions in English without parallel Chinese copies. Keep `README.zh-CN.md` as the Chinese user entry point. Other Chinese text belongs in translation resources or examples specifically explaining localization.

## Language tables and new entries

- Main table: [en.json](../src/FruitsAtelier.Core/Localization/en.json). English defines the complete key set.
- Chinese table: [zh-CN.json](../src/FruitsAtelier.Core/Localization/zh-CN.json). Its keys must match the main table.
- Code entry point: `using L = FruitsAtelier.Localization.Strings;`, then `L.Get("Module.SemanticKey", arguments...)`.

For new application text, add the same key to the English master and every language table before referencing it from code. Store complete sentences in resources and pass dynamic names, counts, and values as parameters. Do not assemble sentences by concatenating translated words in UI code. Machine data such as format field names, extensions, protocol tokens, and source-file text is not translated.

Use .NET composite-format placeholders such as `{0}`, `{1:F3}`, and `{2:0.######}`. Languages may reorder placeholders, but the set of argument indices must match. Escape literal braces as `{{` and `}}`. Numbers use the selected language's culture; machine-readable numbers in `.osu` and project files follow the file modules' format rules.

Built-in default names also come from resources and are used only when creating objects or when metadata is actually missing. The Chinese table currently retains the original English data values for these names. Never traverse existing documents and reassign names when changing languages. Language changes must invalidate or rebuild cached application diagnostics so that subsequent UI messages use the new language.

## Adding a language

Add a UTF-8 `<culture>.json` file under `src/FruitsAtelier.Core/Localization`, such as `fr-FR.json`. Copy every key from the main table and translate its string, including the language button label. Use a valid culture name for the filename.

Core embeds `Localization/*.json` and enumerates these resources at runtime to produce `AvailableLanguages`. Rebuilding after adding a matching JSON table makes the language available without maintaining a hardcoded list. This mechanism does not load external override files from the runtime directory or watch JSON changes while running.

## Validation and checks

`Strings.Validate()` / `LocalizationCatalog.Validate()` check missing or extra keys, valid composite formatting, and matching argument indices across languages. JSON parsing rejects duplicate keys and non-string values. Missing translations fall back to English; keys unknown to the main table display `[key]` to expose omissions.

Run `python src/FruitsAtelier.Core/Localization/audit.py` from the project root to check language tables, directly referenced resource keys, and obvious remaining GUI literals. The static scan is not a C# parser; use it alongside .NET tests and actual UI checks.

After changes, run the existing Core localization and App UI tests, then switch languages in the application and inspect menus, properties, tooltips, errors, number formatting, and narrow-window layouts. Confirm that switching does not change the document, dirty state, or existing names. The test entry point is `tests/FruitsAtelier.Core.Tests/LocalizationTests.cs`.

System and third-party exceptions retain their original messages and may receive a localized outer explanation. Language-resource bootstrap failures use independent messages to avoid recursively loading the language table.
