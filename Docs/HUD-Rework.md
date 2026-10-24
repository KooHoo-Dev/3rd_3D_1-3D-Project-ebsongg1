WARD SHIFT HUD 개편 보고 (2026-10-25)

MainGame 씬의 기존 HUD Canvas를 재사용해 배치와 표시 조건을 변경했다. 마법 판정, 이상현상 성장·생성, 피해, DAY 규칙 및 보상 계산은 유지했다. 정식 UI 아트와 새로운 게임 기능은 추가하지 않았다.

1. Canvas와 Hierarchy

```text
WardShift_TestHUD [기존 Canvas, WardShiftHUDLayout]
  HUD
    Crosshair [기존 오브젝트]
    AnomalyAlerts [활성 현상별 독립 카드 최대 3개 + 초과 개수]
    CoreStatus [안정도 / 오염도, 위치 / 노출]
    DayResources [DAY / 남은 시간 / 결정]
    FloodCondition [현재 조건 이름 / 효과]
    AimOrResult [최소 조준 정보 또는 짧은 결과]
    CastPanel [실제 시전 중에만 표시]
    MagicAndInventory [마법 3개 / 실제 가방 4칸]
    Controls
    PickupPrompt
  LegacyHUD
    CastingPanel [기존 MagicHUD 컴포넌트, 구형 Graphic 비활성화]
    WardStatusPanel [기존 WardStatusHUD 컴포넌트, 구형 Graphic 비활성화]
    FailurePanel [기존 참조 보존, DAY 흐름에서는 숨김]
  DayOverlays [Play Mode에서 기존 Day5HUD가 생성]
    InventoryPanel / DayClearPanel / DayFailedPanel / DayEndingPanel
    OpenDaySupply / DaySupplyPanel
```

기존 FacilityMap_Canvas와 AnomalyPing_Canvas는 유지한다. DAY UI를 위한 별도 Canvas 생성은 제거했다. HUD 기준 해상도는 1920×1080, CanvasScaler는 Scale With Screen Size, Match 0.5다. 화면 가장자리 기본 여백은 32다. 모달을 열거나 근무가 끝나면 일반 HUD를 숨긴다.

2. 추가·수정 스크립트

| 파일 | 역할 |
| --- | --- |
| Assets/Scripts/Core/WardShiftHUDLayout.cs (추가) | 영역 생성, Noto Sans KR 행 높이 보정, 마법 선택 강조, 실제 4칸 가방 요약, 범람 조건, 모달 가시성 |
| Assets/Scripts/Core/WardStatusHUD.cs | 상태 게이지, 인스턴스별 이상현상 카드 생성·갱신·정렬·회수, 독립적인 장치 방출 경고 |
| Assets/Scripts/Magic/MagicHUD.cs | 조준점 아래 시전 패널, 대상 진행도와 타이밍 구간, 별도 조준/결과 문구 |
| Assets/Scripts/Core/Day5HUD.cs | 기존 Canvas에 인벤토리/결과/강화/보급 창 연결, 우측 상단 DAY 표시, 기존 보급 열기 버튼을 좌측 하단으로 이동 |
| Assets/Scripts/Core/AnomalyScreenPingHUD.cs | 기존 핑의 라벨 크기·문구 조정, HUD/시전 영역 회피, 라벨 겹침 완화, 보급 창에서도 숨김 |
| Assets/Scripts/Anomalies/ManaDeviceAnomaly.cs | 기존 안정화 대상의 Progress를 반환하는 읽기 전용 StabilizationProgress 속성만 추가 |
| Assets/Editor/WardShiftHUDSetup.cs (추가) | 기존 Canvas의 UI 생성·참조 연결·씬 저장 자동화 |
| Assets/Editor/WardShiftHUDPlayModeCheck.cs (추가) | MainGame Play Mode 자동 검사와 Game 뷰 캡처 |

MagicCaster, MagicTarget, CursePatch, PlayerInventory, DayFlowManager, FloodConditionRules의 게임 규칙은 수정하지 않았다. 편집기에 이미 열려 있던 MainGame의 미저장 PatchPoint_05 위치 변경도 보존해 저장했다.

3. 데이터 연결

기존 MagicHUD와 WardStatusHUD의 직렬화된 게임 데이터 참조를 유지하고 ConfigureView로 새 UI 참조를 연결했다. 안정도는 기존 StabilityChanged 구독을 유지하며 OnDisable에서 해제한다. 오염도, 이상현상 목록, DAY/시간, 선택 마법, 아이템 종류·수량 등은 기존 공개 상태를 읽는다. 새 전역 이벤트 구독은 추가하지 않았다.

이상현상 카드는 기존 활성 인스턴스 목록을 읽고 인스턴스 ID로 재사용한다. 위험 단계가 높고, 파열까지 남은 시간이 짧으며, 발생 순번이 최근인 현상을 먼저 표시한다. 최대 3장을 보여 주고 추가 현상 수와 M 지도 안내를 표시한다. 같은 구역의 현상도 인스턴스마다 별도 카드로 남는다. 카드는 해결·파열·비활성화 시 회수하고 DAY 전환·재도전 시 정리한다. 장치 방출 예고와 시설/플레이어 전체 경고는 카드별 정보와 독립적으로 유지한다. 인벤토리 슬롯은 실제 슬롯 순서를 보존하며 소비 아이템용 숫자 단축키를 만들지 않았다.

4. 시전 게이지 표시/숨김

MagicHUD는 CastingTarget이 존재하고 IsCasting/IsAvailable이며 입력이 차단되지 않은 경우에만 CastPanel을 활성화한다. 조준 상태에서는 대상 이름과 필요한 마법만 보인다. 완료·취소·대상 상실·입력 차단 시 게이지가 숨겨지고, 결과는 기존 ShowingResult 기간 동안 별도의 짧은 텍스트로 표시한다.

봉인은 실제 대상의 StableStart/StableEnd와 Progress를 사용한다. 정화는 매 프레임 현재 CastingTarget의 Progress를 읽으므로 패치가 같은 프레임에 바뀌어도 불필요한 숨김 단계를 거치지 않는다. 안정화는 대상에 저장된 Progress를 그대로 읽으며 UI에서 초기화하지 않는다. HUD 가시성과 장치의 월드 경고 링/방출 로직은 연결하지 않았다.

표시 갱신 순서는 기존 DAY 입력·종료 처리 뒤에 오도록 조정했다.

5. 범람 조건 표시 시점

매 LateUpdate에서 현재 DAY와 DayFlowManager.ActiveFloodCondition을 읽는다. DAY 1~3 또는 활성 조건이 None이면 FloodCondition 오브젝트 전체를 비활성화해 빈 공간을 남기지 않는다. DAY 4~7은 기존 FloodConditionRules 데이터의 이름과 효과를 표시한다. DAY 전환·재도전 후에도 최신 DAY와 조건에 맞춰 다시 표시한다. 실제 범람 조건 계산과 선택 규칙은 수정하지 않았다.

6. 실제 테스트와 범위

설치된 Unity 6000.3.15f1의 열린 MainGame 편집기에서 스크립트 컴파일과 Play Mode 검사를 실행했다. 현재 2차 검사에서 자동 assertion 264개가 통과했다. 상세 결과는 HUD/validation.txt, Game 뷰 캡처는 HUD 폴더에 있다.

확인한 내용:

- 조준만 할 때 게이지 숨김, 3종 시전 진행도 연결, 봉인 타이밍 구간, 취소/입력 차단 시 숨김.
- 정화 대상 전환 시 진행도 변경과 패널 연속 표시. 이 검사는 대상/시전자 상태를 주입한 표시 계약 검사다.
- 실제 MagicCaster 취소 경로에서 안정화 35% 보존, 재시전 50% 표시.
- 실제 장치 이상현상과 경고 링 생성 후 시전 중단과 관계없이 HUD 방출 예고/월드 링 유지.
- 안정도 64, 오염도 38, 결정 및 4칸 실제 가방 수량의 UI 반영.
- 지도/가방 API를 통한 열기·닫기, 기존 시간 정지·입력 차단·커서 복원, 일반 HUD 숨김.
- 실제 이상현상 프리팹 4개 동시 활성화 시 인스턴스별 카드, 같은 구역 중복 카드, 위험도·파열 시간·최근 발생순 정렬, 위험 단계/시간 갱신, 파열 후 제거, 카드 상한 3개와 초과 개수.
- DAY 1~3 범람 패널 전체 숨김, DAY 4 이후 적용 조건 표시, DAY 4 활성 조건 없음 상태 숨김, DAY 전환·재도전 시 갱신.
- 강화 선택창의 제목/요약/조건/카드/버튼 경계와 카드별 텍스트 잘림 검사. 1920×1080 및 1280×720에서 같은 카드 크기·버튼 영역 검사.
- 실제 강화 구매 시 단계 상승·결정 차감·1회 구매 제한, 1920×1080 및 1280×720에서 다음 DAY 버튼 활성 및 진행.
- CLEAR/FAILED/엔딩 시 HUD 잔상 제거.
- 1920×1080과 1920×1200에서 주요 패널의 화면 내부 배치와 상단 패널 간 간격.
- HUD TMP의 Noto Sans KR 재사용과 텍스트 잘림 검사. Game 뷰 캡처에서 한글·사용 기호를 시각적으로 확인.
- 마지막 봉인 캡처는 임시 대상과 임시 Input System 마우스 장치로 실제 Raycast·InputAction 경로를 실행하고 CastingTarget 및 화면 게이지가 활성화된 상태에서 촬영했다. 장치는 검사 종료 시 제거한다.

범위 제한: 이상현상 생성은 실제 프리팹으로 검사했으며 성장 시간은 테스트에서 주입했다. 강화를 구매할 때는 실제 버튼 콜백을 호출했고, 자연 플레이 전체에서 반복 구매를 조작한 것은 아니다. 7일 전체 자연 진행과 빌드 실행은 검증하지 않았다. DAY 전환 검사에는 기존 디버그 흐름과 조건 테스트 오버라이드를 사용했다. 모든 임시 대상과 테스트 상태는 Play Mode 종료로 제거되며 씬에 저장하지 않는다.

ESC 메뉴 입력은 현재 게임 코드에 구현되어 있지 않아 HUD에 표시하지 않았다. 편집기에서 ESC로 커서 잠금이 풀리는 동작을 게임 메뉴로 안내하지 않는다. 기존 T 보급 입력과 버튼은 유지했다.

7. Unity에서 직접 확인할 최소 항목

1) DAY CLEAR에서 세 강화 설명·현재/다음 단계 효과·비용을 확인하고 구매 및 다음 DAY 시작을 확인한다.
2) DAY 1~3에서 범람 조건 패널이 없는지, DAY 4부터 실제 조건 이름과 효과가 보이는지 확인한다.
3) 이상현상 1~4개를 활성화해 개별 카드·최대 3장·초과 개수 안내와 위험도 정렬, 파열/진압 후 제거를 확인한다.
4) 1번 봉인·2번 정화·3번 안정화, Q 감지, M 지도, B 가방과 소비 아이템을 확인한다. 이상현상 지도 핑이 카드 UI와 별도로 계속 표시되는지도 확인한다.

다시 생성할 때는 Tools > WARD SHIFT > Setup Readable HUD를 사용한다. 기존 생성물이 있으면 중복으로 만들지 않는다. 검사 재실행 메뉴는 Tools > WARD SHIFT > Run HUD Play Mode Check다. 검사는 Play Mode를 종료한 상태에서 MainGame을 열고 실행한다.

8. 추천 Git 커밋 메시지

```text
refactor(ui): reorganize WARD SHIFT HUD and show cast gauges only while casting
```

이 작업은 HUD 개편에서 종료한다. DAY 밸런스와 정식 UI 디자인은 후속 작업 범위다.
