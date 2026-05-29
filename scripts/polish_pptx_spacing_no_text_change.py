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

for prefix, uri in NS.items():
    ET.register_namespace(prefix, uri)


FONT = "Malgun Gothic"
TITLE_COLOR = "164A7A"
SUBTITLE_COLOR = "4F6F82"
BODY_COLOR = "222222"
MUTED = "6D7882"

EMU = 914400
CONTENT_LEFT = 558800
CONTENT_TOP = 228600
CONTENT_WIDTH = 10300000
TITLE_H = 470000
SUBTITLE_TOP = 584200
SUBTITLE_H = 280000

SECTION_SLIDES = {4, 7, 10, 17, 21, 23, 25, 27, 30, 32, 41}
TOC_SLIDES = {2, 3}


def q(ns: str, tag: str) -> str:
    return f"{{{NS[ns]}}}{tag}"


def slide_number(name: str) -> int:
    m = re.search(r"slide(\d+)\.xml", name)
    return int(m.group(1)) if m else 0


def text_of(sp: ET.Element) -> str:
    return " ".join(t.text.strip() for t in sp.findall(".//a:t", NS) if t.text and t.text.strip())


def is_slide_number_text(text: str, slide_no: int) -> bool:
    return text == str(slide_no)


def xfrm(sp: ET.Element) -> ET.Element | None:
    return sp.find(".//a:xfrm", NS)


def xfrm_values(sp: ET.Element) -> tuple[int, int, int, int] | None:
    xf = xfrm(sp)
    if xf is None:
        return None
    off = xf.find("a:off", NS)
    ext = xf.find("a:ext", NS)
    if off is None or ext is None:
        return None
    return int(off.get("x", "0")), int(off.get("y", "0")), int(ext.get("cx", "0")), int(ext.get("cy", "0"))


def set_xfrm(sp: ET.Element, x: int, y: int, w: int, h: int) -> None:
    xf = xfrm(sp)
    if xf is None:
        sppr = sp.find("p:spPr", NS)
        if sppr is None:
            sppr = ET.SubElement(sp, q("p", "spPr"))
        xf = ET.SubElement(sppr, q("a", "xfrm"))
    off = xf.find("a:off", NS)
    if off is None:
        off = ET.SubElement(xf, q("a", "off"))
    ext = xf.find("a:ext", NS)
    if ext is None:
        ext = ET.SubElement(xf, q("a", "ext"))
    off.set("x", str(x))
    off.set("y", str(y))
    ext.set("cx", str(w))
    ext.set("cy", str(h))


def tx_body(sp: ET.Element) -> ET.Element | None:
    return sp.find("p:txBody", NS)


def body_pr(sp: ET.Element) -> ET.Element | None:
    return sp.find(".//a:bodyPr", NS)


def set_body_margins(sp: ET.Element, l: int = 76200, r: int = 76200, t: int = 38100, b: int = 38100, anchor: str | None = None) -> None:
    bp = body_pr(sp)
    if bp is None:
        return
    bp.set("lIns", str(l))
    bp.set("rIns", str(r))
    bp.set("tIns", str(t))
    bp.set("bIns", str(b))
    if anchor:
        bp.set("anchor", anchor)


def set_alignment(sp: ET.Element, algn: str) -> None:
    for ppr in sp.findall(".//a:pPr", NS):
        ppr.set("algn", algn)


def set_line_spacing(sp: ET.Element, pct: int = 110000, before: int = 0, after: int = 4000) -> None:
    for p in sp.findall(".//a:p", NS):
        ppr = p.find("a:pPr", NS)
        if ppr is None:
            ppr = ET.Element(q("a", "pPr"))
            p.insert(0, ppr)
        for child in list(ppr):
            if child.tag in {q("a", "lnSpc"), q("a", "spcBef"), q("a", "spcAft")}:
                ppr.remove(child)
        ln = ET.SubElement(ppr, q("a", "lnSpc"))
        ET.SubElement(ln, q("a", "spcPct"), {"val": str(pct)})
        if before:
            spc_bef = ET.SubElement(ppr, q("a", "spcBef"))
            ET.SubElement(spc_bef, q("a", "spcPts"), {"val": str(before)})
        if after:
            spc_aft = ET.SubElement(ppr, q("a", "spcAft"))
            ET.SubElement(spc_aft, q("a", "spcPts"), {"val": str(after)})


def set_font(sp: ET.Element, pt: int, *, bold: bool | None = None, color: str | None = None) -> None:
    tb = tx_body(sp)
    if tb is None:
        return
    for rpr in tb.findall(".//a:rPr", NS):
        rpr.set("sz", str(pt * 100))
        if bold is not None:
            rpr.set("b", "1" if bold else "0")
        latin = rpr.find("a:latin", NS)
        if latin is None:
            latin = ET.SubElement(rpr, q("a", "latin"))
        latin.set("typeface", FONT)
        ea = rpr.find("a:ea", NS)
        if ea is None:
            ea = ET.SubElement(rpr, q("a", "ea"))
        ea.set("typeface", FONT)
        if color:
            for child in list(rpr):
                if child.tag == q("a", "solidFill"):
                    rpr.remove(child)
            solid = ET.SubElement(rpr, q("a", "solidFill"))
            ET.SubElement(solid, q("a", "srgbClr"), {"val": color})
    for ppr in tb.findall(".//a:pPr", NS):
        defr = ppr.find("a:defRPr", NS)
        if defr is None:
            defr = ET.SubElement(ppr, q("a", "defRPr"))
        defr.set("sz", str(pt * 100))
        if bold is not None:
            defr.set("b", "1" if bold else "0")
        latin = defr.find("a:latin", NS)
        if latin is None:
            latin = ET.SubElement(defr, q("a", "latin"))
        latin.set("typeface", FONT)
        ea = defr.find("a:ea", NS)
        if ea is None:
            ea = ET.SubElement(defr, q("a", "ea"))
        ea.set("typeface", FONT)


def shapes(root: ET.Element) -> list[ET.Element]:
    return root.findall(".//p:sp", NS)


def normalize_content_layout(root: ET.Element, slide_no: int) -> None:
    title_done = False
    subtitle_done = False
    for sp in sorted(shapes(root), key=lambda s: xfrm_values(s) or (0, 0, 0, 0)):
        text = text_of(sp)
        vals = xfrm_values(sp)
        if not text or vals is None or is_slide_number_text(text, slide_no):
            continue
        x, y, w, h = vals

        if not title_done and y < 520000 and w > 3000000:
            set_xfrm(sp, CONTENT_LEFT, CONTENT_TOP, CONTENT_WIDTH, TITLE_H)
            set_font(sp, 27, bold=True, color=TITLE_COLOR)
            set_body_margins(sp, 0, 0, 0, 0, "mid")
            set_line_spacing(sp, 100000, after=0)
            title_done = True
            continue

        if not subtitle_done and 520000 <= y < 900000 and w > 3000000:
            set_xfrm(sp, CONTENT_LEFT, SUBTITLE_TOP, min(w, CONTENT_WIDTH), SUBTITLE_H)
            set_font(sp, 13, bold=False, color=SUBTITLE_COLOR)
            set_body_margins(sp, 0, 0, 0, 0, "mid")
            set_line_spacing(sp, 105000, after=0)
            subtitle_done = True
            continue

        # Step number markers and compact labels.
        if text.isdigit() and len(text) <= 2 and 900000 < y < 5900000 and w < 550000:
            set_xfrm(sp, x, y, 335000, 335000)
            set_font(sp, 18, bold=True)
            set_body_margins(sp, 0, 0, 0, 0, "mid")
            set_alignment(sp, "ctr")
            continue

        # Body typography: keep content intact, only tune size and breathing room.
        compact_label = h <= 360000 and len(text) <= 35
        long_text = len(text) >= 95 or h >= 900000
        medium_text = len(text) >= 45
        if compact_label:
            set_font(sp, 14, bold=True, color=BODY_COLOR)
            set_line_spacing(sp, 105000, after=1000)
        elif long_text:
            set_font(sp, 12, bold=False, color=BODY_COLOR)
            set_line_spacing(sp, 108000, after=3000)
        elif medium_text:
            set_font(sp, 13, bold=False, color=BODY_COLOR)
            set_line_spacing(sp, 110000, after=3000)
        else:
            set_font(sp, 14, bold=False, color=BODY_COLOR)
            set_line_spacing(sp, 110000, after=3000)
        set_body_margins(sp, 65000, 65000, 35000, 35000)


def normalize_toc_layout(root: ET.Element, slide_no: int) -> None:
    for sp in shapes(root):
        text = text_of(sp)
        vals = xfrm_values(sp)
        if not text or vals is None or is_slide_number_text(text, slide_no):
            continue
        x, y, w, h = vals
        if text == "목차":
            set_xfrm(sp, CONTENT_LEFT, 300000, 2500000, 430000)
            set_font(sp, 28, bold=True, color=TITLE_COLOR)
            set_body_margins(sp, 0, 0, 0, 0, "mid")
        elif re.fullmatch(r"\d{2}\.", text):
            set_font(sp, 14, bold=True, color="2F75B5")
            set_body_margins(sp, 0, 0, 0, 0, "mid")
            set_alignment(sp, "r")
        elif y > 900000:
            if text.startswith("•") or "•" in text:
                set_font(sp, 11, bold=False, color=SUBTITLE_COLOR)
                set_line_spacing(sp, 105000, after=1000)
            else:
                set_font(sp, 14, bold=True, color=BODY_COLOR)
                set_line_spacing(sp, 105000, after=1000)
            set_body_margins(sp, 20000, 20000, 10000, 10000)


def normalize_section_layout(root: ET.Element, slide_no: int) -> None:
    items = []
    for sp in shapes(root):
        text = text_of(sp)
        vals = xfrm_values(sp)
        if not text or vals is None or is_slide_number_text(text, slide_no):
            continue
        items.append((vals[1], vals[0], sp, text))
    items.sort()

    number_sp = None
    title_sp = None
    desc_sp = None
    for _, _, sp, text in items:
        if text.isdigit() and number_sp is None:
            number_sp = sp
        elif title_sp is None:
            title_sp = sp
        elif desc_sp is None:
            desc_sp = sp
            break

    if number_sp is not None:
        set_xfrm(number_sp, 760000, 2340000, 650000, 520000)
        set_font(number_sp, 34, bold=True, color="2F75B5")
        set_body_margins(number_sp, 0, 0, 0, 0, "mid")
        set_alignment(number_sp, "r")
    if title_sp is not None:
        set_xfrm(title_sp, 1550000, 2350000, 2650000, 500000)
        set_font(title_sp, 25, bold=True, color=TITLE_COLOR)
        set_body_margins(title_sp, 0, 0, 0, 0, "mid")
    if desc_sp is not None:
        set_xfrm(desc_sp, 4100000, 2380000, 6300000, 620000)
        set_font(desc_sp, 16, bold=False, color=SUBTITLE_COLOR)
        set_body_margins(desc_sp, 0, 0, 0, 0, "mid")


def normalize_page_number(root: ET.Element, slide_no: int) -> None:
    for sp in shapes(root):
        text = text_of(sp)
        vals = xfrm_values(sp)
        if text == str(slide_no) and vals is not None:
            x, y, w, h = vals
            if x > 11000000 or (x < 100000 and y < 100000) or w == 0 or h == 0:
                set_xfrm(sp, 11290000, 6420000, 530000, 190000)
                set_font(sp, 9, bold=False, color=MUTED)
                set_body_margins(sp, 0, 0, 0, 0, "mid")
                set_alignment(sp, "r")
                return


def clean_slide(data: bytes, slide_no: int) -> bytes:
    root = ET.fromstring(data)
    normalize_page_number(root, slide_no)
    if slide_no in TOC_SLIDES:
        normalize_toc_layout(root, slide_no)
    elif slide_no in SECTION_SLIDES:
        normalize_section_layout(root, slide_no)
    elif slide_no != 1:
        normalize_content_layout(root, slide_no)
    else:
        # Keep cover content and layout; only make title family consistent.
        for sp in shapes(root):
            text = text_of(sp)
            vals = xfrm_values(sp)
            if not text or vals is None or is_slide_number_text(text, slide_no):
                continue
            x, y, w, h = vals
            if y < 2600000:
                set_font(sp, 34, bold=True, color=TITLE_COLOR)
            else:
                set_font(sp, 16, bold=False, color=SUBTITLE_COLOR)
            set_body_margins(sp, 0, 0, 0, 0)
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    with tempfile.NamedTemporaryFile(delete=False, suffix=".pptx") as tmp:
        tmp_path = Path(tmp.name)
    try:
        with ZipFile(src, "r") as zin, ZipFile(tmp_path, "w", ZIP_DEFLATED) as zout:
            for item in zin.infolist():
                data = zin.read(item.filename)
                if item.filename.startswith("ppt/slides/slide") and item.filename.endswith(".xml"):
                    data = clean_slide(data, slide_number(item.filename))
                zout.writestr(item, data)
        shutil.move(str(tmp_path), dst)
    finally:
        if tmp_path.exists():
            tmp_path.unlink()
    print(dst.resolve())


if __name__ == "__main__":
    main()
