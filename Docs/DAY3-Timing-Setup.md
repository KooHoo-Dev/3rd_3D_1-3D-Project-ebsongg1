# DAY 3 — 1차: 타이밍 시전 연결 및 확인

이번 단계는 테스트 구체의 타이밍 시전까지입니다. 씬/프리팹은 수정하지 않았습니다.

## 1. 기존 구체 설정
Unity에서 Assets/Scenes/MainGame.unity를 열고 스크립트 컴파일이 끝날 때까지 기다립니다.
Test_MagicSphere와 Test_MagicSphere (1)의 MagicTarget에서 다음을 확인합니다.
- Use Timing Cast: 체크
- Cast Duration: 2
- Stable Start: 0.6
- Stable End: 0.85
- Required Magic: Seal
- Deactivate On Complete: 체크
기존 MagicTargetVFX, 카메라, 플레이어, HUD 연결은 유지합니다.
자동 완료를 비교하려면 해당 구체의 Use Timing Cast만 해제합니다.

## 2. 안정 구간 UI 추가
기존 CastingPanel 아래 ProgressBackground와 같은 부모에 UI > Image를 만들고 이름을 TimingZones로 지정합니다.
ProgressBackground의 RectTransform을 복사해 붙여넣은 뒤, 높이를 8로 줄이고 게이지 바로 아래로 이동합니다.
TimingZones 색상을 어두운 파랑으로, Raycast Target을 해제로 설정합니다.
TimingZones 아래에 UI > Image 두 개를 만들고 StableZone, OverchargeZone으로 이름을 지정합니다.
두 이미지 모두 Image Type은 Simple, Raycast Target은 해제로 둡니다. Layout Group은 추가하지 않습니다.
CastingPanel의 기존 MagicHUD에서 Stable Zone에 StableZone, Overcharge Zone에 OverchargeZone을 연결합니다.
스크립트가 자식 이미지의 앵커/크기/색상을 설정합니다. 기본적으로 녹색은 전체 너비의 60~85%, 붉은색은 85~100%입니다.
별도의 얇은 띠로 표시하므로 기존 ProgressFill이 안정 구간을 가리지 않습니다.
기존 ProgressFill은 Image Type = Filled, Fill Method = Horizontal, Fill Origin = Left를 확인합니다.
새로 추가한 UI가 기존 안내 문구와 겹치면 TimingZones 위치만 조정합니다.

구조 예시 (기존 부모 관계에 맞춰 배치):
CastingPanel
  ProgressBackground
    ProgressFill (기존)
  TimingZones (새 Image)
    StableZone (새 Image)
    OverchargeZone (새 Image)

## 3. 실행 흐름과 코드 역할
PlayerInteractor.LateUpdate가 이번 프레임의 조준 대상을 먼저 확인합니다.
MagicCaster.LateUpdate가 입력과 조준을 검사하고 MagicTarget.AdvanceCast로 충전합니다.
버튼을 놓으면 MagicTarget.ReleaseCast가 성공/충전 부족/과충전을 판정합니다.
MagicTarget의 CastStarted/CastStopped/Completed 이벤트를 기존 MagicTargetVFX가 받아 효과를 처리합니다.
MagicHUD.LateUpdate가 충전량과 결과를 표시합니다.

MagicCastResult는 결과 이름을 묶은 enum입니다. CastResolved 이벤트는 대상과 결과를 전달하므로 나중에 과충전 페널티를 연결할 수 있습니다.
Completed와 기존 Inspector On Completed는 성공 때만 발생합니다. 실패 및 취소는 대상을 제거하지 않습니다.
성공 결과는 실제 버튼 해제 충전량을 1.5초 표시합니다. 실패도 1.5초 표시하며 새 시전은 바로 시작할 수 있습니다.
판정은 반올림한 숫자가 아닌 실제 충전량 기준입니다. 60%와 85%는 성공, 85% 초과는 과충전입니다.
100% 도달 시 자동 과충전 실패하며, 누르고 있는 동안 반복 실패하지 않습니다.
조준 이탈/마법 변경 시 새 클릭이 필요합니다. 누른 채 다른 대상에 옮겨 자동으로 충전을 시작하지 않습니다.

## 4. 일시정지와 메뉴 연결
Time.timeScale이 0이면 시전이 취소됩니다. 포커스 상실과 컴포넌트 비활성화도 취소합니다.
커서가 잠겨 있지 않고 UI 위에 있으면 시전/마법 선택을 차단합니다.
메뉴 전체가 열려 있는 동안 차단하려면 메뉴 열기 코드에서 MagicCaster.SetInputBlocked(true), 닫기에서 false를 호출합니다.
이 연결은 기존 메뉴 시스템이 생길 때 필요합니다. UI 위 판정만으로 모든 메뉴 상태를 알 수는 없습니다.
Q 감지 및 Starter Assets 이동 입력은 이번 변경 대상이 아닙니다.

## 5. Unity에서 직접 확인할 항목
각 성공 테스트 후에는 Play 모드를 종료하고 다시 실행하면 구체가 복구됩니다.
1. 1키로 봉인 선택 → 구체 조준 → 좌클릭 약 1.2~1.7초 → 녹색 구간에서 해제: 완료 효과와 구체 비활성화.
2. 60% 미만에서 해제: 충전 부족 표시, 구체 유지, 재시도 가능.
3. 85% 초과~100% 미만에서 해제: 과충전 표시와 로그, 반복 효과 중단.
4. 2초 이상 유지: 과충전 한 번만 발생. 계속 누르고 있어도 재시작하지 않고 놓은 후 새 클릭 시 재시도.
5. 충전 중 조준 이탈, 사거리 이탈, 2키로 마법 변경: 취소 및 반복 효과 제거.
6. 정화 선택 상태에서 봉인 구체 클릭: 시작되지 않음.
7. 충전 중 Hierarchy에서 구체 비활성화/삭제: 예외나 남은 반복 효과가 없는지 확인.
8. 충전 중 창 전환, MagicCaster 비활성화, 일시정지: 성공으로 오인하지 않는지 확인.
9. 메뉴가 있으면 SetInputBlocked 연결 후 UI 클릭으로 시전되지 않는지 확인.
10. Use Timing Cast 해제: 끝까지 유지하면 기존 자동 성공, 중간 해제는 취소.
11. 두 구체 중 하나를 완료해도 다른 구체는 유지되는지 확인.
12. Console의 빨간 오류, VFX 중복/잔류, 구간 띠의 위치를 확인.

## 이후 단계
위 테스트 확인 후 마력 균열 본체/봉인점 2개 관리 코드, 벽 SpawnPoint 연동을 진행합니다.
아직 균열/생성/오염도/성장/보상 시스템은 구현하지 않았습니다.
추천 커밋 메시지: feat: add release-timed magic casting and charge feedback
