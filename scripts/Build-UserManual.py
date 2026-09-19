"""Render docs/USER_MANUAL.md to an English PDF. Requires reportlab."""

import argparse
import re
from html import escape
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.pagesizes import A4
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak


ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, default=ROOT / "artifacts/releases/FruitsAtelier-User-Manual.pdf")
args = parser.parse_args()
args.output.parent.mkdir(parents=True, exist_ok=True)

INK = colors.HexColor("#20312F")
GREEN = colors.HexColor("#187565")
MUTED = colors.HexColor("#60736F")
styles = {
    "body": ParagraphStyle("body", fontName="Helvetica", fontSize=9.2, leading=13, textColor=INK, spaceAfter=7),
    "title": ParagraphStyle("title", fontName="Helvetica-Bold", fontSize=27, leading=32, textColor=INK, spaceAfter=5),
    "section": ParagraphStyle("section", fontName="Helvetica-Bold", fontSize=19, leading=24, textColor=GREEN, spaceAfter=12),
    "sub": ParagraphStyle("sub", fontName="Helvetica-Bold", fontSize=11, leading=15, textColor=GREEN, spaceBefore=8, spaceAfter=6),
    "cell": ParagraphStyle("cell", fontName="Helvetica", fontSize=8.5, leading=11.5, textColor=INK, alignment=TA_LEFT),
}


def inline(value):
    value = escape(value)
    value = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", value)
    return re.sub(r"`(.+?)`", r'<font name="Courier">\1</font>', value)


def footer(canvas, doc):
    canvas.setStrokeColor(colors.HexColor("#C9DED7"))
    canvas.line(42, 39, A4[0] - 42, 39)
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(MUTED)
    canvas.drawString(42, 26, "FRUITSATELIER  /  USER MANUAL  /  0.8")
    canvas.drawRightString(A4[0] - 42, 26, str(doc.page))


story = []
lines = (ROOT / "docs/USER_MANUAL.md").read_text(encoding="utf-8-sig").splitlines()
index = 0
section = 0
while index < len(lines):
    line = lines[index].strip()
    index += 1
    if not line:
        continue
    if line.startswith("| "):
        rows = [line]
        while index < len(lines) and lines[index].startswith("|"):
            rows.append(lines[index])
            index += 1
        data = [[Paragraph(inline(cell.strip()), styles["cell"]) for cell in row.strip("|").split("|")]
                for row in rows if not re.match(r"^\|[\s:|\-]+$", row)]
        table = Table(data, colWidths=[174, A4[0] - 84 - 174], repeatRows=1, hAlign="LEFT")
        table.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#DBEEE7")),
            ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.HexColor("#F3F7F5"), colors.white]),
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ("LEFTPADDING", (0, 0), (-1, -1), 8), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
            ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
            ("LINEBELOW", (0, 0), (-1, 0), .7, colors.HexColor("#A1C8BB")),
        ]))
        story.extend([table, Spacer(1, 9)])
    elif line.startswith("## "):
        if section:
            story.append(PageBreak())
        section += 1
        story.append(Paragraph(inline(line[3:]), styles["section"]))
    elif line.startswith("### "):
        story.append(Paragraph(inline(line[4:]), styles["sub"]))
    elif line.startswith("# "):
        story.append(Paragraph(inline(line[2:]), styles["title"]))
    else:
        if line.startswith("- "):
            line = "&#8226; " + inline(line[2:])
        else:
            line = inline(line)
        story.append(Paragraph(line, styles["body"]))

doc = SimpleDocTemplate(str(args.output), pagesize=A4, leftMargin=42, rightMargin=42,
                        topMargin=38, bottomMargin=52, title="FruitsAtelier 0.8 User Manual",
                        author="FruitsAtelier", subject="Features and keyboard shortcuts")
doc.build(story, onFirstPage=footer, onLaterPages=footer)
print(args.output)
