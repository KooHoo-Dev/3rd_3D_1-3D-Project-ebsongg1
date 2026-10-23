# WARD SHIFT — 다중 이상현상 생성 설정

## 구성 요소

- `AnomalyBehaviour`: DAY 관리, 시설 위험 계산, 보상, 생성 지점 회수에 사용하는 공통 계약입니다.
- `ManaRift`: 기존 두 봉인점, 타이밍 입력, 완료 VFX를 유지하고 공통 계약을 구현합니다. 생성 표면은 `Wall`입니다.
- `AnomalySpawnPoint`: `Wall`, `Floor`, `Device` 표면과 점유 상태를 관리합니다. 이상현상이 진압·파열·제거되면 점유를 반환합니다.
- `AnomalySpawner`: 활성 목록, 최대 동시 수, 반복 생성 타이머를 관리합니다.

## MainGame 설정

MainGame에는 `SP_PUR_Wall_01`, `SP_SEAL_Wall_01`, `SP_CORE_Wall_01` 세 벽 지점이 이미 연결되어 있습니다. `Tools > WARD SHIFT > Setup Multi Anomaly Spawning`을 실행하면 현재 씬의 생성 지점 참조를 다시 연결하고 다음 프로토타입 값을 저장합니다.

| Inspector 항목 | 값 | 동작 |
| --- | ---: | --- |
| Spawn Interval Seconds | 6 | 첫 생성 이후 반복 간격 |
| Maximum Concurrent Anomalies | 2 | 동시에 유지할 수 있는 상한 |
| First Spawn Delay Seconds | 1 | DAY 시작 후 첫 생성까지 대기 |
| Spawn On Start | 해제 | DAY 루프가 Playing에 진입할 때 스포너를 켭니다 |

기존 MainGame의 DAY 길이는 바꾸지 않습니다. `Randomize Point`가 꺼져 있으면 선택한 지점을 우선 사용하고, 점유 중이면 비어 있는 호환 지점으로 이동합니다. 따라서 첫 균열은 선택 지점에, 두 번째는 다른 벽 지점에 나타납니다. 같은 `AnomalySpawnPoint`를 여러 배열 항목이나 스포너에서 참조해도 점유 검사로 중복 생성을 막습니다.

추가 이상현상은 `AnomalyBehaviour` 구현체로 만들고 `Additional Anomaly Prefabs`에 연결하면 됩니다. 해당 구현체의 `SpawnSurface`가 Wall/Floor/Device 중 호환 지점을 선택합니다. 이번 구현에는 ManaRift만 포함합니다.

## 직접 확인

Unity 메뉴에서 `Tools > WARD SHIFT > Run Multi Anomaly Play Mode Check`를 실행합니다. 자동 검증은 테스트용 간격과 성장 시간을 Play 중에만 적용하고, 종료 후 Inspector 값을 복원합니다. 씬을 저장하지 않습니다.

수동으로 확인하려면 MainGame에서 Play를 누르고 아래 항목을 봅니다.

1. DAY가 시작된 뒤 첫 지점과 다른 벽 지점에 두 개까지 생성됩니다.
2. 첫 균열의 위험 단계가 먼저 증가하는 동안 두 번째는 자기 생성 시점부터 성장합니다.
3. 한 균열의 봉인점 두 개를 성공시켜도 다른 균열의 성장·오염은 계속됩니다.
4. 최대 동시 수 2에 도달하면 새 인스턴스가 추가되지 않습니다.
5. 하나를 진압한 뒤 그 지점이 이후 생성에 다시 선택될 수 있습니다.
6. DAY CLEAR 또는 DAY FAILED 뒤에는 스포너가 중단되고 새 균열이 생성되지 않습니다.

스포너, DAY 보상 구독, 시설 위험 및 플레이어 오염은 모두 `AnomalyBehaviour`를 사용합니다. 기존 마법 입력, 봉인 VFX, 인벤토리 보상 값은 그대로 유지됩니다.
