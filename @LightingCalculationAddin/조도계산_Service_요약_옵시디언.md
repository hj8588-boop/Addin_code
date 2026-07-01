# 조도계산 Service 요약

대상 파일: [LightingCalculationService.cs](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs)

## 이 파일의 역할

`LightingCalculationService.cs`는 조도 계산서 애드인의 핵심 계산/저장 로직을 담당한다.

주요 역할은 다음과 같다.

- Revit의 Space/Room 데이터를 읽어서 조도 계산 행으로 변환
- 조도 계산 수식 적용
- 조명기구 타입과 광속값 읽기
- 광속값을 Revit 조명기구 파라미터에 동기화
- 계산 결과를 Space/Room 파라미터에 저장

화면 UI나 버튼 배치는 `LightingCalculationForm.cs`가 담당하고, 실제 계산과 Revit 파라미터 입출력은 이 파일이 담당한다.

## 공유 파라미터 이름

위치: [LightingCalculationService.cs:11](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:11)

```csharp
public const string RequiredLuxParam = "필수 요구조도";
public const string UtilizationParam = "조명율";
public const string MaintenanceParam = "보수율";
public const string EffectiveHeightParam = "광원고";
public const string CeilingReflectanceParam = "조도_천정반사율";
public const string WallReflectanceParam = "조도_벽반사율";
public const string FloorReflectanceParam = "조도_바닥반사율";
public const string TargetFixtureTypeParam = "조도_적용전등타입";
public const string RoomIndexParam = "조도_실지수";
public const string RequiredCountParam = "필요등수";
public const string ResultFluxParam = "fixtureFlux_lm";
public const string ResultFixtureParam = "조도_적용기구";
public const string InputFluxParam = "조도_광속_lm";
```

이 값들은 Revit Space/Room 또는 조명기구에서 읽고 쓰는 파라미터 이름이다.

## 데이터 불러오기

위치: [LightingCalculationService.cs:30](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:30)

```csharp
public static IList<LightingSpaceRow> LoadRows(Document document, IList<LightingFixtureType> fixtureTypes)
```

이 함수는 Revit 모델에서 Space와 Room을 모아서 조도 계산서 한 행씩 만든다.

핵심 흐름:

1. Space/Room 수집
2. 면적이 0 이하인 공간 제외
3. 면적, 가로, 세로, 요구조도, 광원고, 반사율, 보수율, 조명 타입, 광속 읽기
4. 조명기구 기본값 적용
5. 조도 계산 실행
6. 레벨명/공간명 기준으로 정렬

중요 코드:

```csharp
row.RequiredLux = ReadDouble(space, RequiredLuxParam, 500);
row.EffectiveHeightM = ReadDouble(space, EffectiveHeightParam, 2.4);
row.CeilingReflectance = ReadDouble(space, CeilingReflectanceParam, 70);
row.WallReflectance = ReadDouble(space, WallReflectanceParam, 50);
row.FloorReflectance = ReadDouble(space, FloorReflectanceParam, 20);
row.MaintenanceFactor = ReadDouble(space, MaintenanceParam, 0.8);
```

기본값:

- 요구조도: `500`
- 광원고: `2.4`
- 천정 반사율: `70`
- 벽 반사율: `50`
- 바닥 반사율: `20`
- 보수율: `0.8`

## 조도 계산식

위치: [LightingCalculationService.cs:166](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:166)

```csharp
public static void Recalculate(LightingSpaceRow row)
{
    row.RoomIndex = CalculateRoomIndex(row.AreaM2, row.LengthM, row.WidthM, row.EffectiveHeightM);
    double autoUtilization = GetUtilizationFromRoomIndex(row.RoomIndex);
    row.UtilizationFactor = autoUtilization > 0 ? autoUtilization : row.UtilizationFactor;
```

`Recalculate()`는 한 행의 계산 결과를 다시 계산한다.

계산 순서:

1. 실지수 계산
2. 실지수 기준 조명율 자동 선택
3. 누락값 검사
4. 계산등수, 필요등수, 계산조도 산출

필요등수 계산 위치: [LightingCalculationService.cs:188](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:188)

```csharp
row.RawRequiredCount = (row.RequiredLux * row.AreaM2) / (row.FixtureFluxLm * row.UtilizationFactor * row.MaintenanceFactor);
row.RequiredCount = (int)Math.Ceiling(row.RawRequiredCount);
row.CalculatedIlluminance = (row.FixtureFluxLm * row.UtilizationFactor * row.MaintenanceFactor * row.RequiredCount) / row.AreaM2;
```

수식:

```text
계산등수 = 요구조도 × 면적 / (광속 × 조명율 × 보수율)
필요등수 = 계산등수 올림
계산조도 = 광속 × 조명율 × 보수율 × 필요등수 / 면적
```

## 실지수 계산

위치: [LightingCalculationService.cs:331](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:331)

```csharp
private static double CalculateRoomIndex(double areaM2, double lengthM, double widthM, double effectiveHeightM)
{
    if (areaM2 <= 0 || lengthM <= 0 || widthM <= 0 || effectiveHeightM <= 0)
    {
        return 0;
    }

    double denominator = effectiveHeightM * (lengthM + widthM);
    return denominator <= 0 ? 0 : areaM2 / denominator;
}
```

수식:

```text
실지수 = 면적 / (광원고 × (가로 + 세로))
```

## 조명율 자동 선택

위치: [LightingCalculationService.cs:342](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:342)

```csharp
private static double GetUtilizationFromRoomIndex(double roomIndex)
{
    if (roomIndex <= 0) return 0;
    if (roomIndex <= 0.75) return 0.60;
    if (roomIndex <= 1.00) return 0.68;
    if (roomIndex <= 1.25) return 0.76;
    if (roomIndex <= 1.50) return 0.81;
    if (roomIndex <= 2.00) return 0.88;
    if (roomIndex <= 2.50) return 0.93;
    if (roomIndex <= 3.00) return 0.96;
    return 1.01;
}
```

현재 조명율은 실지수만 기준으로 자동 선택한다.

주의:

- 천정/벽/바닥 반사율은 현재 입력/저장/엑셀 추출은 되지만, 조명율 계산식에는 아직 반영되지 않는다.
- 반사율별 조명율 표를 적용하려면 이 함수 또는 별도 조명율 테이블 로직을 수정해야 한다.

## 광속 읽기

위치: [LightingCalculationService.cs:464](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:464)

```csharp
private static void ReadFixtureFlux(Element type, Element instance, out double flux, out string parameterName)
```

조명기구 타입과 인스턴스에서 광속값을 찾는다.

광속 후보 파라미터:

위치: [LightingCalculationService.cs:25](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:25)

```csharp
private static readonly string[] FluxParamNames = new[]
{
    InputFluxParam, ResultFluxParam, "광속", "광속(lm)", "Lamp Luminous Flux", "Initial Intensity"
};
```

읽는 순서:

1. 타입 파라미터 확인
2. 인스턴스 파라미터 확인
3. 첫 번째로 찾은 양수 광속값 사용

## 광속 동기화

위치: [LightingCalculationService.cs:229](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:229)

```csharp
public static int SyncFixtureFluxToProject(Document document)
```

이 함수는 모델 안의 조명기구에서 광속값을 찾아 `fixtureFlux_lm`에 복사한다.

역할:

- 조명기구 타입별 광속값 수집
- 타입 파라미터 `fixtureFlux_lm`에 저장
- 인스턴스 파라미터 `fixtureFlux_lm`에도 저장

이 기능은 화면의 `광속 동기화` 버튼과 연결된다.

## Revit 저장

위치: [LightingCalculationService.cs:300](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:300)

```csharp
public static void SaveRows(Document document, IEnumerable<LightingSpaceRow> rows)
{
    using (var transaction = new Transaction(document, "조도 계산서 저장"))
```

이 함수는 조도 계산서 화면의 행 데이터를 Revit Space/Room 파라미터에 저장한다.

저장 코드:

위치: [LightingCalculationService.cs:309](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:309)

```csharp
SetParameter(space, RequiredLuxParam, row.RequiredLux);
SetParameter(space, EffectiveHeightParam, row.EffectiveHeightM);
SetParameter(space, CeilingReflectanceParam, row.CeilingReflectance);
SetParameter(space, WallReflectanceParam, row.WallReflectance);
SetParameter(space, FloorReflectanceParam, row.FloorReflectance);
SetParameter(space, MaintenanceParam, row.MaintenanceFactor);
SetParameter(space, UtilizationParam, row.UtilizationFactor);
SetParameter(space, TargetFixtureTypeParam, row.FixtureType);
SetParameter(space, InputFluxParam, row.FixtureFluxLm);
SetParameter(space, ResultFluxParam, row.FixtureFluxLm);
SetParameter(space, RoomIndexParam, row.RoomIndex);
SetParameter(space, RequiredCountParam, row.RequiredCount);
SetParameter(space, ResultFixtureParam, row.FixtureType);
```

저장 대상:

- 요구조도
- 광원고
- 천정/벽/바닥 반사율
- 보수율
- 조명율
- 조명 타입
- 광속
- 실지수
- 필요등수
- 적용기구

## 파라미터 쓰기 방식

위치: [LightingCalculationService.cs:673](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs:673)

```csharp
private static bool TrySetParameter(Element element, string parameterName, object value)
```

이 함수는 Revit 파라미터 타입에 맞춰 값을 저장한다.

처리 방식:

- `StorageType.Double`이면 숫자 실수로 저장
- `StorageType.Integer`이면 정수로 저장
- `StorageType.String`이면 문자열로 저장
- 파라미터가 없거나 읽기 전용이면 저장하지 않음

중요:

```text
Revit 공유 파라미터가 실제 프로젝트에 없으면 저장되지 않는다.
파라미터가 읽기 전용이어도 저장되지 않는다.
```

## 관련 파일

- 화면 UI/버튼/엑셀 추출: [LightingCalculationForm.cs](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationForm.cs)
- 행 데이터 구조: [LightingSpaceRow.cs](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingSpaceRow.cs)
- 엑셀 파일 생성: [SimpleXlsxWriter.cs](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/SimpleXlsxWriter.cs)
- 계산/저장/광속 동기화: [LightingCalculationService.cs](C:/Users/user/Desktop/codex/LightingCalculationAddin/src/LightingCalculationService.cs)

## 앞으로 수정할 때 보는 위치

| 하고 싶은 작업 | 수정 위치 |
|---|---|
| 요구조도, 보수율, 반사율 기본값 변경 | `LoadRows()` |
| 필요등수 계산식 변경 | `Recalculate()` |
| 실지수 계산식 변경 | `CalculateRoomIndex()` |
| 조명율 표 변경 | `GetUtilizationFromRoomIndex()` |
| 저장 파라미터 추가/삭제 | `SaveRows()` |
| 광속 후보 파라미터 추가 | `FluxParamNames` |
| Revit 파라미터 쓰기 오류 확인 | `TrySetParameter()` |

