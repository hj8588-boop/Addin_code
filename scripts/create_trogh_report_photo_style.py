from __future__ import annotations

import html
import zipfile
from pathlib import Path


OUT = Path("#트로프 자동배치/AI활용_Dynamo_트로프자동배치_보고서.docx")


def esc(value: object) -> str:
    return html.escape(str(value), quote=True)


def r(text: str, *, bold: bool = False, color: str | None = None, size: int | None = None) -> str:
    props = [
        '<w:rFonts w:ascii="Malgun Gothic" w:eastAsia="Malgun Gothic" w:hAnsi="Malgun Gothic" w:cs="Malgun Gothic"/>'
    ]
    if bold:
        props.append("<w:b/><w:bCs/>")
    if color:
        props.append(f'<w:color w:val="{color}"/>')
    if size:
        props.append(f'<w:sz w:val="{size}"/><w:szCs w:val="{size}"/>')
    preserve = ' xml:space="preserve"' if text[:1].isspace() or text[-1:].isspace() else ""
    return f"<w:r><w:rPr>{''.join(props)}</w:rPr><w:t{preserve}>{esc(text)}</w:t></w:r>"


def p(
    text: str = "",
    *,
    style: str | None = None,
    bold: bool = False,
    color: str | None = None,
    size: int | None = None,
    before: int | None = None,
    after: int | None = None,
) -> str:
    props = []
    if style:
        props.append(f'<w:pStyle w:val="{style}"/>')
    if before is not None or after is not None:
        attrs = []
        if before is not None:
            attrs.append(f'w:before="{before}"')
        if after is not None:
            attrs.append(f'w:after="{after}"')
        props.append(f"<w:spacing {' '.join(attrs)} w:line=\"300\" w:lineRule=\"auto\"/>")
    ppr = f"<w:pPr>{''.join(props)}</w:pPr>" if props else ""
    return f"<w:p>{ppr}{r(text, bold=bold, color=color, size=size)}</w:p>"


def section_heading(text: str) -> str:
    return (
        '<w:p><w:pPr><w:pStyle w:val="SectionHeading"/>'
        '<w:pBdr><w:left w:val="single" w:sz="18" w:space="8" w:color="1A73E8"/></w:pBdr>'
        '<w:spacing w:before="520" w:after="360"/></w:pPr>'
        f'{r(text, bold=True, size=27)}</w:p>'
    )


def rich_paragraph(parts: list[tuple[str, bool]]) -> str:
    runs = "".join(r(text, bold=bold) for text, bold in parts)
    return f'<w:p><w:pPr><w:spacing w:after="150" w:line="310" w:lineRule="auto"/></w:pPr>{runs}</w:p>'


def callout(title: str, body: str) -> str:
    return (
        '<w:tbl><w:tblPr><w:tblW w:w="9300" w:type="dxa"/>'
        '<w:tblBorders><w:top w:val="single" w:sz="8" w:color="8EBBF2"/>'
        '<w:left w:val="single" w:sz="14" w:color="1A73E8"/>'
        '<w:bottom w:val="single" w:sz="8" w:color="8EBBF2"/>'
        '<w:right w:val="single" w:sz="8" w:color="8EBBF2"/>'
        '</w:tblBorders></w:tblPr><w:tr><w:tc>'
        '<w:tcPr><w:tcW w:w="9300" w:type="dxa"/><w:shd w:fill="EDF5FF"/>'
        '<w:tcMar><w:top w:w="190" w:type="dxa"/><w:left w:w="210" w:type="dxa"/>'
        '<w:bottom w:w="190" w:type="dxa"/><w:right w:w="210" w:type="dxa"/></w:tcMar></w:tcPr>'
        f'{p(title, bold=True, color="1264C8", after=80)}{p(body, after=60)}'
        '</w:tc></w:tr></w:tbl>'
    )


def page_break() -> str:
    return '<w:p><w:r><w:br w:type="page"/></w:r></w:p>'


def document_xml() -> str:
    body: list[str] = []
    body.append(p("AI 활용 Dynamo 자동화 개발 보고서", bold=True, color="1F4E79", size=32, after=120))
    body.append(p("주제: DWG/Revit Edge 기반 트로프 패밀리 자동 배치", color="4F81BD", size=22, after=420))

    body.append(section_heading("1. 작업한 계기"))
    body.append(p(
        "철도 전기·통신 BIM 모델링 업무에서는 트로프와 같이 선형을 따라 반복 배치되는 객체가 많습니다."
    ))
    body.append(rich_paragraph([
        ("기존에는 CAD 도면 또는 Revit 모델의 기준선을 보면서 ", False),
        ("수작업으로 간격을 확인하고", True),
        (" 패밀리를 하나씩 배치한 뒤 방향과 위치를 보정하고 있었습니다.", False),
    ]))
    body.append(p(
        "특히 DWG 도면의 선분을 기준으로 트로프를 배치해야 하는 경우, 기준선 선택, 간격 산정, 패밀리 삽입, 회전 방향 조정, 오프셋 보정이 반복되어 오류 발생 가능성이 높고 작업 시간이 과도하게 소요되었습니다."
    ))
    body.append(callout(
        "해결 방향",
        "DWG 또는 Revit edge를 직접 선택하면 기준선과 보조선을 자동 분석하고, 지정한 간격·오프셋·회전 방식에 따라 트로프 패밀리를 자동 배치하는 Dynamo Python 스크립트를 개발하였습니다.",
    ))

    body.append(section_heading("2. 수행 방법"))
    body.append(p(
        "Dynamo 내부에 Python Script 노드를 구성하고 Revit API를 활용하여 선택한 edge를 curve 데이터로 변환한 뒤, 연속 선형을 계산하는 방식으로 개발하였습니다."
    ))
    methods = [
        ("1) EDGE 선택 모드", "사용자가 main edge와 side edge를 순서대로 선택하면 curve로 변환 가능한 참조만 필터링하여 처리"),
        ("2) 선형 데이터 분석", "DWG ImportInstance, Revit Curve, PolyLine을 실제 Revit 좌표계로 변환하고 끝점 허용오차를 기준으로 연속 구간 구성"),
        ("3) 배치점 자동 계산", "패밀리 설치 간격, Y_offset, X_offset 값을 적용하여 기준선 위의 배치 위치와 보정 위치를 계산"),
        ("4) 방향 및 회전 적용", "main edge의 접선 방향과 side edge 방향을 이용해 폭 방향 벡터를 산정하고, 0도·90도·180도 회전 모드를 선택 적용"),
        ("5) 패밀리 자동 생성", "선택한 Family Type을 활성화한 후 지정 위치로 이동시키고 Z축 기준 회전만 수행하여 불필요한 3차원 기울어짐을 방지"),
    ]
    for title, desc in methods:
        body.append(rich_paragraph([(title, True), (" - " + desc, False)]))

    body.append(section_heading("3. AI 활용 내용"))
    body.append(p(
        "AI는 단순 문장 작성이 아니라 Dynamo 자동화 로직을 기획하고 Python/Revit API 코드로 구체화하는 과정에 활용되었습니다."
    ))
    ai_items = [
        ("1) 업무 절차 분석", "수작업으로 수행하던 트로프 배치 절차를 입력값, 선택 절차, 계산 로직, 출력 결과로 분해"),
        ("2) Python 코드 작성 보조", "Revit API의 FamilyInstance 생성, ElementTransformUtils 이동·회전, ImportInstance 좌표 변환 로직 작성"),
        ("3) 오류 원인 검토", "DWG edge 선택 불가, PolyLine 변환, 패밀리 방향 불일치, Z축 외 회전 발생 가능성을 검토하고 보완"),
        ("4) 사용자 입력 구조 개선", "Dynamo Player에서 사용할 수 있도록 패밀리 설치 간격, 패밀리 타입, 회전방식, 재실행 버튼, X/Y 오프셋 입력값을 정리"),
    ]
    for title, desc in ai_items:
        body.append(rich_paragraph([(title, True), (" - " + desc, False)]))

    body.append(callout(
        "AI 활용 효과",
        "Revit API와 Dynamo Python의 복잡한 코드 구조를 빠르게 작성하고, 좌표 변환·방향 벡터·패밀리 회전과 같은 오류 가능성이 높은 부분을 반복 검토하여 개발 시간을 단축하였습니다.",
    ))

    body.append(section_heading("4. 주요 입력값 및 기능"))
    feature_rows = [
        ("패밀리 설치 간격", "트로프를 반복 배치할 간격을 mm 단위로 입력"),
        ("배치할 패밀리 타입", "프로젝트 내 트로프 Family Type 선택"),
        ("회전방식", "0=기준선 방향, 1=90도 방향, 2=180도 방향으로 배치 방향 제어"),
        ("재실행 버튼", "기준 edge를 다시 선택하여 배치 기준 갱신"),
        ("X_offset", "선택 edge와 패밀리 중심선 사이의 수직 보정값"),
        ("Y_offset", "edge 진행 방향 기준 시작 위치 보정값"),
    ]
    for title, desc in feature_rows:
        body.append(rich_paragraph([(title, True), (" : " + desc, False)]))

    body.append(section_heading("5. 적용 효과"))
    effects = [
        "트로프 패밀리 반복 배치 시간을 단축하고 수작업 입력을 줄일 수 있습니다.",
        "기준선 기반 자동 배치로 객체 위치 편차와 누락 가능성을 줄일 수 있습니다.",
        "DWG 도면의 선형 정보를 BIM 모델 배치 기준으로 직접 활용할 수 있습니다.",
        "간격, 오프셋, 회전방식을 입력값으로 관리하여 설계 변경 시 재작업 대응이 빠릅니다.",
        "AI를 활용한 자동화 개발 사례로, 향후 관로·케이블트레이·지지대 등 다른 선형 기반 객체에도 확장 가능합니다.",
    ]
    for effect in effects:
        body.append(rich_paragraph([("· ", True), (effect, False)]))

    body.append(section_heading("6. 보완 및 개선 방향"))
    improvements = [
        ("배치 결과 검증", "생성 수량, 누락 구간, 총 배치 길이를 OUT 또는 별도 표로 출력"),
        ("오류 메시지 개선", "패밀리 생성 실패 또는 edge 선택 오류 발생 시 원인을 사용자에게 표시"),
        ("사용자 안내 강화", "Dynamo 그룹 주석과 Player 입력 설명을 보강하여 선택 순서 혼동 방지"),
        ("적용 대상 확대", "트로프 외 선형 기반 시설물 패밀리 자동 배치 기능으로 확장"),
    ]
    for title, desc in improvements:
        body.append(rich_paragraph([(title, True), (" - " + desc, False)]))

    body.append(section_heading("7. 결론"))
    body.append(p(
        "본 Dynamo는 AI를 활용하여 트로프 패밀리 자동 배치 업무를 구현한 사례입니다. 선택한 DWG/Revit edge를 기준으로 선형을 분석하고, 설치 간격과 오프셋, 회전 조건을 반영하여 패밀리를 자동 생성함으로써 반복 작업을 줄이고 BIM 모델링 품질을 높일 수 있습니다."
    ))
    body.append(p(
        "향후 결과 검증 기능과 오류 로그 기능을 추가하면 실제 프로젝트에서의 활용성과 유지관리성이 더욱 높아질 것으로 판단됩니다."
    ))

    section = (
        '<w:sectPr><w:pgSz w:w="11906" w:h="16838"/>'
        '<w:pgMar w:top="1150" w:right="1200" w:bottom="1150" w:left="1200" w:header="720" w:footer="720" w:gutter="0"/>'
        '</w:sectPr>'
    )
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
        f'<w:body>{"".join(body)}{section}</w:body></w:document>'
    )


STYLES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults>
<w:rPrDefault><w:rPr><w:rFonts w:ascii="Malgun Gothic" w:eastAsia="Malgun Gothic" w:hAnsi="Malgun Gothic" w:cs="Malgun Gothic"/><w:sz w:val="21"/><w:szCs w:val="21"/></w:rPr></w:rPrDefault>
<w:pPrDefault><w:pPr><w:spacing w:after="150" w:line="300" w:lineRule="auto"/></w:pPr></w:pPrDefault>
</w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
<w:style w:type="paragraph" w:styleId="SectionHeading"><w:name w:val="Section Heading"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/></w:pPr><w:rPr><w:b/><w:color w:val="000000"/><w:sz w:val="27"/><w:szCs w:val="27"/></w:rPr></w:style>
</w:styles>'''


PACKAGE_FILES = {
    "[Content_Types].xml": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
<Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>''',
    "_rels/.rels": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>''',
    "word/_rels/document.xml.rels": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
</Relationships>''',
    "word/styles.xml": STYLES,
    "word/settings.xml": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:zoom w:percent="100"/></w:settings>''',
    "docProps/core.xml": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
<dc:title>AI 활용 Dynamo 트로프 자동배치 보고서</dc:title><dc:creator>Codex</dc:creator></cp:coreProperties>''',
    "docProps/app.xml": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Application>Codex</Application></Properties>''',
}


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as docx:
        for name, value in PACKAGE_FILES.items():
            docx.writestr(name, value)
        docx.writestr("word/document.xml", document_xml())
    print(OUT.resolve())


if __name__ == "__main__":
    main()
