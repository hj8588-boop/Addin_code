from __future__ import annotations

import html
import zipfile
from datetime import date
from pathlib import Path


OUT = Path("#트로프 자동배치/AI활용_Dynamo_트로프자동배치_보고서.docx")


def esc(text: object) -> str:
    return html.escape(str(text), quote=True)


def r(text: str, *, bold: bool = False, color: str | None = None, size: int | None = None) -> str:
    props = []
    if bold:
        props.append("<w:b/>")
    if color:
        props.append(f'<w:color w:val="{color}"/>')
    if size:
        props.append(f'<w:sz w:val="{size}"/><w:szCs w:val="{size}"/>')
    rpr = f"<w:rPr>{''.join(props)}</w:rPr>" if props else ""
    preserve = ' xml:space="preserve"' if text[:1].isspace() or text[-1:].isspace() else ""
    return f"<w:r>{rpr}<w:t{preserve}>{esc(text)}</w:t></w:r>"


def p(text: str = "", style: str | None = None, *, bold: bool = False, color: str | None = None) -> str:
    ppr = f'<w:pPr><w:pStyle w:val="{style}"/></w:pPr>' if style else ""
    return f"<w:p>{ppr}{r(text, bold=bold, color=color)}</w:p>"


def bullet(text: str) -> str:
    return f'<w:p><w:pPr><w:pStyle w:val="ListBullet"/></w:pPr>{r(text)}</w:p>'


def page_break() -> str:
    return '<w:p><w:r><w:br w:type="page"/></w:r></w:p>'


def cell(text: str, width: int, *, shade: str | None = None, bold: bool = False) -> str:
    shd = f'<w:shd w:fill="{shade}"/>' if shade else ""
    return (
        f'<w:tc><w:tcPr><w:tcW w:w="{width}" w:type="dxa"/>{shd}'
        '<w:tcMar><w:top w:w="120" w:type="dxa"/><w:left w:w="120" w:type="dxa"/>'
        '<w:bottom w:w="120" w:type="dxa"/><w:right w:w="120" w:type="dxa"/></w:tcMar>'
        f'</w:tcPr>{p(text, bold=bold)}</w:tc>'
    )


def table(rows: list[list[str]], widths: list[int]) -> str:
    out = [
        '<w:tbl><w:tblPr><w:tblStyle w:val="TableGrid"/>'
        '<w:tblW w:w="0" w:type="auto"/><w:tblLook w:firstRow="1" w:noHBand="0" w:noVBand="1"/>'
        '</w:tblPr>'
    ]
    for i, row in enumerate(rows):
        out.append("<w:tr>")
        for j, value in enumerate(row):
            out.append(cell(value, widths[j], shade="DDEBF7" if i == 0 else None, bold=i == 0))
        out.append("</w:tr>")
    out.append("</w:tbl>")
    return "".join(out)


def callout(title: str, body: str) -> str:
    return (
        '<w:tbl><w:tblPr><w:tblW w:w="9000" w:type="dxa"/><w:tblBorders>'
        '<w:top w:val="single" w:sz="8" w:color="5B9BD5"/>'
        '<w:left w:val="single" w:sz="8" w:color="5B9BD5"/>'
        '<w:bottom w:val="single" w:sz="8" w:color="5B9BD5"/>'
        '<w:right w:val="single" w:sz="8" w:color="5B9BD5"/>'
        '</w:tblBorders></w:tblPr><w:tr>'
        '<w:tc><w:tcPr><w:tcW w:w="9000" w:type="dxa"/><w:shd w:fill="EAF3F8"/>'
        '<w:tcMar><w:top w:w="180" w:type="dxa"/><w:left w:w="180" w:type="dxa"/>'
        '<w:bottom w:w="180" w:type="dxa"/><w:right w:w="180" w:type="dxa"/></w:tcMar></w:tcPr>'
        f'{p(title, "Heading3")}{p(body)}</w:tc></w:tr></w:tbl>'
    )


def document_xml() -> str:
    today = date.today().strftime("%Y.%m.%d.")
    parts: list[str] = []
    parts.append(p("AI 활용 Dynamo 자동화 개발 보고서", "Title"))
    parts.append(p("DWG/Revit Edge 기반 트로프 패밀리 자동 배치", "Subtitle"))
    parts.append(p(f"작성일: {today}"))
    parts.append(p("대상 파일: 최종@final_select edge_fit_family_nrbscurve_dwg_position_rotate_zonly.dyn"))
    parts.append(callout(
        "보고서 개요",
        "본 보고서는 AI를 활용하여 Revit Dynamo 기반의 트로프 자동 배치 그래프를 기획, 구현, 검토한 과정을 정리한 문서이다. "
        "대상 Dynamo는 사용자가 선택한 DWG 또는 Revit edge를 기준선으로 해석하고, 지정한 간격과 오프셋에 따라 콘크리트 트로프 패밀리를 자동 배치한다.",
    ))
    parts.append(page_break())

    parts.append(p("1. 개발 배경", "Heading1"))
    parts.append(p(
        "철도 전기·통신 BIM 모델링 업무에서는 선형을 따라 반복 배치되는 트로프, 관로, 지지물 등의 객체가 많다. "
        "기존 수작업 방식은 기준선 확인, 간격 산정, 패밀리 삽입, 회전 방향 조정, 위치 보정이 반복되어 작업 시간이 길고 배치 품질이 작업자 숙련도에 영향을 받는다."
    ))
    parts.append(p(
        "본 Dynamo는 이러한 반복 작업을 자동화하기 위해 작성되었다. 특히 CAD 도면에서 가져온 DWG edge와 Revit 모델 edge를 모두 선택 대상으로 허용하여, "
        "설계 도면의 선형 정보를 BIM 객체 배치 기준으로 직접 활용할 수 있도록 구성하였다."
    ))
    parts.append(p("2. 개발 목표", "Heading1"))
    for item in [
        "선택한 main edge를 따라 트로프 패밀리를 일정 간격으로 자동 배치한다.",
        "side edge를 참조하여 패밀리의 폭 방향과 회전 방향을 안정적으로 계산한다.",
        "DWG 선분, Revit curve, PolyLine 등 다양한 선형 객체를 처리한다.",
        "X/Y 오프셋과 회전 방식을 입력값으로 제공하여 현장별 조건에 대응한다.",
        "AI를 활용해 Python/Revit API 로직을 빠르게 작성하고 오류 원인을 분석한다.",
    ]:
        parts.append(bullet(item))

    parts.append(p("3. Dynamo 구성", "Heading1"))
    parts.append(table([
        ["구분", "노드/입력값", "설명"],
        ["입력", "패밀리 설치 간격 = 500", "트로프 패밀리의 반복 배치 간격을 mm 단위로 지정한다."],
        ["입력", "배치할 패밀리 타입", "Revit 프로젝트 내 Family Type 중 배치 대상 트로프를 선택한다."],
        ["입력", "회전방식 0/1/2", "패밀리 방향을 기준선 방향, 폭 방향, 반대 방향 중 선택한다."],
        ["입력", "재실행 버튼", "True일 때 edge를 다시 선택하여 기준선을 갱신한다."],
        ["입력", "X_offset", "선택 edge와 패밀리 중심선 간 수직 보정값을 지정한다."],
        ["입력", "Y_offset", "edge 진행 방향의 시작 위치 보정값을 지정한다."],
        ["처리", "Python Script", "edge 선택, curve 변환, 선형 연결, 간격 계산, 패밀리 생성 및 회전을 수행한다."],
        ["출력", "OUT", "생성된 FamilyInstance 목록을 반환한다."],
    ], [1800, 2700, 4500]))

    parts.append(p("4. 주요 구현 로직", "Heading1"))
    parts.append(p("4.1 Edge 선택 및 Curve 추출", "Heading2"))
    parts.append(p(
        "사용자는 먼저 main edge를 선택하고 이후 side edge를 선택한다. Python 노드 내부의 선택 필터는 curve로 변환 가능한 참조만 허용한다. "
        "ImportInstance의 경우 GetTotalTransform을 적용하여 DWG 내부 좌표를 Revit 실제 좌표계로 변환한다."
    ))
    parts.append(p("4.2 선분 연결 및 구간 생성", "Heading2"))
    parts.append(p(
        "선택된 curve들은 끝점 거리 허용오차를 기준으로 하나의 연속 run으로 연결된다. 방향이 반대인 선분은 reverse 처리하여 진행 방향을 통일한다. "
        "이를 통해 여러 개의 짧은 CAD 선분도 하나의 배치 기준선처럼 사용할 수 있다."
    ))
    parts.append(p("4.3 Main/Side Edge 매칭", "Heading2"))
    parts.append(p(
        "main run과 side run은 전체 길이 차이가 가장 작은 조합으로 매칭된다. 이후 main edge 위의 배치점에서 side edge로 투영한 점을 찾고, "
        "두 점 사이의 방향을 폭 방향 벡터로 계산한다."
    ))
    parts.append(p("4.4 패밀리 배치 및 Z축 회전", "Heading2"))
    parts.append(p(
        "배치점은 설치 간격과 Y_offset에 따라 산정되며, X_offset은 폭 방향으로 적용된다. 패밀리는 기준점에 생성된 후 목표 위치로 이동하고, "
        "HandOrientation 또는 FacingOrientation을 기준으로 Z축 회전만 수행한다. 이 방식은 경사나 Z값 변화에 의한 불필요한 3차원 회전을 방지한다."
    ))

    parts.append(p("5. AI 활용 내용", "Heading1"))
    parts.append(table([
        ["활용 단계", "AI 활용 방식", "성과"],
        ["문제 정의", "수작업 배치 절차를 자동화 가능한 단위로 분해", "입력값, 선택 순서, 예외 조건을 명확히 정리"],
        ["코드 작성", "Revit API, Dynamo Python, 좌표 변환 로직 초안 생성", "반복 작성 시간이 줄고 구현 속도 향상"],
        ["오류 해결", "DWG ImportInstance 좌표 변환, PolyLine 처리, 회전 방향 오류 분석", "다양한 edge 형식에 대응하는 안정성 확보"],
        ["사용성 개선", "재실행 버튼, 오프셋, 회전 모드 등 사용자 입력 구조 제안", "Dynamo Player에서 조작하기 쉬운 형태로 개선"],
        ["문서화", "로직 설명과 보고서 구조 정리", "개발 과정과 적용 효과를 공유 가능한 산출물로 정리"],
    ], [1700, 4200, 3100]))

    parts.append(p("6. 업무 적용 효과", "Heading1"))
    for item in [
        "반복 패밀리 배치 시간을 단축하고 모델링 작업자의 피로도를 줄인다.",
        "기준선 기반 배치로 위치 편차를 줄이고 배치 품질을 일정하게 유지한다.",
        "DWG 선형을 직접 활용하므로 2D 설계 정보와 BIM 모델링 사이의 재입력 작업을 줄인다.",
        "간격, 오프셋, 회전값을 입력값으로 관리하여 설계 변경 시 재작업 대응이 빠르다.",
        "AI를 활용한 코드 작성과 검토로 Dynamo 개발 경험이 적은 사용자도 고급 자동화 로직을 구현할 수 있다.",
    ]:
        parts.append(bullet(item))

    parts.append(p("7. 한계 및 개선 방향", "Heading1"))
    parts.append(table([
        ["항목", "현재 한계", "개선 방향"],
        ["예외 처리", "패밀리 생성 실패 시 일부 오류가 조용히 넘어갈 수 있음", "실패 위치와 원인을 OUT 또는 로그로 반환"],
        ["검증", "배치 결과를 사용자가 모델에서 직접 확인해야 함", "배치 수량, 누락 구간, 총 길이 검토표 자동 생성"],
        ["사용성", "edge 선택 순서를 사용자가 기억해야 함", "Dynamo Player 설명 문구와 그룹 주석 보강"],
        ["확장성", "현재는 트로프 패밀리 배치에 초점", "케이블 트레이, 관로, 지지대 등 선형 기반 객체로 확장"],
    ], [1800, 3700, 3500]))

    parts.append(p("8. 결론", "Heading1"))
    parts.append(p(
        "본 Dynamo는 AI를 활용하여 반복적인 트로프 패밀리 배치 업무를 자동화한 사례이다. "
        "사용자가 선택한 DWG/Revit edge를 기준으로 선형을 해석하고, 간격·오프셋·회전 조건을 반영하여 패밀리를 자동 생성함으로써 "
        "작업 효율성과 모델 품질을 동시에 높일 수 있다."
    ))
    parts.append(p(
        "특히 AI는 단순한 코드 작성 보조를 넘어, 업무 절차를 자동화 로직으로 변환하고 Revit API의 좌표·회전·선택 처리 문제를 해결하는 데 활용되었다. "
        "향후 결과 검증 기능과 로그 기능을 추가하면 실제 프로젝트 적용성과 유지관리성이 더 높아질 것으로 판단된다."
    ))

    body = "".join(parts)
    sect = (
        '<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" '
        'w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>'
    )
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
        f"<w:body>{body}{sect}</w:body></w:document>"
    )


STYLES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Malgun Gothic" w:eastAsia="Malgun Gothic" w:hAnsi="Malgun Gothic"/><w:sz w:val="21"/><w:szCs w:val="21"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="140" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:before="360" w:after="280"/></w:pPr><w:rPr><w:b/><w:color w:val="1F4E79"/><w:sz w:val="40"/><w:szCs w:val="40"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:after="420"/></w:pPr><w:rPr><w:color w:val="5B9BD5"/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="320" w:after="160"/></w:pPr><w:rPr><w:b/><w:color w:val="1F4E79"/><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="220" w:after="100"/></w:pPr><w:rPr><w:b/><w:color w:val="2F75B5"/><w:sz w:val="23"/><w:szCs w:val="23"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:after="80"/></w:pPr><w:rPr><w:b/><w:color w:val="1F4E79"/><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="ListBullet"><w:name w:val="List Bullet"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="360" w:hanging="180"/></w:pPr></w:style>
<w:style w:type="table" w:styleId="TableGrid"><w:name w:val="Table Grid"/><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:color="BFBFBF"/><w:left w:val="single" w:sz="4" w:color="BFBFBF"/><w:bottom w:val="single" w:sz="4" w:color="BFBFBF"/><w:right w:val="single" w:sz="4" w:color="BFBFBF"/><w:insideH w:val="single" w:sz="4" w:color="D9D9D9"/><w:insideV w:val="single" w:sz="4" w:color="D9D9D9"/></w:tblBorders></w:tblPr></w:style>
</w:styles>'''


FILES = {
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
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
<dc:title>AI 활용 Dynamo 트로프 자동배치 보고서</dc:title><dc:creator>Codex</dc:creator><cp:lastModifiedBy>Codex</cp:lastModifiedBy></cp:coreProperties>''',
    "docProps/app.xml": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Application>Codex</Application></Properties>''',
}


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as docx:
        for name, content in FILES.items():
            docx.writestr(name, content)
        docx.writestr("word/document.xml", document_xml())
    print(OUT.resolve())


if __name__ == "__main__":
    main()
