from __future__ import annotations

import html
import zipfile
from pathlib import Path


DOCX_OUT = Path("#트로프 자동배치/AI활용_Dynamo_트로프자동배치_보고서_카페글용.docx")
TXT_OUT = Path("#트로프 자동배치/AI활용_Dynamo_트로프자동배치_카페글.txt")


TITLE = "AI를 활용한 Dynamo 트로프 자동 배치 작업 사례"
SUBTITLE = "DWG/Revit Edge 기준으로 트로프 패밀리를 자동 배치하는 Dynamo 개발"


SECTIONS = [
    (
        "1. 작업한 계기",
        [
            "철도 전기·통신 BIM 모델링 업무에서는 트로프처럼 선형을 따라 반복 배치되는 객체가 자주 발생합니다.",
            "기존에는 CAD 도면이나 Revit 모델의 기준선을 확인하면서 트로프 패밀리를 일정 간격으로 직접 배치하고, 이후 방향과 위치를 하나씩 보정해야 했습니다.",
            "특히 DWG 도면의 선분을 기준으로 작업할 경우 기준선 선택, 간격 산정, 패밀리 삽입, 회전 방향 조정, 오프셋 보정이 반복되어 작업 시간이 오래 걸리고 누락이나 위치 오류가 발생할 가능성이 있었습니다.",
        ],
        (
            "해결 방향",
            "DWG 또는 Revit edge를 직접 선택하면 기준선과 보조선을 자동 분석하고, 지정한 간격·오프셋·회전 방식에 따라 트로프 패밀리를 자동 배치하는 Dynamo Python 스크립트를 작성했습니다.",
        ),
    ),
    (
        "2. 수행 방법",
        [
            "Dynamo 내부에는 Python Script 노드를 중심으로 구성했습니다. 사용자가 main edge와 side edge를 순서대로 선택하면, 선택된 edge를 Revit API에서 curve 데이터로 변환하고 연속 선형으로 재구성합니다.",
            "이후 기준선 위에 일정 간격으로 배치점을 만들고, side edge와의 관계를 이용해 폭 방향과 회전 방향을 계산합니다. 패밀리는 계산된 위치로 이동시킨 뒤 Z축 기준으로만 회전되도록 처리했습니다.",
        ],
        None,
    ),
    (
        "3. 주요 기능",
        [
            "1) DWG/Revit edge 선택 - CAD에서 가져온 DWG 선분과 Revit curve를 모두 배치 기준으로 사용할 수 있습니다.",
            "2) 선형 자동 연결 - 여러 개로 나뉜 선분도 끝점 허용오차를 기준으로 하나의 연속 구간처럼 처리합니다.",
            "3) 간격 배치 - 패밀리 설치 간격 값을 입력하면 기준선 위에 반복 배치점을 자동 생성합니다.",
            "4) 오프셋 적용 - X_offset과 Y_offset을 통해 패밀리 중심 위치와 시작 위치를 조정할 수 있습니다.",
            "5) 회전 방식 선택 - 0도, 90도, 180도 회전 모드를 제공하여 패밀리 방향을 현장 조건에 맞게 조정할 수 있습니다.",
            "6) 재실행 기능 - 재실행 버튼을 통해 edge를 다시 선택하고 기준선을 갱신할 수 있습니다.",
        ],
        None,
    ),
    (
        "4. AI 활용 내용",
        [
            "이번 작업에서는 AI를 단순히 문서 작성 용도로만 사용하지 않고, Dynamo 자동화 로직을 정리하고 Python/Revit API 코드 구조를 잡는 데 활용했습니다.",
            "수작업 절차를 자동화 가능한 단계로 나누고, edge 선택 필터, DWG ImportInstance 좌표 변환, PolyLine 처리, 패밀리 생성 및 회전 로직을 검토하는 데 도움을 받았습니다.",
            "또한 패밀리가 의도하지 않은 방향으로 회전하거나 DWG 선분 좌표가 맞지 않는 문제처럼 실제 Dynamo 작업 중 발생할 수 있는 오류를 분석하고 보완하는 과정에도 AI를 활용했습니다.",
        ],
        (
            "AI 활용 포인트",
            "업무 절차를 코드 로직으로 변환하고, Revit API의 좌표·회전·선택 처리처럼 헷갈리기 쉬운 부분을 반복 검토하여 개발 시간을 줄일 수 있었습니다.",
        ),
    ),
    (
        "5. 적용 효과",
        [
            "트로프 패밀리를 반복 배치하는 시간을 줄이고, 기준선 기반으로 일정하게 배치할 수 있었습니다.",
            "DWG 도면의 선형 정보를 BIM 모델 배치 기준으로 직접 활용할 수 있어 2D 도면 확인 후 다시 입력하는 과정을 줄일 수 있습니다.",
            "설치 간격, 오프셋, 회전 방식을 입력값으로 관리하므로 설계 변경이나 현장 조건 변경에도 비교적 빠르게 대응할 수 있습니다.",
            "이번 Dynamo 구조는 트로프뿐만 아니라 관로, 케이블트레이, 지지대 등 선형을 따라 배치되는 다른 시설물에도 확장할 수 있을 것으로 생각됩니다.",
        ],
        None,
    ),
    (
        "6. 마무리",
        [
            "이번 작업은 AI를 활용해 반복적인 BIM 모델링 작업을 Dynamo 자동화로 개선한 사례입니다.",
            "아직 배치 결과 검증표나 오류 로그 출력 같은 보완 기능은 추가할 여지가 있지만, 기준선 선택부터 패밀리 배치와 회전까지 자동화했다는 점에서 실제 프로젝트 업무에 충분히 활용 가능성이 있다고 판단됩니다.",
        ],
        None,
    ),
]


def esc(value: object) -> str:
    return html.escape(str(value), quote=True)


def run(text: str, *, bold: bool = False, color: str | None = None, size: int | None = None) -> str:
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


def para(
    text: str,
    *,
    style: str | None = None,
    bold: bool = False,
    color: str | None = None,
    size: int | None = None,
    before: int = 0,
    after: int = 160,
) -> str:
    props = [f'<w:spacing w:before="{before}" w:after="{after}" w:line="310" w:lineRule="auto"/>']
    if style:
        props.insert(0, f'<w:pStyle w:val="{style}"/>')
    return f"<w:p><w:pPr>{''.join(props)}</w:pPr>{run(text, bold=bold, color=color, size=size)}</w:p>"


def heading(text: str) -> str:
    return para(text, style="Heading1", bold=True, color="1F4E79", size=27, before=420, after=180)


def callout(title: str, body: str) -> str:
    return (
        '<w:tbl><w:tblPr><w:tblW w:w="9000" w:type="dxa"/>'
        '<w:tblBorders><w:top w:val="single" w:sz="6" w:color="9DC3E6"/>'
        '<w:left w:val="single" w:sz="10" w:color="2F75B5"/>'
        '<w:bottom w:val="single" w:sz="6" w:color="9DC3E6"/>'
        '<w:right w:val="single" w:sz="6" w:color="9DC3E6"/></w:tblBorders>'
        '</w:tblPr><w:tr><w:tc><w:tcPr><w:tcW w:w="9000" w:type="dxa"/>'
        '<w:shd w:fill="EAF3F8"/><w:tcMar><w:top w:w="180" w:type="dxa"/>'
        '<w:left w:w="220" w:type="dxa"/><w:bottom w:w="180" w:type="dxa"/>'
        '<w:right w:w="220" w:type="dxa"/></w:tcMar></w:tcPr>'
        f'{para(title, bold=True, color="1264C8", after=70)}{para(body, after=50)}'
        '</w:tc></w:tr></w:tbl>'
    )


def document_xml() -> str:
    parts = [
        para(TITLE, bold=True, color="1F4E79", size=34, after=120),
        para(SUBTITLE, color="5B9BD5", size=22, after=360),
    ]
    for title, paragraphs, box in SECTIONS:
        parts.append(heading(title))
        for text in paragraphs:
            parts.append(para(text))
        if box:
            parts.append(callout(box[0], box[1]))
    sect = (
        '<w:sectPr><w:pgSz w:w="11906" w:h="16838"/>'
        '<w:pgMar w:top="1150" w:right="1250" w:bottom="1150" w:left="1250" w:header="720" w:footer="720" w:gutter="0"/>'
        '</w:sectPr>'
    )
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
        f'<w:body>{"".join(parts)}{sect}</w:body></w:document>'
    )


def text_version() -> str:
    lines = [TITLE, "", SUBTITLE, ""]
    for title, paragraphs, box in SECTIONS:
        lines.append(title)
        lines.append("")
        lines.extend(paragraphs)
        lines.append("")
        if box:
            lines.append(f"[{box[0]}]")
            lines.append(box[1])
            lines.append("")
    return "\n".join(lines).strip() + "\n"


STYLES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults>
<w:rPrDefault><w:rPr><w:rFonts w:ascii="Malgun Gothic" w:eastAsia="Malgun Gothic" w:hAnsi="Malgun Gothic" w:cs="Malgun Gothic"/><w:sz w:val="21"/><w:szCs w:val="21"/></w:rPr></w:rPrDefault>
<w:pPrDefault><w:pPr><w:spacing w:after="160" w:line="310" w:lineRule="auto"/></w:pPr></w:pPrDefault>
</w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/></w:pPr><w:rPr><w:b/><w:color w:val="1F4E79"/><w:sz w:val="27"/><w:szCs w:val="27"/></w:rPr></w:style>
</w:styles>'''


PACKAGE = {
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
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>AI 활용 Dynamo 트로프 자동 배치 작업 사례</dc:title><dc:creator>Codex</dc:creator></cp:coreProperties>''',
    "docProps/app.xml": '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Application>Codex</Application></Properties>''',
}


def main() -> None:
    DOCX_OUT.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(DOCX_OUT, "w", zipfile.ZIP_DEFLATED) as docx:
        for name, content in PACKAGE.items():
            docx.writestr(name, content)
        docx.writestr("word/document.xml", document_xml())
    TXT_OUT.write_text(text_version(), encoding="utf-8")
    print(DOCX_OUT.resolve())
    print(TXT_OUT.resolve())


if __name__ == "__main__":
    main()
