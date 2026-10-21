# DAY 3 — 3차: 벽 생성 지점 연결

## 새 스크립트 역할
- AnomalySpawnPoint: 기존 SP 빈 오브젝트에 부착. 표면 종류와 점유 상태를 관리합니다.
- AnomalySpawner: GameSystems에 부착. 프리팹과 생성 지점을 선택해 균열 하나를 만듭니다.
방 이름이나 SP 이름으로 생성 가능 종류를 추측하지 않습니다. Surface 설정만 사용합니다.
현재 Wall만 지원하며 Floor/Device는 이후 확장을 위한 분류입니다.

## 1. 사용할 프리팹 확인 (Play 모드 종료 상태)
Project 창에 Assets/Prefabs/ManaRift_Test.prefab이 있습니다. 지난 단계에서 테스트한 설정이 이 프리팹에 저장되어 있는지 확인합니다.
Hierarchy에서 수정했다면 해당 인스턴스의 Overrides를 검토하고 필요한 변경을 Apply합니다.
프리팹 루트는 활성 상태이며 ManaRift 컴포넌트도 켜져 있어야 합니다.
봉인점 두 개가 활성 상태이고 Is Completed 및 Is Resolved가 꺼져 있어야 합니다.
프리팹 루트 Scale은 (1, 1, 1)을 권장합니다.
수동 배치했던 씬의 ManaRift_Test는 이후 비활성화합니다. 비활성화한 상태를 프리팹에 Apply하지 마세요.
자동 생성 관리자는 수동 배치한 균열까지 감지하지 않으므로 테스트용 인스턴스를 따로 꺼 주어야 합니다.

## 2. 기존 벽 SP 설정
Hierarchy의 SP_SEAL_Wall_01을 선택합니다. 기존 빈 오브젝트를 그대로 사용합니다.
Add Component에서 AnomalySpawnPoint를 추가합니다.
- Surface: Wall
- Surface Offset: 0.03 (벽에서 약 3cm 띄우는 값)

SP의 Position은 원하는 균열 중심 위치로 맞춥니다.
Scene 뷰의 이동 도구를 Local로 바꾸고, 파란 화살표(+Z)가 벽에서 방 안쪽을 향하도록 SP의 Rotation을 조정합니다.
균열 본체는 로컬 XY 평면에 놓이고 봉인점은 +Z 쪽에 있으므로, 이 방향이 플레이어가 보는 앞면입니다.
기존 SP가 바닥 높이에 있다면 원하는 벽 높이까지 올려 주세요.
코드는 벽 표면을 자동 검출하거나 방향을 자동 보정하지 않습니다. 실제 벽과의 정렬은 여기서 설정합니다.
Wall SP를 하나만 연결해 먼저 확인한 뒤 SP_PUR_Wall_01, SP_CORE_Wall_01에도 같은 컴포넌트를 추가할 수 있습니다.
SP에는 MagicTarget 또는 ManaRift를 붙이지 않습니다.

## 3. GameSystems 설정
Hierarchy의 기존 GameSystems를 선택하고 AnomalySpawner를 추가합니다.
- Rift Prefab: Project 창의 Assets/Prefabs/ManaRift_Test.prefab을 드래그합니다. Hierarchy의 씬 오브젝트가 아닙니다.
- Spawn Points: Size를 1로 설정하고 Element 0에 SP_SEAL_Wall_01을 드래그합니다.
- Randomize Point: 해제
- Selected Point Index: 0
- Spawn On Start: 체크
씬을 저장합니다.

Hierarchy의 역할:
GameSystems                    [AnomalySpawner]
AnomalySpawnPoints 아래 기존 SP들
  SP_SEAL_Wall_01               [AnomalySpawnPoint]
  SP_PUR_Wall_01                [선택: AnomalySpawnPoint]
  SP_CORE_Wall_01               [선택: AnomalySpawnPoint]

Play 시작 후 씬 최상위에 ManaRift_Test(Clone)이 생성되는 것이 정상입니다.
SP 자식으로 만들지 않으므로 SP 부모의 Scale에 의해 균열이 늘어나지 않습니다.
SP를 생성 후 움직여도 이미 생성된 균열은 따라가지 않습니다.

## 4. 첫 실행 확인
1. Play를 누르면 선택한 벽에 균열 하나가 생성되고 Console에 생성 지점 이름이 출력됩니다.
2. 벽 안에 묻히거나 반대로 보이면 Play를 종료하고 SP 위치/회전을 수정합니다. 필요하면 Surface Offset을 늘립니다.
3. Q 감지 → 1키 봉인 선택 → 두 봉인점 각각 타이밍 성공 → 전체 균열 제거를 확인합니다.
4. 생성 직후 GameSystems의 AnomalySpawner 컴포넌트 제목을 우클릭하여 Spawn Rift (Play Mode)를 여러 번 실행합니다.
   기존 균열이 남아 있다는 로그만 나오고 새 균열은 생성되지 않아야 합니다.
5. 진압이 끝나 루트가 제거된 뒤 같은 메뉴를 실행하면 동일 위치에 새 균열이 생성되어야 합니다.
자동 생성은 시작 시 한 번뿐입니다. 진압 후 자동으로 계속 재생성하지 않습니다.
6. 생성된 균열을 Hierarchy에서 비활성화하고 생성 메뉴를 실행해도 추가 생성되지 않아야 합니다.
   삭제한 뒤 다음 프레임에는 다시 생성할 수 있습니다.
7. 충전 중 균열을 삭제해도 Console 예외나 잔여 반복 VFX가 없어야 합니다.

## 5. 여러 위치 및 랜덤 확인
Play를 종료하고 Spawn Points에 Wall SP를 추가합니다.
Randomize Point를 끈 상태에서는 Selected Point Index 0, 1, 2를 각각 선택해 Play하며 모든 벽에서 배치를 확인합니다.
잘못된 번호/비활성 지점/이미 점유된 지점/Floor 또는 Device는 경고 후 생성하지 않습니다. 다른 지점으로 자동 대체하지 않습니다.
Randomize Point를 켜면 연결 목록 중 활성 상태이며 비어 있는 Wall에서 하나를 선택합니다.
중복 연결된 같은 SP는 무작위 후보로 한 번만 계산합니다.
각 AnomalySpawner는 자기 균열 하나만 유지하고, 같은 SP를 여러 생성기가 참조하더라도 SP의 점유 상태로 중복을 차단합니다.
DAY 3에서는 GameSystems에 생성기 하나만 사용하세요.

## 실행 흐름
AnomalySpawner.Start → 지점 선택 → AnomalySpawnPoint.TrySpawn → SP 위치/회전으로 프리팹 생성.
MagicTarget 두 개 완료 → ManaRift 완료 이벤트/VFX → 균열 루트 Destroy.
생성기와 SP가 가진 GameObject 참조는 Unity에서 파괴 후 null로 판정되므로 다음 생성 요청이 허용됩니다.
단순 비활성화는 null이 되지 않아 점유를 유지합니다.

## 검증 범위
C# 컴파일은 프로젝트의 Unity 참조 DLL을 사용하여 확인합니다. 실제 Play 테스트와 벽면 배치 검증은 위 절차로 직접 진행해야 합니다.
씬과 프리팹, 외부 에셋은 이번 코드 작업에서 수정하지 않았습니다.
성장/오염도/보상/동시 다중 이상현상은 구현하지 않았습니다.
추천 커밋 메시지: feat: spawn mana rifts at configurable wall points

두 봉인점 진압, 다른 벽에서 배치, 중복 생성 방지까지 확인하면 DAY 3의 전체 플레이 흐름 검증이 끝납니다.
