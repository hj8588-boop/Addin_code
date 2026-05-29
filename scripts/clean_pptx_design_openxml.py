from __future__ import annotations

import re
import shutil
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


NS = {
    "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
    "p": "http://schemas.openxmlformats.org/presentationml/2006/main",
}

ET.register_namespace("a", NS["a"])
ET.register_namespace("p", NS["p"])


FONT = "Malgun Gothic"

COLOR_MAP = {
    # Existing orange/brown heavy palette -> calmer BIM training palette.
    "6B4724": "174A6A",
    "CC4700": "1F6E8C",
    "D95700": "2F75B5",
    "D66600": "2F75B5",
    "EFAE00": "8DB7D6",
    "FFD774": "CFE5F2",
    "FFCB7F": "CFE5F2",
    "FFEDD9": "EEF6FB",
    "FFFAF4": "F7FAFC",
    "FDF8E9": "F7FAFC",
    "F9EBB9": "EAF4FB",
    "806000": "5B7F95",
    # Reduce harsh alert colors while keeping emphasis readable.
    "FF0000": "C43B3B",
    "FFFF00": "FFE08A",
    # Normalize blues.
    "000066": "164A7A",
    "0070C0": "2F75B5",
    "305496": "1F4E79",
    # Muted support green.
    "548235": "5B8C5A",
}

THEME_COLOR_MAP = {
    "accent1": "1F4E79",
    "accent2": "2F75B5",
    "accent3": "1F6E8C",
    "accent4": "8DB7D6",
    "accent5": "CFE5F2",
    "accent6": "C43B3B",
}


def should_process_xml(name: str) -> bool:
    return (
        name.startswith("ppt/slides/")
        or name.startswith("ppt/slideLayouts/")
        or name.startswith("ppt/slideMasters/")
        or name.startswith("ppt/theme/")
    ) and name.endswith(".xml")


def normalize_font(root: ET.Element) -> None:
    for tag in ("latin", "ea", "cs"):
        for node in root.findall(f".//a:{tag}", NS):
            node.set("typeface", FONT)
            node.attrib.pop("pitchFamily", None)
            node.attrib.pop("charset", None)


def normalize_colors(root: ET.Element) -> None:
    for node in root.findall(".//a:srgbClr", NS):
        val = node.attrib.get("val", "").upper()
        if val in COLOR_MAP:
            node.set("val", COLOR_MAP[val])


def normalize_theme(root: ET.Element) -> None:
    for name, color in THEME_COLOR_MAP.items():
        for node in root.findall(f".//a:clrScheme/a:{name}/a:srgbClr", NS):
            node.set("val", color)
    for node in root.findall(".//a:fontScheme//a:latin", NS):
        node.set("typeface", FONT)
    for node in root.findall(".//a:fontScheme//a:ea", NS):
        node.set("typeface", FONT)
    for node in root.findall(".//a:fontScheme//a:cs", NS):
        node.set("typeface", FONT)


def clean_xml(data: bytes, name: str) -> bytes:
    try:
        root = ET.fromstring(data)
    except ET.ParseError:
        return data
    normalize_font(root)
    normalize_colors(root)
    if name.startswith("ppt/theme/"):
        normalize_theme(root)
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def slide_number(name: str) -> int:
    match = re.search(r"slide(\d+)\.xml", name)
    return int(match.group(1)) if match else 0


def report(path: Path) -> str:
    with ZipFile(path) as z:
        slides = sorted(
            [
                name
                for name in z.namelist()
                if name.startswith("ppt/slides/slide") and name.endswith(".xml")
            ],
            key=slide_number,
        )
        colors: dict[str, int] = {}
        fonts: dict[str, int] = {}
        for name in slides:
            root = ET.fromstring(z.read(name))
            for node in root.findall(".//a:srgbClr", NS):
                val = node.attrib.get("val")
                if val:
                    colors[val] = colors.get(val, 0) + 1
            for node in root.findall(".//a:latin", NS):
                val = node.attrib.get("typeface")
                if val:
                    fonts[val] = fonts.get(val, 0) + 1
        top_colors = ", ".join(f"{k}:{v}" for k, v in sorted(colors.items(), key=lambda x: x[1], reverse=True)[:10])
        top_fonts = ", ".join(f"{k}:{v}" for k, v in sorted(fonts.items(), key=lambda x: x[1], reverse=True)[:10])
        return f"slides={len(slides)}\nfonts={top_fonts}\ncolors={top_colors}"


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    dst.parent.mkdir(parents=True, exist_ok=True)

    with tempfile.NamedTemporaryFile(delete=False, suffix=".pptx") as tmp:
        tmp_path = Path(tmp.name)

    try:
        with ZipFile(src, "r") as zin, ZipFile(tmp_path, "w", ZIP_DEFLATED) as zout:
            for item in zin.infolist():
                data = zin.read(item.filename)
                if should_process_xml(item.filename):
                    data = clean_xml(data, item.filename)
                zout.writestr(item, data)
        shutil.move(str(tmp_path), dst)
    finally:
        if tmp_path.exists():
            tmp_path.unlink()

    print(dst.resolve())
    print(report(dst))


if __name__ == "__main__":
    main()
