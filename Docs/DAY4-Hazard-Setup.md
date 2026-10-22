# DAY 4 — 성장, 시설 피해, 오염, 안전 구역과 임시 실패

## 작업 상태와 범위

코드는 추가했으며 씬/프리팹의 Inspector 연결은 아래 순서대로 직접 진행합니다.
Unity 6000.3.15f1의 참조 DLL과 Roslyn으로 Assets/Scripts 전체를 별도 컴파일했습니다. 오류 없이 통과했고 Inspector 할당 필드의 CS0649 경고가 나왔습니다.
이것은 Unity Editor 자체의 컴파일/Play 테스트가 아닙니다. 아래 실행 테스트, UI 배치, 이동/시점 정지는 아직 실행 검증하지 않았습니다.

기존 두 봉인점, 타이밍 구간, 시전 실패/취소와 VFX는 유지합니다.
DAY 진행, 아이템, 보상, 인벤토리, 업그레이드, 단계별 마법 난이도는 추가하지 않았습니다.

## 1. 코드 역할과 규칙

| 스크립트 | 역할 |
| --- | --- |
| ManaRift (수정) | 성장 시간, 위험 단계, 폭주 1회 여부, 주변 오염량 |
| WardStatus (신규) | 시설 안정도, 위험 계산 순서, 실패 처리 |
| PlayerContamination (신규) | 플레이어 오염도 0~100, 안전 구역 회복 |
| SafeZone (신규) | 플레이어 기준점이 안전 박스 안에 있는지 검사 |
| WardStatusHUD (신규) | 수치/위험 경고/실패 UI 표시 |
| AnomalySpawner (수정) | 실패 이후 생성 요청 차단 |

시간은 균열 생성 후 누적 시간입니다. 1단계로 시작하고 10초에 2단계, 20초에 3단계, 30초에 폭주합니다.
폭주 때 안정도가 20 감소하며, 같은 균열이 다시 폭주하지 않습니다. 폭주 후에도 3단계로 남고 봉인할 수 있습니다.
봉인점 하나만 성공하면 위험이 유지됩니다. 둘 다 성공하면 성장/폭주/오염 계산에서 즉시 제외되고 기존 완료 효과 후 제거됩니다.
일시정지 중에는 시간이 흐르지 않습니다. 균열을 비활성화하면 위험 계산과 누적 시간이 멈추고 재활성화 시 이어집니다.

기본 오염은 균열 루트로부터 3m 이내에서 2단계 +3/초, 3단계 +6/초입니다. 1단계는 오염이 없습니다.
여러 균열의 범위가 겹치면 오염량을 합산합니다. 벽 너머도 거리 안이면 오염됩니다(이번 버전은 시야 차단 검사 없음).
안전 구역 안에서는 모든 주변 오염을 무시하고 -10/초로 회복합니다. 여러 안전 구역이 겹쳐도 회복량은 중복되지 않습니다.
안전 구역은 시설 폭주 피해를 막지 않습니다. 범위 밖에서는 오염이 그대로 유지됩니다.

## 2. 준비 — Play 모드를 끄고 씬 저장

1. 현재 DAY 3 상태를 커밋하거나 백업합니다.
2. MainGame 씬을 열고 새 스크립트의 Unity 컴파일이 끝날 때까지 기다립니다.
3. Console에 빨간 오류가 있다면 설정을 진행하기 전에 오류 전문을 전달해주세요.
4. 기존 플레이어, CastingPanel, 봉인점과 VFX 연결은 유지합니다.

## 3. 플레이어 오염과 시설 관리자 연결

1. Hierarchy에서 실제로 이동하는 플레이어 오브젝트를 선택합니다. MainCamera나 고정된 그룹 오브젝트가 아닙니다. 보통 CharacterController와 FirstPersonController가 있는 오브젝트입니다.
2. Add Component에서 PlayerContamination을 추가합니다.
3. Recovery Per Second = 10, Contamination = 0으로 둡니다.
4. 빈 GameObject를 만들고 이름을 WardSystems로 지정합니다. WardStatus를 추가합니다. 씬 전체에 하나만 있어야 합니다.
5. WardStatus의 Player에 2번에서 추가한 PlayerContamination을 드래그합니다.
6. Caster에 기존 MagicCaster가 붙은 플레이어를 드래그합니다.
7. Stability = 100으로 설정합니다.
8. Disable On Failure 배열에 실제 사용 중인 이동/시점 컴포넌트, PlayerInput, MagicDetector를 연결합니다.
   - FirstPersonController 사용 시 해당 컴포넌트를 연결합니다. ThirdPersonController를 쓰면 실제 사용하는 쪽을 연결합니다.
   - 별도 카메라 입력/회전 스크립트가 있으면 추가합니다. 기본 Starter Assets는 컨트롤러에서 시점 회전을 처리합니다.
   - 컴포넌트 제목 부분을 배열 칸으로 드래그하면 정확한 컴포넌트를 지정할 수 있습니다.
   - WardStatus, WardStatusHUD, Canvas, PlayerContamination은 넣지 않습니다.

실패 시 WardStatus가 시전을 취소하고 입력을 차단하며, 위 배열의 컴포넌트를 끄고 Time.timeScale을 0으로 만듭니다.
커서를 해제하고 실패 원인을 표시합니다. timeScale만으로는 일부 카메라 입력이 멈추지 않으므로 배열 연결이 필요합니다.
실패 후 재시작 버튼은 아직 없습니다. Play를 종료하고 다시 시작합니다. 실행 중 WardSystems를 껐다 켜는 방식으로 재시작하지 마세요.

## 4. 균열 프리팹 설정

1. Project에서 Assets/Prefabs/ManaRift_Test.prefab을 열어 루트의 ManaRift를 선택합니다.
2. 기존 Seal Point 01/02, 완료 VFX 연결을 유지합니다.
3. 다음 값을 확인합니다.

| Inspector 항목 | 기본값 | 뜻 |
| --- | --- | --- |
| Stage Two At | 10 | 생성 후 2단계가 되는 초 |
| Stage Three At | 20 | 생성 후 3단계가 되는 초 |
| Overload At | 30 | 생성 후 폭주하는 초 |
| Overload Damage | 20 | 폭주 1회 시설 피해 |
| Danger Radius | 3 | 오염 거리(m) |
| Stage Two Contamination | 3 | 2단계 초당 오염량 |
| Stage Three Contamination | 6 | 3단계 초당 오염량 |
| Age / Danger Stage / Has Overloaded | 0 / 1 / 해제 | 실행 상태, 테스트 중 관찰용 |

시간은 반드시 Stage Two At < Stage Three At < Overload At 순서로 설정합니다. 역순인 경우 코드는 각 시점 사이 최소 0.1초를 보장하지만, Inspector 값을 올바르게 입력하는 것을 권장합니다.
4. 프리팹을 저장하고 씬으로 돌아옵니다. 수동 배치한 균열에 값 Override가 있으면 그 값도 확인합니다.
5. AnomalySpawner의 Rift Prefab은 기존처럼 Project의 프리팹을 연결합니다.

WardStatus가 없거나 비활성화된 씬은 기존 DAY 3 봉인만 작동하고 성장/피해는 진행하지 않습니다.
이번 단계의 시각적 단계 표현은 UI와 Inspector입니다. 균열 모델의 크기/색/VFX는 바꾸지 않았습니다.

## 5. 시작·준비실에 안전 구역 만들기

1. 빈 GameObject를 만들고 StartRoomSafeZone으로 이름을 지정합니다.
2. SafeZone을 추가하면 BoxCollider가 함께 추가됩니다.
3. BoxCollider의 Is Trigger를 반드시 체크하고 컴포넌트는 활성 상태로 둡니다. Rigidbody는 필요 없습니다.
4. Transform Scale은 (1, 1, 1)로 두고 방 바닥 중심에 배치합니다.
5. 예를 들어 방 크기가 6m × 6m라면 BoxCollider Center = (0, 1.5, 0), Size = (6, 3, 6)으로 시작합니다.
6. Scene 뷰에서 Gizmos를 켜고 오브젝트를 선택해 초록 박스가 방 안을 덮는지 확인합니다. 실제 방 크기에 맞춰 Size를 수정합니다.
7. 준비실에도 PrepRoomSafeZone을 만들고 같은 방식으로 설정합니다.

플레이어의 PlayerContamination이 붙은 Transform 위치가 박스 안에 있어야 합니다. 발 위치가 바닥 아래에 살짝 있으면 박스 아래 면을 더 내려주세요.
플레이어 Collider가 일부 겹치는지만으로 판정하지 않습니다. 기준점이 박스에 들어오면 안전 구역입니다.
Trigger이므로 기존 조준 Raycast(QueryTriggerInteraction.Ignore)를 가리지 않습니다. 벽처럼 막힌다면 Is Trigger 설정을 확인하세요.

## 6. 상태 UI 만들기

기존 Canvas를 사용합니다. 기존 CastingPanel은 유지하고 같은 Canvas 아래에 별도 패널을 만듭니다.

```text
Canvas
  CastingPanel                 기존
  WardStatusPanel              WardStatusHUD 부착, 항상 활성
    StatusText                 Text - TextMeshPro
    WarningText                Text - TextMeshPro
    StabilityBackground        Image
      StabilityFill            Image
    ContaminationBackground    Image
      ContaminationFill        Image
  FailurePanel                 Image, 시작 시 비활성
    FailureText                Text - TextMeshPro
```

1. Canvas 우클릭 → UI → Panel을 만들고 WardStatusPanel로 이름을 바꿉니다.
2. RectTransform Anchor Presets를 좌측 상단으로 설정합니다. Pivot = (0, 1), Pos X = 20, Pos Y = -20, Width = 450, Height = 250으로 시작합니다.
3. Panel의 Image 색을 어두운 반투명으로 설정하고 Raycast Target을 해제합니다.
4. 자식에 UI → Text - TextMeshPro 두 개를 만들어 위 이름을 지정합니다. 기존 한글 표시가 되는 TMP Font Asset을 사용합니다.
5. StatusText는 좌상단 기준 (15, -10), 크기 420×110, 폰트 20으로 시작합니다. WarningText는 (15, -120), 크기 420×60, 폰트 18로 둡니다. 두 항목 모두 Pivot (0, 1), Raycast Target 해제입니다.
6. 게이지 Background 두 개를 좌상단 기준 (15, -190), (15, -220)에 만들고 크기 420×14로 둡니다. 색은 짙은 회색입니다.
7. 각 Background 아래 Fill Image를 만들고 전체 Stretch, Left/Right/Top/Bottom = 0으로 설정합니다.
8. Fill은 Source Image에 기본 UI Sprite를 넣고 Image Type = Filled, Fill Method = Horizontal, Fill Origin = Left, Fill Amount = 1로 설정합니다. Filled가 안 보이면 Source Image부터 지정하세요.
9. StabilityFill은 초록, ContaminationFill은 보라색으로 둡니다. 이미지의 Raycast Target은 모두 해제합니다.
10. WardStatusPanel에 WardStatusHUD를 추가합니다. Status에는 WardSystems의 WardStatus, 나머지 텍스트/Fill 필드에는 이름에 맞는 UI 컴포넌트를 연결합니다.
11. Canvas 바로 아래에 FailurePanel을 만듭니다. 중앙 Anchor/Pivot (0.5, 0.5), 위치 (0, 0), 크기 600×220으로 시작하고 어두운 배경을 사용합니다.
12. FailureText는 패널 안 전체 Stretch, 여백 20, 가운데 정렬, 폰트 28로 설정합니다.
13. HUD의 Failure Panel과 Failure Text에 연결한 뒤 FailurePanel만 비활성화합니다. WardStatusPanel을 끄면 UI가 갱신되지 않습니다.
14. FailurePanel을 Canvas의 마지막 자식으로 두면 다른 UI 위에 그려집니다. 씬을 저장합니다.

화면 해상도에 따라 겹치면 새 패널의 위치/크기만 조절합니다. 급하면 Fill 두 개 없이 텍스트부터 연결해도 수치와 경고는 표시됩니다.
최고 위험 단계는 씬의 활성 균열 중 최고값입니다. 위험 균열이 없으면 0입니다. 경고는 실패 → 오염 80 이상 → 안정도 20 이하 → 폭주 잔존 → 3단계 → 주변 오염 → 2단계 순으로 한 개를 표시합니다.

## 7. 순서대로 직접 실행할 테스트 (아직 미실행)

각 테스트를 새 Play로 시작합니다. 테스트 전용 값은 가능하면 Play 중 변경해 종료 시 원래대로 돌아오게 합니다.

| 테스트 | 행동 | 예상 결과 |
| --- | --- | --- |
| 초기값 | Play 시작 | 안정도 100, 오염 0, 균열 1단계 |
| 성장 | 균열에서 3m 이상 떨어져 30초 관찰 | 10초에 2단계, 20초에 3단계, 30초에 폭주 |
| 폭주 1회 | 위 상태로 30초 더 대기 | 안정도 80 유지. 같은 균열의 추가 폭주 피해 없음 |
| 근접 오염 | 안전 구역 밖, 2단계 균열 3m 안에서 2초 대기 | 오염 약 +6. 3단계는 2초에 약 +12 |
| 거리 이탈 | 오염이 생긴 뒤 3m 밖 이동 | 증가가 멈추고 값 유지 |
| 안전 회복 | 오염 20 이상에서 시작/준비실로 이동 | 안전 구역 표시, 초당 약 10 회복, 0 아래로 내려가지 않음 |
| 안전 우선 | 위험 반경과 안전 박스가 겹치게 임시 배치 | 박스 안에서는 회복만 함. 시설 폭주 피해는 발생 |
| 부분 봉인 | 첫 봉인점만 성공하고 대기 | 나머지 봉인점 유지, 성장/폭주 진행 |
| 정상 봉인 | 폭주 전에 두 봉인점을 성공시키고 원래 폭주 시간 이후 대기 | 균열 제거, 추가 오염/폭주 피해 없음 |
| 폭주 후 봉인 | 폭주 후 두 봉인점 성공 | 이미 받은 시설 피해는 유지, 균열 제거 후 오염 증가 중단 |
| 타이밍 회귀 | 충전 부족/과충전/조준 이탈 후 재시도 | 기존 실패·취소 유지, 성공만 봉인점 완료로 계산 |
| 시설 실패 | 새 Play에서 WardStatus Stability를 20으로 변경하고 멀리서 폭주 대기 | 0에서 실패 1회, 원인 표시, 이동/시점/시전 정지 |
| 오염 실패 | 새 Play에서 오염을 99로 바꾸고 위험 균열 접근 | 100에서 실패, 100 초과하지 않음 |
| 실패 후 생성 | 실패 중 Spawner 메뉴의 Spawn Rift (Play Mode) 실행 | 추가 생성 안 됨 |
| 재실행 | 실패 후 Play 종료 → 다시 Play | timeScale이 복원되고 정상 이동/시전 가능 |
| 일시정지 | Editor Pause 버튼으로 몇 초 멈췄다가 재개 | 일시정지 중 성장/피해/오염 진행 없음 |

추가 경계 확인: 두 번째 봉인 성공이 폭주 예정 프레임과 겹치면 봉인 성공을 우선 처리하도록 구현했습니다. 일반 수동 조작으로 같은 프레임을 정확히 맞추기 어려우므로 이 조건은 실행 검증 완료로 간주하지 않습니다.
안전 구역을 비활성화하거나 박스 밖으로 나가면 다음 계산부터 회복이 끝나야 합니다. 안전 구역에서 시작해도 첫 프레임부터 안전으로 인식해야 합니다.
Console에 NullReferenceException/MissingReferenceException이 없는지와 기존 VFX가 남지 않는지도 확인합니다.

## 8. 실행 순서와 제한

PlayerInteractor(10000) → MagicDetector(10001) → MagicCaster(10002) → ManaRift 완료 처리(10003) → WardStatus 위험 계산(10004) → WardStatusHUD(10005) 순서입니다.
기존 MagicHUD는 10003으로 유지합니다. Project Settings > Script Execution Order에 이 클래스들의 수동 순서가 있으면 코드의 기본 순서를 덮어쓸 수 있으므로 같은 순서로 맞추거나 수동 항목을 제거합니다.
균열은 실제 봉인점 두 개의 완료 상태도 확인하므로 Destroy가 프레임 끝으로 지연돼도 피해 대상에서 제외됩니다.

프로토타입의 작은 씬을 전제로 활성 균열/안전 구역을 매 프레임 검색합니다. 대규모 씬 최적화, 저장/불러오기, 실패 후 씬 재로드, 벽을 통한 오염 차단은 이번 범위 밖입니다.
피해/오염 계산은 프레임 단위이므로 표시값과 수동 초시계 사이에는 한 프레임 정도 차이가 생길 수 있습니다.

문제가 있으면 테스트 행 이름, 예상/실제 결과, Console 오류 전문, 해당 컴포넌트의 Inspector 설정을 알려주세요.
