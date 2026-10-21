# DAY 3 — 2차: 마력 균열 프리팹 조립

## 이번에 추가한 코드
Assets/Scripts/ManaRift.cs: 두 봉인점의 Completed 이벤트를 받아 둘 다 완료되면 균열 전체를 제거합니다.
봉인 순서는 자유입니다. 실패, 단순 비활성화, 삭제는 봉인 성공으로 세지 않습니다.
Resolved(C# 이벤트)와 On Resolved(Inspector 이벤트)는 전체 진압 시 한 번만 발생합니다.
마지막 봉인점의 완료 콜백 내부에서는 부모를 제거하지 않습니다. LateUpdate에서 전체 완료를 처리하여 기존 봉인점 VFX 구독자가 먼저 실행되게 합니다.
기존 MagicTarget, MagicCaster, MagicDetector, MagicTargetVFX는 이번 단계에서 수정하지 않았습니다.

## 1. Play 모드를 끄고 루트 생성
MainGame 씬에서 빈 GameObject를 만들고 ManaRift_Test로 이름을 바꿉니다.
Scale은 (1, 1, 1)로 두고 ManaRift 컴포넌트를 추가합니다.
우선 플레이어가 접근하기 쉬운 벽 앞에 수동 배치합니다. 자동 생성 연결은 다음 단계입니다.
루트의 로컬 +Z(파란 화살표)가 벽에서 방 안쪽을 향하도록 회전합니다.
봉인점은 루트의 +Z 쪽에 놓으므로 벽 안으로 들어가지 않는지 Scene 뷰에서 확인합니다.

Hierarchy:
ManaRift_Test                 [ManaRift]
  RiftVisual                 [MeshRenderer, Collider 없음]
  SealPoint_01                [SphereCollider, MagicTarget, MagicTargetVFX]
    CastVFXAnchor
  SealPoint_02                [SphereCollider, MagicTarget, MagicTargetVFX]
    CastVFXAnchor
  CompleteVFXAnchor           [빈 GameObject]

루트에는 MagicTarget과 Collider를 붙이지 않습니다. 봉인점끼리는 부모/자식이 아닌 형제입니다.

## 2. 임시 균열 본체
루트 아래 3D Object > Cube를 만들고 RiftVisual로 이름을 바꿉니다.
Local Position (0, 0, 0), Local Scale (0.15, 1.2, 0.05)로 시작합니다.
BoxCollider는 제거합니다. 본체 Collider가 봉인점 조준과 Q 감지를 가리지 않도록 합니다.
기존 보라색 재질을 연결합니다. 원하는 재질이 없다면 새 URP/Lit Material 하나를 만들고 Base Map 색상을 보라색으로 설정합니다.
외부 에셋 원본 Material은 변경하지 않습니다. 멋진 외형은 나중에 교체해도 됩니다.

## 3. 기존 구체 재사용
Play 모드 밖에서 정상 작동한 Test_MagicSphere를 두 번 복제합니다.
복제한 두 구체를 ManaRift_Test 아래로 옮기고 SealPoint_01, SealPoint_02로 이름을 바꿉니다.
각각 Local Position (-0.35, 0.3, 0.2), (0.35, -0.3, 0.2), Local Scale (0.25, 0.25, 0.25)로 시작합니다.
두 구체의 SphereCollider는 활성화하고 Is Trigger는 해제합니다.
각 MagicTarget 설정:
- Display Name: 봉인점 1 / 봉인점 2
- Required Magic: Seal
- Cast Duration: 2
- Use Timing Cast: 체크
- Stable Start: 0.6 / Stable End: 0.85
- Deactivate On Complete: 체크
- On Completed: 비워 둡니다. ManaRift가 코드로 구독하므로 수동 연결하지 않습니다.

각 MagicTargetVFX 설정:
- Target: 같은 구체의 MagicTarget
- Cast VFX Anchor: 같은 구체 아래의 CastVFXAnchor
- Cast Loop Prefab: Assets/WardShiftPrototype/VFX/VFX_CastLoop.prefab
- Cast Complete Prefab: Assets/WardShiftPrototype/VFX/VFX_CastComplete.prefab
- Complete Lifetime: 1.5
복제 후 Target과 Anchor가 원래 테스트 구체를 가리키지 않는지 확인합니다.
구체를 작게 만들면 자식 반복 VFX도 작아집니다. 필요하면 CastVFXAnchor의 Scale만 조정합니다.
봉인점의 발광은 기존 발광 재질을 복사해 연결하거나 URP/Lit의 Emission을 활성화해 조절할 수 있습니다.

## 4. 루트 Inspector 연결
루트 아래 빈 CompleteVFXAnchor를 생성하고 Local Position을 (0, 0, 0.25)로 둡니다.
ManaRift 컴포넌트:
- Seal Point 01: SealPoint_01
- Seal Point 02: SealPoint_02
- Complete VFX Prefab: Assets/WardShiftPrototype/VFX/VFX_CastComplete.prefab
- Complete VFX Anchor: 루트 아래 CompleteVFXAnchor
- Complete VFX Lifetime: 1.5
- On Resolved: 지금은 비워 둡니다.
- Is Resolved: 체크하지 않습니다(실행 상태 확인용).

마지막 봉인 성공 시 봉인점 완료 효과와 균열 전체 완료 효과가 각각 한 번 재생됩니다. 두 효과가 함께 보이는 것은 정상입니다.
전체 완료 효과는 부모 없이 생성되어 균열이 제거되어도 1.5초 동안 유지된 뒤 삭제됩니다.

## 5. 프리팹 저장
설정한 루트를 Project 창의 원하는 프로젝트 전용 Prefabs 폴더로 드래그하고 ManaRift 이름으로 저장합니다.
Play 모드에서 성공 후 저장하지 말고, 초기 상태에서 저장합니다.
기존 테스트 구체가 방해되면 원본 두 개를 비활성화해 둡니다. 삭제할 필요는 없습니다.
씬을 저장합니다.

## 6. 직접 실행 테스트
1. 벽 가까이 접근하고 Q 유지: 두 봉인점 모두 감지되는지 확인합니다.
2. 1키로 봉인 선택 후 첫 봉인점에 타이밍 성공: 그 구체만 사라지고 본체와 다른 구체는 남아야 합니다.
3. 남은 봉인점에 충전 부족/과충전 실패: 본체와 봉인점이 유지되고 다시 시도할 수 있어야 합니다.
4. 남은 봉인점 성공: 전체 완료 VFX, Console의 '마력 균열 진압 완료 (2/2)' 로그 한 번, 루트 제거를 확인합니다.
5. 1.5초 후 완료 VFX도 사라지고 반복 VFX가 남지 않는지 확인합니다.
6. Play를 다시 시작하고 2번 봉인점부터 완료해도 같은 결과인지 확인합니다.
7. Play를 다시 시작해 한 봉인점을 수동 비활성화하거나 삭제해 봅니다. 이것만으로 전체 진압이 되면 안 됩니다.
8. 충전 중 루트를 비활성화해 반복 VFX가 남지 않는지 확인합니다.
9. Console에 NullReferenceException/MissingReferenceException이 없는지 확인합니다.

봉인점 연결 누락, 동일 대상 중복 연결, 잘못된 부모 관계, Seal/타이밍 설정 오류가 있으면 ManaRift가 오류를 출력하고 자신을 비활성화합니다.
이 경우 Play를 종료하고 Inspector 연결을 고친 뒤 재실행합니다.

## 현재 검증 범위와 다음 단계
코드는 Unity 참조 DLL로 별도 컴파일하며 실제 Unity 조작, 벽면 외형, 감지/시전/VFX 연동은 위 절차로 확인해야 합니다.
이 단계에서 자동 생성, SpawnPoint 점유, 랜덤 생성은 아직 추가하지 않았습니다.
두 봉인점 진압 테스트가 통과하면 다음 단계에서 벽 SpawnPoint 연결 코드를 추가합니다.
추천 커밋 메시지: feat: resolve mana rifts after both seal points complete
