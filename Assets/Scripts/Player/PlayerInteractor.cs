using System;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>화면 중앙에서 가장 가까운 충돌 물체를 찾고 마법 대상인지 확인.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Header("조준 설정")]
        [Tooltip("화면을 렌더링하는 MainCamera를 연결. PlayerFollowCamera나 PlayerCameraRoot가 아님.")]
        [SerializeField] private Camera aimCamera;
        [Tooltip("플레이어의 최상위 오브젝트. 자신과 자식의 Collider만 검사에서 제외.")]
        [SerializeField] private Transform playerRoot;
        [SerializeField, Min(0.1f)] private float interactionDistance = 4f;
        [Tooltip("대상뿐 아니라 벽과 바닥도 포함해야 함. 처음에는 Everything을 사용.")]
        [SerializeField] private LayerMask raycastLayers = ~0;

        [Header("조준점 표시 (선택)")]
        [SerializeField] private Graphic crosshair;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color targetColor = Color.cyan;
        [SerializeField] private Color sealReadyColor = new Color(0.3f, 1f, 0.65f);

        [Header("실행 상태 확인 (Current Target은 직접 연결하지 않음)")]
        [SerializeField] private MagicTarget currentTarget;
        [SerializeField] private bool logTargetChanges;
        [SerializeField] private bool drawDebugRay = true;

        public MagicTarget CurrentTarget => currentTarget;
        public Transform PlayerRoot => playerRoot;
        public Collider CurrentCollider { get; private set; }
        public event Action<MagicTarget> TargetChanged;
        private bool sealReady;

        public void SetSealReady(bool ready)
        {
            if (sealReady == ready) return;
            sealReady = ready;
            RefreshCrosshair();
        }

        /// <summary>기존 4m 조준 검사 밖의 첫 표면이 Q로 감지된 봉인점인지 확인합니다.</summary>
        public MagicTarget GetDetectedSealBeyondRange(float maxDistance, MagicTarget chainTarget = null)
        {
            if (aimCamera == null || !aimCamera.isActiveAndEnabled
                || maxDistance <= interactionDistance) return null;
            Ray viewportRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Ray ray = new Ray(aimCamera.transform.position, viewportRay.direction);
            Collider nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, maxDistance, raycastLayers,
                         QueryTriggerInteraction.Ignore))
            {
                if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot)) continue;
                if (hit.distance >= nearestDistance) continue;
                nearest = hit.collider;
                nearestDistance = hit.distance;
            }
            if (nearest == null || nearestDistance <= interactionDistance) return null;
            MagicTarget target = nearest.GetComponentInParent<MagicTarget>();
            ManaRift rift = target != null ? target.GetComponentInParent<ManaRift>() : null;
            return target != null && target.IsAvailable && rift != null
                && target == rift.ExpectedSealPoint
                && (target.IsDetected || target == chainTarget)
                && target.RequiredMagic == MagicType.Seal ? target : null;
        }

        /// <summary>정화 홀드 중 조준점이 흔들려도 대상까지의 실제 시야가 열려 있는지 확인합니다.</summary>
        public bool HasClearPathToTarget(Collider targetCollider)
        {
            if (aimCamera == null || targetCollider == null || !targetCollider.enabled) return false;
            Vector3 origin = aimCamera.transform.position;
            Vector3 offset = targetCollider.bounds.center - origin;
            float distance = offset.magnitude;
            if (distance < 0.001f || distance > interactionDistance) return false;
            Ray ray = new Ray(origin, offset / distance);
            if (!targetCollider.Raycast(ray, out RaycastHit targetHit, distance + 0.05f)) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, distance + 0.05f,
                         raycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot)) continue;
                // Other overlapping patches share the same floor surface and must not hide the
                // selected patch from the continuous-hold line-of-sight check.
                if (hit.collider == targetCollider || hit.collider.GetComponentInParent<CursePatch>() != null)
                    continue;
                if (hit.distance < targetHit.distance - 0.015f) return false;
            }
            return true;
        }

        private void Awake()
        {
            if (playerRoot == null)
                playerRoot = transform;
            if (aimCamera == null)
                aimCamera = Camera.main;

            currentTarget = null;
            RefreshCrosshair();
            if (aimCamera == null)
            {
                Debug.LogError("PlayerInteractor: Aim Camera에 MainCamera를 연결하세요.", this);
                enabled = false;
            }
        }

        // Starter Assets의 시점 회전과 일반적인 Cinemachine 카메라 갱신 이후에 검사.
        private void LateUpdate()
        {
            if (aimCamera == null || !aimCamera.isActiveAndEnabled)
            {
                CurrentCollider = null;
                SetTarget(null);
                return;
            }

            Ray viewportRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // 가까운 벽도 차단할 수 있도록 근거리 클리핑 평면 대신 카메라 위치에서 시작.
            Ray ray = new Ray(aimCamera.transform.position, viewportRay.direction);
            RaycastHit[] hits = Physics.RaycastAll(ray, interactionDistance, raycastLayers,
                QueryTriggerInteraction.Ignore);

            CurrentCollider = null;
            float nearestBlockingDistance = float.PositiveInfinity;
            RaycastHit nearestBlockingHit = default;
            bool hasBlockingHit = false;
            foreach (RaycastHit hit in hits)
            {
                if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot))
                    continue;
                CursePatch patch = hit.collider.GetComponentInParent<CursePatch>();
                if (patch == null && hit.distance < nearestBlockingDistance)
                {
                    nearestBlockingDistance = hit.distance;
                    nearestBlockingHit = hit;
                    hasBlockingHit = true;
                }
            }

            // All overlapping patches remain candidates even if another patch is slightly in
            // front. Keep the current patch while the aim remains inside its footprint; when it
            // no longer overlaps the aim point, prefer the patch center nearest the ray contact.
            MagicTarget selectedPatchTarget = null;
            Collider selectedPatchCollider = null;
            float bestCenterDistance = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.distance > nearestBlockingDistance + 0.015f)
                    continue;
                if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot)) continue;

                CursePatch patch = hit.collider.GetComponentInParent<CursePatch>();
                MagicTarget patchTarget = patch != null ? patch.PurificationTarget : null;
                if (patch == null || patchTarget == null || !patchTarget.IsAvailable
                    || !patch.ContainsAimPoint(hit.point)) continue;

                float centerDistance = patch.AimCenterDistanceSqr(hit.point);
                if (patchTarget == currentTarget)
                {
                    selectedPatchTarget = patchTarget;
                    selectedPatchCollider = patch.AimCollider;
                    break;
                }
                if (centerDistance >= bestCenterDistance) continue;

                bestCenterDistance = centerDistance;
                selectedPatchTarget = patchTarget;
                selectedPatchCollider = patch.AimCollider;
            }

            MagicTarget candidate;
            if (selectedPatchTarget != null)
            {
                CurrentCollider = selectedPatchCollider;
                candidate = selectedPatchTarget;
            }
            else
            {
                // Non-patch targets retain their original nearest-surface occlusion rule.
                CurrentCollider = hasBlockingHit ? nearestBlockingHit.collider : null;
                candidate = CurrentCollider != null ? CurrentCollider.GetComponentInParent<MagicTarget>() : null;
            }
            SetTarget(candidate != null && candidate.IsAvailable ? candidate : null);

            if (drawDebugRay)
                Debug.DrawRay(ray.origin, ray.direction * interactionDistance,
                    currentTarget != null ? targetColor : normalColor);
        }

        private void SetTarget(MagicTarget target)
        {
            bool changed = currentTarget != target;
            currentTarget = target;
            RefreshCrosshair();
            if (!changed)
                return;

            if (logTargetChanges)
                Debug.Log(target != null ? $"조준 대상: {target.DisplayName}" : "조준 대상: 없음", this);
            TargetChanged?.Invoke(target);
        }

        private void RefreshCrosshair()
        {
            if (crosshair != null)
                crosshair.color = sealReady ? sealReadyColor
                    : currentTarget != null ? targetColor : normalColor;
        }

        private void OnDisable()
        {
            CurrentCollider = null;
            sealReady = false;
            SetTarget(null);
        }
    }
}
