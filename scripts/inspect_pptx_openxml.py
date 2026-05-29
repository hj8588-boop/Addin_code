from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile


NS = {
    "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
    "p": "http://schemas.openxmlformats.org/presentationml/2006/main",
}


def slide_number(name: str) -> int:
    return int(re.search(r"slide(\d+)\.xml", name).group(1))


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    path = Path(sys.argv[1])
    with ZipFile(path) as z:
        slides = sorted(
            [
                name
                for name in z.namelist()
                if name.startswith("ppt/slides/slide") and name.endswith(".xml")
            ],
            key=slide_number,
        )
        print("slides", len(slides))
        fills: dict[str, int] = {}
        fonts: dict[str, int] = {}
        for index, name in enumerate(slides, 1):
            root = ET.fromstring(z.read(name))
            texts = [t.text.strip() for t in root.findall(".//a:t", NS) if t.text and t.text.strip()]
            for srgb in root.findall(".//a:srgbClr", NS):
                val = srgb.attrib.get("val")
                if val:
                    fills[val] = fills.get(val, 0) + 1
            for latin in root.findall(".//a:latin", NS):
                face = latin.attrib.get("typeface")
                if face:
                    fonts[face] = fonts.get(face, 0) + 1
            print(f"{index:02d}: " + " | ".join(texts[:8])[:220])
        print("\ncolors")
        for color, count in sorted(fills.items(), key=lambda item: item[1], reverse=True)[:25]:
            print(color, count)
        print("\nfonts")
        for font, count in sorted(fonts.items(), key=lambda item: item[1], reverse=True)[:25]:
            print(font, count)


if __name__ == "__main__":
    main()
