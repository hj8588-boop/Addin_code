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
    "r": "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
}

for prefix, uri in NS.items():
    ET.register_namespace(prefix, uri)


SLIDE_W = 12192000
SLIDE_H = 6858000
FONT = "Malgun Gothic"
TITLE_COLOR = "164A7A"
SUBTITLE_COLOR = "4F6F82"
TEXT_COLOR = "222222"
PAGE_COLOR = "7A8790"
ACCENT = "2F75B5"

CONTENT_TITLE = (558800, 228600, 10300000, 470000)
CONTENT_SUBTITLE_Y = 625000
PAGE_XFRM = (11290000, 6420000, 530000, 190000)

SECTION_SLIDES = {4, 7, 10, 17, 21, 23, 25, 27, 30, 32, 41}
TOC_SLIDES = {2, 3}


def slide_number(name: str) -> int:
    m = re.search(r"slide(\d+)\.xml", name)
    return int(m.group(1)) if m else 0


def q(ns: str, tag: str) -> str:
    return f"{{{NS[ns]}}}{tag}"


def text_of(sp: ET.Element) -> str:
    return " ".join(t.text.strip() for t in sp.findall(".//a:t", NS) if t.text and t.text.strip())


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


def ensure_solid_fill(sp: ET.Element, color: str) -> None:
    for rpr in sp.findall(".//a:rPr", NS):
        # Remove existing solid fill children.
        for child in list(rpr):
            if child.tag == q("a", "solidFill"):
                rpr.remove(child)
        solid = ET.SubElement(rpr, q("a", "solidFill"))
        srgb = ET.SubElement(solid, q("a", "srgbClr"))
        srgb.set("val", color)


def set_font_size(sp: ET.Element, pt: int, *, bold: bool | None = None, color: str | None = None) -> None:
    tx = sp.find("p:txBody", NS)
    if tx is None:
        return
    for rpr in tx.findall(".//a:rPr", NS):
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
    for ppr in tx.findall(".//a:pPr", NS):
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
    if color:
        ensure_solid_fill(sp, color)


def set_text_anchor(sp: ET.Element, anchor: str = "t") -> None:
    body_pr = sp.find(".//a:bodyPr", NS)
    if body_pr is not None:
        body_pr.set("anchor", anchor)


def set_paragraph_alignment(sp: ET.Element, algn: str) -> None:
    for ppr in sp.findall(".//a:pPr", NS):
        ppr.set("algn", algn)


def shapes(root: ET.Element) -> list[ET.Element]:
    return root.findall(".//p:sp", NS)


def find_zero_page_number(root: ET.Element, page_no: int) -> ET.Element | None:
    target = str(page_no)
    for sp in shapes(root):
        if text_of(sp) == target:
            vals = xfrm_values(sp)
            if vals is None or vals[2] == 0 or vals[3] == 0 or (vals[0] < 50000 and vals[1] < 50000):
                return sp
    return None


def max_shape_id(root: ET.Element) -> int:
    max_id = 1
    for c_nv_pr in root.findall(".//p:cNvPr", NS):
        try:
            max_id = max(max_id, int(c_nv_pr.get("id", "1")))
        except ValueError:
            pass
    return max_id


def add_page_number(root: ET.Element, page_no: int) -> ET.Element:
    sp_tree = root.find(".//p:spTree", NS)
    if sp_tree is None:
        raise RuntimeError("Missing spTree")
    new_id = max_shape_id(root) + 1
    sp = ET.Element(q("p", "sp"))
    nv = ET.SubElement(sp, q("p", "nvSpPr"))
    c_nv_pr = ET.SubElement(nv, q("p", "cNvPr"))
    c_nv_pr.set("id", str(new_id))
    c_nv_pr.set("name", "Slide Number")
    ET.SubElement(nv, q("p", "cNvSpPr"))
    nv_pr = ET.SubElement(nv, q("p", "nvPr"))
    ph = ET.SubElement(nv_pr, q("p", "ph"))
    ph.set("type", "sldNum")
    sppr = ET.SubElement(sp, q("p", "spPr"))
    xf = ET.SubElement(sppr, q("a", "xfrm"))
    ET.SubElement(xf, q("a", "off"), {"x": str(PAGE_XFRM[0]), "y": str(PAGE_XFRM[1])})
    ET.SubElement(xf, q("a", "ext"), {"cx": str(PAGE_XFRM[2]), "cy": str(PAGE_XFRM[3])})
    tx = ET.SubElement(sp, q("p", "txBody"))
    ET.SubElement(tx, q("a", "bodyPr"), {"anchor": "mid"})
    ET.SubElement(tx, q("a", "lstStyle"))
    p = ET.SubElement(tx, q("a", "p"))
    ppr = ET.SubElement(p, q("a", "pPr"), {"algn": "r"})
    ET.SubElement(ppr, q("a", "defRPr"), {"sz": "900"})
    rr = ET.SubElement(p, q("a", "r"))
    rpr = ET.SubElement(rr, q("a", "rPr"), {"lang": "ko-KR", "sz": "900"})
    ET.SubElement(rpr, q("a", "latin"), {"typeface": FONT})
    ET.SubElement(rpr, q("a", "ea"), {"typeface": FONT})
    solid = ET.SubElement(rpr, q("a", "solidFill"))
    ET.SubElement(solid, q("a", "srgbClr"), {"val": PAGE_COLOR})
    ET.SubElement(rr, q("a", "t")).text = str(page_no)
    sp_tree.append(sp)
    return sp


def normalize_page_number(root: ET.Element, page_no: int) -> None:
    sp = find_zero_page_number(root, page_no)
    if sp is None:
        sp = add_page_number(root, page_no)
    else:
        set_xfrm(sp, *PAGE_XFRM)
    set_font_size(sp, 9, bold=False, color=PAGE_COLOR)
    set_paragraph_alignment(sp, "r")
    set_text_anchor(sp, "mid")


def likely_title_shapes(root: ET.Element) -> list[ET.Element]:
    candidates = []
    for sp in shapes(root):
        txt = text_of(sp)
        if not txt:
            continue
        vals = xfrm_values(sp)
        if vals is None:
            continue
        x, y, w, h = vals
        candidates.append((y, x, w, h, sp))
    return [item[-1] for item in sorted(candidates, key=lambda item: (item[0], item[1]))]


def normalize_content_slide(root: ET.Element, page_no: int) -> None:
    title_candidates = []
    subtitle_candidates = []
    for sp in shapes(root):
        txt = text_of(sp)
        if not txt or txt.isdigit():
            continue
        vals = xfrm_values(sp)
        if vals is None:
            continue
        x, y, w, h = vals
        if y < 900000 and w > 2500000:
            title_candidates.append((y, x, sp))
        elif 520000 <= y < 850000 and w > 2500000:
            subtitle_candidates.append((y, x, sp))

    if title_candidates:
        title = sorted(title_candidates, key=lambda item: (item[0], item[1]))[0][2]
        set_xfrm(title, *CONTENT_TITLE)
        set_font_size(title, 27, bold=True, color=TITLE_COLOR)
        set_text_anchor(title, "mid")
    if subtitle_candidates:
        subtitle = sorted(subtitle_candidates, key=lambda item: (item[0], item[1]))[0][2]
        set_xfrm(subtitle, CONTENT_TITLE[0], CONTENT_SUBTITLE_Y, CONTENT_TITLE[2], 300000)
        set_font_size(subtitle, 13, bold=False, color=SUBTITLE_COLOR)
        set_text_anchor(subtitle, "mid")


def normalize_toc_slide(root: ET.Element, page_no: int) -> None:
    for sp in shapes(root):
        txt = text_of(sp)
        vals = xfrm_values(sp)
        if vals is None:
            continue
        if txt == "목차":
            set_xfrm(sp, 558800, 300000, 2500000, 430000)
            set_font_size(sp, 28, bold=True, color=TITLE_COLOR)
            set_text_anchor(sp, "mid")
        elif re.fullmatch(r"\d{2}\.", txt):
            set_font_size(sp, 14, bold=True, color=ACCENT)
        elif vals[0] > 1000000 and vals[1] > 900000:
            if txt.startswith("•") or "•" in txt:
                set_font_size(sp, 11, bold=False, color=SUBTITLE_COLOR)
            else:
                set_font_size(sp, 14, bold=True, color=TEXT_COLOR)


def normalize_section_slide(root: ET.Element, page_no: int) -> None:
    candidates = likely_title_shapes(root)
    number_shape = None
    title_shape = None
    desc_shape = None
    for sp in candidates:
        txt = text_of(sp)
        if txt.isdigit() and number_shape is None:
            number_shape = sp
        elif title_shape is None:
            title_shape = sp
        elif desc_shape is None:
            desc_shape = sp
            break

    if number_shape is not None:
        set_xfrm(number_shape, 760000, 2340000, 650000, 520000)
        set_font_size(number_shape, 34, bold=True, color=ACCENT)
        set_text_anchor(number_shape, "mid")
        set_paragraph_alignment(number_shape, "r")
    if title_shape is not None:
        set_xfrm(title_shape, 1550000, 2350000, 2450000, 500000)
        set_font_size(title_shape, 25, bold=True, color=TITLE_COLOR)
        set_text_anchor(title_shape, "mid")
    if desc_shape is not None:
        set_xfrm(desc_shape, 3900000, 2380000, 6500000, 620000)
        set_font_size(desc_shape, 16, bold=False, color=SUBTITLE_COLOR)
        set_text_anchor(desc_shape, "mid")


def clean_slide(data: bytes, page_no: int) -> bytes:
    root = ET.fromstring(data)
    normalize_page_number(root, page_no)
    if page_no in TOC_SLIDES:
        normalize_toc_slide(root, page_no)
    elif page_no in SECTION_SLIDES:
        normalize_section_slide(root, page_no)
    elif page_no != 1:
        normalize_content_slide(root, page_no)
    else:
        # Cover typography only; preserve cover composition.
        candidates = likely_title_shapes(root)
        if candidates:
            set_font_size(candidates[0], 34, bold=True, color=TITLE_COLOR)
        if len(candidates) > 1:
            set_font_size(candidates[1], 17, bold=False, color=SUBTITLE_COLOR)
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
