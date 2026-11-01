# WARD SHIFT 범람 조건 (과거 설계)

> 이전 범람 조건 설계 기록입니다. 현재 스토리 모드에서는 범람 조건을 사용하지 않습니다.
> 아래 설명과 테스트 API는 현재 코드에 적용되지 않는 과거 기록입니다.
> 현재 게임에서는 안전 구역도 제거되었습니다.
> 현재 검사는 Unity 메뉴 `Tools > WARD SHIFT > Verify Story Mode Without Flood Conditions`에서 실행합니다.

## 과거 규칙

- DAY 1~3은 기본 규칙만 적용합니다.
- DAY 4~7은 DAY 시작 전에 시드로 조건 하나를 선택하고, 직전 DAY와 같은 조건이 연속되지 않게 선택합니다.
- 재도전은 DAY 시작 스냅샷의 조건을 복원합니다.
- 정화 순환 강화는 안전 구역 회복에 2배를 적용하고, 기본 또는 현장 정화 강화 회복량에는 2/3을 적용합니다.
- 조기 봉합 보너스는 정상 진압 이벤트의 1단계 보상에만 적용합니다.

## 과거 검사 절차 (현재 실행 불가)

MainGame 씬을 연 뒤 Unity 메뉴 `Tools > WARD SHIFT > Run Flood Condition Play Mode Check`를 실행하면 DAY 진행, 두 조건, 정화약과 비상 수리 키트, 보상, 자동 정리, 재도전을 검사합니다. 검사는 Play Mode에서만 값을 바꾸며 씬을 저장하지 않습니다.

에디터 검사 코드에서는 다음 호출로 이후 DAY의 조건을 고정할 수 있습니다. DAY 1~3은 고정 요청과 관계없이 조건이 없습니다.

```csharp
dayFlow.ConfigureFloodConditionTestOverride(true, FloodCondition.PurificationCycle);
// 또는 FloodCondition.EarlySealBonus
dayFlow.ConfigureFloodConditionTestOverride(false, FloodCondition.None);
```

이 API와 고정값은 `UNITY_EDITOR`에서만 컴파일되며 게임 빌드의 랜덤 선택에는 포함되지 않습니다.
