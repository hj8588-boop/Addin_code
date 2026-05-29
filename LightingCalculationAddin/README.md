# Lighting Calculation Add-in

Revit 2024용 조도 계산서 add-in입니다. 기존 Dynamo `#조도계산서/선택@lighting_fixture_required_count.dyn`와 Python 계산 흐름을 C# UI로 옮겼습니다.

## 기능

- 현재 모델의 MEP Space 목록을 표 형태로 표시합니다.
- Space별 `필수 요구조도`, `광원고`, `보수율`, `조도_적용전등타입`, `조도_광속_lm`을 UI에서 수정합니다.
- 선택 Space 기준으로 조명 타입을 체크박스 목록에서 선택합니다.
- 광속을 직접 수정하거나 조명 타입의 `fixtureFlux_lm`/`광속` 값을 가져옵니다.
- 편집 즉시 `실지수`, `조명율`, `계산등수`, `필요등수`, `계산조도`를 다시 계산합니다.
- `Revit 저장` 버튼으로 결과를 Space 공유파라미터에 기록합니다.
- `엑셀 추출` 버튼으로 바탕화면 `조도계산서_exports` 폴더에 `.xlsx`를 생성합니다.
- 개발 중에는 로더 DLL이 `LightingCalculationEngine.dll`을 매 실행마다 새로 읽습니다. 로더 구조 설치 후에는 UI/계산 코드 수정 시 Revit을 재시작하지 않고 빌드/설치 후 버튼을 다시 누르면 됩니다.

## 계산식

```text
실지수 = 면적 / (광원고 * (가로 + 세로))
필요등수 = CEILING((요구조도 * 면적) / (광속 * 조명율 * 보수율))
계산조도 = 광속 * 조명율 * 보수율 * 필요등수 / 면적
```

조명율은 기존 Dynamo 기준과 같이 실지수 구간으로 자동 산정합니다.

## 빌드

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\build_revit2024.ps1
```

## 설치

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\install_revit2024.ps1
```

Revit을 재시작한 뒤 `Codex Tools > Lighting > 조도 계산서`를 실행하세요.

## 개발 중 재시작 줄이기

이번 구조는 Revit이 한 번 로드하는 `LightingCalculationAddin.dll`과, 버튼 실행 때마다 파일에서 다시 읽는 `LightingCalculationEngine.dll`로 나뉩니다.

- `App.cs`, `Command.cs`, `.addin` manifest를 바꾸면 Revit 재시작이 필요합니다.
- `LightingCalculationForm.cs`, `LightingCalculationService.cs`, `LightingFixtureType.cs`, `LightingSpaceRow.cs`, `SimpleXlsxWriter.cs`만 바꾸면 재시작 없이 다시 빌드/설치 후 버튼을 다시 실행하면 됩니다.
