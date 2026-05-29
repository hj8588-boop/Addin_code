"""
Revit MCP Server
Claude가 이 서버를 통해 Revit 모델 데이터를 읽고 명령을 실행합니다.

통신 방식: 파일 기반 (C:\\revit_mcp\\ 폴더 공유)
- Claude가 command.json에 명령 작성
- Dynamo/pyRevit이 command.json을 읽고 실행
- 결과를 result.json에 저장
- Claude가 result.json을 읽고 응답
"""

import asyncio
import json
import os
import time

import mcp.types as types
from mcp.server import Server
from mcp.server.stdio import stdio_server

COMM_DIR = r"C:\revit_mcp"
COMMAND_FILE = os.path.join(COMM_DIR, "command.json")
RESULT_FILE = os.path.join(COMM_DIR, "result.json")
TIMEOUT_SECONDS = 30

app = Server("revit-mcp")


def send_command(tool_name: str, args: dict) -> dict:
    """명령 파일 작성 후 결과 파일을 기다립니다."""
    os.makedirs(COMM_DIR, exist_ok=True)

    # 이전 결과 파일이 있으면 삭제
    if os.path.exists(RESULT_FILE):
        os.remove(RESULT_FILE)

    # 명령 작성
    with open(COMMAND_FILE, "w", encoding="utf-8") as f:
        json.dump({"tool": tool_name, "args": args}, f, ensure_ascii=False, indent=2)

    # 결과 대기
    deadline = time.time() + TIMEOUT_SECONDS
    while time.time() < deadline:
        time.sleep(0.5)
        if os.path.exists(RESULT_FILE):
            with open(RESULT_FILE, "r", encoding="utf-8") as f:
                result = json.load(f)
            os.remove(RESULT_FILE)
            return result

    return {"error": f"Revit 응답 없음 ({TIMEOUT_SECONDS}초 초과). Dynamo 감시 스크립트가 실행 중인지 확인하세요."}


@app.list_tools()
async def list_tools() -> list[types.Tool]:
    return [
        types.Tool(
            name="get_elements_by_category",
            description="Revit 모델에서 특정 카테고리의 요소 목록과 파라미터를 가져옵니다. 예: '벽', '기둥', 'OST_Walls'",
            inputSchema={
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "description": "카테고리 이름 (한글 또는 영어). 예: '벽', '기둥', 'Walls', 'Columns'",
                    },
                    "max_count": {
                        "type": "integer",
                        "description": "최대 결과 수 (기본값: 20)",
                        "default": 20,
                    },
                },
                "required": ["category"],
            },
        ),
        types.Tool(
            name="get_element_by_id",
            description="Revit 요소 ID로 특정 요소의 상세 파라미터 정보를 가져옵니다.",
            inputSchema={
                "type": "object",
                "properties": {
                    "element_id": {
                        "type": "integer",
                        "description": "Revit 요소의 정수 ID",
                    }
                },
                "required": ["element_id"],
            },
        ),
        types.Tool(
            name="set_parameter",
            description="Revit 요소의 파라미터 값을 변경합니다. 트랜잭션이 자동으로 처리됩니다.",
            inputSchema={
                "type": "object",
                "properties": {
                    "element_id": {
                        "type": "integer",
                        "description": "대상 요소의 Revit ID",
                    },
                    "parameter_name": {
                        "type": "string",
                        "description": "변경할 파라미터 이름",
                    },
                    "value": {
                        "type": "string",
                        "description": "설정할 값 (문자열로 전달, Revit이 적절한 타입으로 변환)",
                    },
                },
                "required": ["element_id", "parameter_name", "value"],
            },
        ),
        types.Tool(
            name="get_project_info",
            description="현재 Revit 프로젝트의 기본 정보를 가져옵니다. (프로젝트명, 클라이언트, 주소 등)",
            inputSchema={
                "type": "object",
                "properties": {},
            },
        ),
        types.Tool(
            name="get_all_categories",
            description="현재 Revit 모델에 있는 모든 카테고리 목록을 가져옵니다.",
            inputSchema={
                "type": "object",
                "properties": {},
            },
        ),
        types.Tool(
            name="run_dynamo_script",
            description="지정한 경로의 Dynamo 스크립트(.dyn)를 실행합니다.",
            inputSchema={
                "type": "object",
                "properties": {
                    "dyn_path": {
                        "type": "string",
                        "description": "실행할 .dyn 파일의 전체 경로",
                    }
                },
                "required": ["dyn_path"],
            },
        ),
        types.Tool(
            name="create_walls_with_windows",
            description="지정한 높이와 길이로 벽을 여러 개 생성하고 각 벽 중앙에 창문을 삽입합니다.",
            inputSchema={
                "type": "object",
                "properties": {
                    "count": {
                        "type": "integer",
                        "description": "생성할 벽 개수 (기본값: 3)",
                        "default": 3,
                    },
                    "height_mm": {
                        "type": "number",
                        "description": "벽 높이 (mm 단위, 기본값: 3000)",
                        "default": 3000,
                    },
                    "length_mm": {
                        "type": "number",
                        "description": "벽 한 개의 길이 (mm 단위, 기본값: 5000)",
                        "default": 5000,
                    },
                    "spacing_mm": {
                        "type": "number",
                        "description": "벽 사이 간격 (mm 단위, 기본값: 2000)",
                        "default": 2000,
                    },
                },
                "required": [],
            },
        ),
        types.Tool(
            name="review_electrical_families",
            description="전기 패밀리 인스턴스를 검토하고 카테고리, 패밀리, 타입, 레벨, IFC 파라미터 상태를 CSV로 저장합니다.",
            inputSchema={
                "type": "object",
                "properties": {
                    "output_folder": {
                        "type": "string",
                        "description": "검토 CSV를 저장할 폴더입니다. 생략하면 Desktop\\revit_electrical_review를 사용합니다.",
                    }
                },
                "required": [],
            },
        ),
        types.Tool(
            name="export_electrical_ifc",
            description="전기 관련 카테고리만 임시 3D 뷰에 남겨 IFC로 내보내고, 추출 대상 객체의 패밀리/타입/파라미터 검토 목록을 CSV와 JSON으로 함께 저장합니다.",
            inputSchema={
                "type": "object",
                "properties": {
                    "output_folder": {
                        "type": "string",
                        "description": "IFC를 저장할 폴더입니다. 생략하면 Desktop\\revit_electrical_ifc를 사용합니다.",
                    },
                    "file_name": {
                        "type": "string",
                        "description": "IFC 파일명입니다. .ifc 확장자가 없으면 자동으로 붙입니다.",
                    },
                },
                "required": [],
            },
        ),
    ]


@app.call_tool()
async def call_tool(name: str, arguments: dict) -> list[types.TextContent]:
    result = send_command(name, arguments)
    text = json.dumps(result, ensure_ascii=False, indent=2)
    return [types.TextContent(type="text", text=text)]


async def main():
    async with stdio_server() as (read_stream, write_stream):
        await app.run(read_stream, write_stream, app.create_initialization_options())

if __name__ == "__main__":
    asyncio.run(main())
