# Revit MCP Add-in

Claude MCP와 Autodesk Revit을 파일 기반으로 연결하는 전용 폴더입니다.

## 폴더 구성

- `scripts/revit_mcp_server.py`: Claude가 실행하는 MCP 서버입니다.
- `scripts/revit_mcp_listener.py`: Dynamo Python 노드에서 수동 실행할 수 있는 리스너입니다.
- `src/RevitMcpListener.cs`: Revit Add-in에서 `C:\revit_mcp\command.json`을 감시하고 Revit API 명령을 실행합니다.
- `src/McpStatusCommand.cs`: Revit 리본에서 MCP 상태를 확인하는 버튼 명령입니다.
- `src/App.cs`: Revit 시작 시 MCP 리스너와 리본 버튼을 등록합니다.
- `.mcp.json`: 이 폴더만 Claude Code 프로젝트로 열 때 사용할 MCP 설정입니다.

## Claude Code 연결

상위 프로젝트의 `.mcp.json`은 이미 이 폴더의 서버를 바라보도록 설정되어 있습니다.

```json
{
  "mcpServers": {
    "revit": {
      "command": "python",
      "args": ["RevitMcpAddin/scripts/revit_mcp_server.py"],
      "cwd": "c:\\Users\\user\\Desktop\\codex"
    }
  }
}
```

## Revit Add-in 빌드와 설치

Revit을 종료한 뒤 PowerShell에서 실행합니다.

```powershell
.\RevitMcpAddin\deploy\build_revit2024.ps1
.\RevitMcpAddin\deploy\install_revit2024.ps1
```

또는 아래 배치 파일을 더블클릭합니다.

```text
RevitMcpAddin\deploy\build_and_install_revit2024.bat
```

설치 후 Revit을 다시 실행하면 `Codex Tools > Claude MCP` 버튼이 생깁니다.

## 통신 방식

1. Claude MCP 서버가 `C:\revit_mcp\command.json`을 만듭니다.
2. Revit Add-in 리스너가 파일 생성을 감지합니다.
3. Revit API 명령을 실행합니다.
4. 결과를 `C:\revit_mcp\result.json`에 저장합니다.
5. Claude MCP 서버가 결과를 읽어 Claude에게 돌려줍니다.
