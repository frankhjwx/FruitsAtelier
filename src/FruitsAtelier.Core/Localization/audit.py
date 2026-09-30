"""Check language tables, literal lookup keys, and obvious unlocalised UI strings.

Run from any folder: python src/FruitsAtelier.Core/Localization/audit.py
The literal scan is a review aid, not a C# parser. Format validity is also tested
with .NET CompositeFormat by LocalizationTests.
"""
from pathlib import Path
import json
import re
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_pairs)


def audit():
    tables = {path.stem: read(path) for path in HERE.glob("*.json")}
    source = tables["en"]
    errors = []
    placeholders = lambda value: set(re.findall(r"(?<!\{)\{(\d+)(?:[:,][^{}]*)?\}(?!\})", value))
    for language, table in tables.items():
        for key in sorted(source.keys() - table.keys()):
            errors.append(f"{language}: missing key {key}")
        for key in sorted(table.keys() - source.keys()):
            errors.append(f"{language}: unknown key {key}")
        for key, value in table.items():
            if not isinstance(value, str):
                errors.append(f"{language}: non-string value for {key}")
            elif key in source and placeholders(value) != placeholders(source[key]):
                errors.append(f"{language}: placeholder mismatch for {key}")
    referenced = set()
    # Strip comments first, preserving newlines; quoted // sequences are kept.
    token = re.compile(r'(?P<literal>\$?@?"(?:\\.|""|[^"\\])*?")|(?P<comment>//[^\n]*|/\*[\s\S]*?\*/)')
    for project in ("FruitsAtelier.App", "FruitsAtelier.Core"):
        for path in (ROOT / "src" / project).rglob("*.cs"):
            if any(part in ("bin", "obj", "Localization") for part in path.parts):
                continue
            content = path.read_text(encoding="utf-8-sig")
            code = token.sub(lambda match: match.group() if match.group("literal") else "\n" * match.group().count("\n"), content)
            for match in re.finditer(r'\b(?:L|Strings)\.Get\(\s*"([^"\n]+)"\s*(?=[,)])', code):
                key = match.group(1)
                referenced.add(key)
                if key not in source:
                    errors.append(f"{path.relative_to(ROOT)}:{code[:match.start()].count(chr(10))+1}: missing lookup key {key}")
            for match in token.finditer(code):
                if not match.group("literal"):
                    continue
                value = match.group()
                if re.search(r"[\u4e00-\u9fff]", value):
                    errors.append(f"{path.relative_to(ROOT)}:{code[:match.start()].count(chr(10))+1}: Chinese literal outside language table: {value}")
            # Direct visible text arguments; domain/file-format literals are not UI copy.
            for match in re.finditer(r'\b(?:Text|SetNotice|MessageBox)\(\s*(?:[^,\n]+,\s*)?\$?"([A-Za-z][^"\n]*[ A-Za-z][^"\n]*)"', code):
                errors.append(f"{path.relative_to(ROOT)}:{code[:match.start()].count(chr(10))+1}: direct UI literal: {match.group(1)}")
    for error in errors:
        print(error)
    print(f"Localization audit: {len(tables)} tables, {len(source)} source keys, {len(referenced)} direct lookup keys, {len(errors)} findings.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(audit())
