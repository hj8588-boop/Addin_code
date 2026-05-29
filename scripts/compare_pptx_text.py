from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile


NS = {"a": "http://schemas.openxmlformats.org/drawingml/2006/main"}


def slide_number(name: str) -> int:
    return int(re.search(r"slide(\d+)\.xml", name).group(1))


def slide_texts(path: Path) -> list[list[str]]:
    with ZipFile(path) as z:
        slides = sorted(
            [n for n in z.namelist() if n.startswith("ppt/slides/slide") and n.endswith(".xml")],
            key=slide_number,
        )
        out = []
        for name in slides:
            root = ET.fromstring(z.read(name))
            out.append([t.text or "" for t in root.findall(".//a:t", NS)])
        return out


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    left = slide_texts(Path(sys.argv[1]))
    right = slide_texts(Path(sys.argv[2]))
    print("left slides", len(left))
    print("right slides", len(right))
    diffs = []
    for i, (a, b) in enumerate(zip(left, right), 1):
        # Added visible slide numbers are allowed only if they already exist as text.
        if a != b:
            diffs.append(i)
            if len(diffs) <= 8:
                print("diff slide", i)
                print("left ", a[:20])
                print("right", b[:20])
    if len(left) != len(right):
        print("slide count differs")
    print("diff count", len(diffs))


if __name__ == "__main__":
    main()
