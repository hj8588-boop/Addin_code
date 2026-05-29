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


def text_of(sp: ET.Element) -> str:
    parts = []
    for t in sp.findall(".//a:t", NS):
        if t.text:
            parts.append(t.text.strip())
    return " ".join([p for p in parts if p])


def xfrm_of(sp: ET.Element):
    xfrm = sp.find(".//a:xfrm", NS)
    if xfrm is None:
        return None
    off = xfrm.find("a:off", NS)
    ext = xfrm.find("a:ext", NS)
    if off is None or ext is None:
        return None
    return tuple(int(off.attrib.get(k, 0)) for k in ("x", "y")) + tuple(int(ext.attrib.get(k, 0)) for k in ("cx", "cy"))


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    path = Path(sys.argv[1])
    with ZipFile(path) as z:
        slides = sorted([n for n in z.namelist() if n.startswith("ppt/slides/slide") and n.endswith(".xml")], key=slide_number)
        for i, name in enumerate(slides, 1):
            root = ET.fromstring(z.read(name))
            print(f"\n--- slide {i:02d} ---")
            shapes = []
            for sp in root.findall(".//p:sp", NS):
                text = text_of(sp)
                if not text:
                    continue
                xfrm = xfrm_of(sp)
                shapes.append((xfrm or (0, 0, 0, 0), text[:120]))
            for (x, y, cx, cy), text in sorted(shapes, key=lambda item: (item[0][1], item[0][0]))[:15]:
                print(f"x={x:8d} y={y:8d} w={cx:8d} h={cy:8d} :: {text}")


if __name__ == "__main__":
    main()
