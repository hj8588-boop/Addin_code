# Dynamo Python Workspace

초보자가 Dynamo Python 노드를 연습하고, 실무 스크립트를 조금씩 정리해 나가기 쉽게 작업 구조를 먼저 맞춘 워크스페이스입니다.

## 추천 폴더 구조

- `dynamo/`: Dynamo `.dyn` 파일 보관
- `scripts/`: Python 노드용 스크립트 템플릿과 실사용 코드
- `practice/`: 작은 연습 파일과 실험 코드
- `notes/`: 메모, 체크리스트, 디버깅 기록
- `output/`: 내보내기 결과물, 로그, 테스트 산출물
- `archive/`: 예전 버전, 백업, 더 이상 쓰지 않는 파일

## 시작 방법

1. 새 Dynamo 그래프는 `dynamo/`에 저장합니다.
2. Python 노드 코드는 `scripts/basic_template.py`를 기준으로 시작합니다.
3. 실험용 코드는 먼저 `practice/`에서 테스트합니다.
4. 결과 파일은 `output/`에 모읍니다.
5. 오래된 버전은 `archive/`로 옮겨서 현재 작업과 분리합니다.

## 초보자용 작업 원칙

- `IN`으로 입력을 받고 `OUT`으로 결과를 내보냅니다.
- Revit 요소를 다룰 때는 먼저 `unwrap()`으로 언래핑합니다.
- 리스트 입력 가능성을 생각해서 `to_list()`로 먼저 정리합니다.
- 길이 단위가 mm라면 Revit 내부 단위(ft)로 `mm_to_ft()` 변환 후 사용합니다.
- 중간 확인 내용은 `debug` 리스트에 쌓아두면 원인 파악이 쉽습니다.

## 기본 템플릿

- 템플릿 파일: `scripts/basic_template.py`
- 포함 내용:
  - `RevitServices`, `RevitAPI`, `RevitNodes` 참조
  - `IN` / `OUT` 유지
  - `to_list`, `unwrap`, `mm_to_ft`
  - `debug` 리스트

## 추천 흐름

- 입력 확인
- 리스트 정리
- Revit 요소 언래핑
- 필요한 계산 또는 파라미터 처리
- `debug` 확인
- `OUT` 정리
