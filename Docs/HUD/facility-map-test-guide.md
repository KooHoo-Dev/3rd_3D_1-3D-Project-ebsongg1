# WARD SHIFT 시설 지도 확인 가이드

## Unity 연결값

`MainGame` 씬에서 `Tools > WARD SHIFT > Setup Facility Map`을 실행하면 방 구역과 지도 좌표 기준이 연결됩니다. `DayFlowManager`의 `FacilityMapHUD`에서 다음 값이 맞는지 확인합니다.

| 항목 | 값 |
|---|---|
| Map Root | `Map_Root` |
| Reference Origin | `Room_01_ControlHall` |
| Reference Anchor Image Point | `(150, 280)` |
| Reference Pixels Per Unit | `4` |
| Reference Image Bounds Min / Max | `(30, 4)` / `(288, 509)` |
| Map Rotation Degrees | `180` |
| Map Scale Multiplier | `1` |
| Map Bounds Padding Units | `4` |
| Marker Outside Padding Units | `8` |
| Player Arrow Angle Offset | `0` |

Map Root 아래의 방 Transform을 기준으로 픽셀 단위 탑뷰를 로컬 X/Z 평면에 맞춥니다. 지도는 세로/가로 비율을 유지하며 지도 영역에 맞춰 크기를 계산합니다. `mapScaleMultiplier`는 전체 크기 조정, `mapRotationDegrees`는 평면도 방향 조정에 사용합니다.

## 플레이어 위치 확인

1. Play Mode에서 DAY가 진행 중일 때 `Tab`을 눌러 지도를 열고 다시 눌러 닫습니다. 지도는 열려 있는 동안 게임을 일시 정지합니다.
2. Player를 각 방으로 이동합니다. 마커는 방 중심에 고정되지 않고 실제 Player Transform 위치를 따라가며, 방향을 돌리면 화살표도 회전해야 합니다.
3. 씬의 방 앵커와 Player 위치를 비교할 때 아래 월드 좌표를 참고합니다. 좌표는 현재 `MainGame`의 방 앵커 Transform에서 읽은 값입니다.

| 확인 영역 | 월드 위치 (X, Y, Z) | 지도에서 볼 위치 |
|---|---:|---|
| 시작 구역 | `(-411.050, 6.020, 51.220)` | 아래쪽 |
| 중앙 복도 | `(-419.000, 2.000, 0.000)` | 중앙 |
| 정화실 | `(-441.360, 2.990, 0.070)` | 오른쪽 |
| 봉인실 | `(-399.750, 12.000, -18.700)` | 왼쪽 가지 |
| 마력핵실 | `(-419.160, 5.000, -58.000)` | 위쪽 |

앵커 높이(Y)는 평면 위치에 사용하지 않습니다. 따라서 계단을 오르내려도 X/Z가 같으면 마커는 같은 평면 좌표에 표시됩니다.

## 이상현상 위치 및 수명 확인

1. DAY 중 `AnomalySpawner` 컴포넌트의 컨텍스트 메뉴에서 `Spawn Rift (Play Mode)`를 실행합니다. 활성 이상현상의 Transform 위치에 작은 발광 마커와 한 번의 파동이 표시됩니다.
2. Play Mode에서 활성 이상현상 Transform을 각 영역의 바닥 위로 옮겨 시작 구역, 중앙 복도, 정화실, 봉인실, 마력핵실의 좌표 투영을 차례로 확인합니다. Transform을 옮기는 것은 확인용이므로 확인 뒤 Play Mode를 종료합니다.
3. 두 번째 이상현상을 생성해 마커가 각각 유지되는지 확인합니다. 하나를 진압하거나 파열시키면 해당 마커만 사라져야 합니다.
4. 마커 색은 위험 단계에 따라 노랑, 주황, 빨강으로 바뀝니다. 지도에는 이상현상 이름이나 긴 설명을 표시하지 않습니다.

이 테스트는 이상현상 Zone 라벨이 아니라 실제 `AnomalyBehaviour.transform.position`이 지도 좌표로 변환되는지를 확인합니다.

## 입력 충돌 확인

- 인벤토리를 연 상태에서 `Tab`을 눌러도 지도가 열리지 않아야 합니다. 인벤토리를 닫은 뒤 `Tab`을 누르면 지도가 열립니다.
- 보급 선택 창이 열려 있을 때 `Tab`은 무시되어야 합니다.
- DAY 종료/강화 화면에서는 `Tab`으로 지도가 열리지 않아야 합니다.
- 지도 밖에서 `Q`를 누르고 있는 동안 기존 마력 감응 강조가 동작해야 합니다. 지도는 별도 입력을 사용하며 Q 동작을 바꾸지 않습니다.
- `M` 키는 더 이상 지도를 토글하지 않습니다.

## 수동 평면도 보정 범위

씬에는 각 방과 스폰 지점의 Transform은 있지만, 바닥 면을 하나의 연결된 평면 폴리곤으로 제공하는 내비게이션/맵 데이터는 없습니다. 현재 바닥 실루엣은 첨부 탑뷰에서 구역 외곽과 복도 중심선을 읽어 단순화한 2D 평면도입니다. 현재 좌표 계산은 실제 씬의 `Map_Root`와 `Room_01_ControlHall` Transform에 고정되어 있습니다.

실제 바닥 경계와 1:1로 맞춰야 하는 경우 수동으로 지정할 데이터는 다음입니다.

- 다섯 방의 바닥 외곽점: `Assets/Scripts/UI/FacilityMapHUD.cs`의 `CreateZoneAreas()`
- 꺾이는 복도 중심선과 폭: 같은 파일의 `CreateCorridors()` 및 `CorridorWidthImagePixels`
- 계단 위치: 같은 파일의 `CreateStairMarkers()`
- 이미지 기준점/단위/외곽 범위: Inspector의 `FacilityMapHUD` 좌표 보정 항목

방 또는 복도 구조가 바뀌면 외곽점과 중심선을 탑뷰에서 다시 지정해야 합니다. 방 앵커나 지도 전체의 방향·크기 문제는 좌표 보정 항목을 조정하고, 방 윤곽 문제는 `CreateZoneAreas()`의 기준 픽셀 좌표를 조정합니다.
