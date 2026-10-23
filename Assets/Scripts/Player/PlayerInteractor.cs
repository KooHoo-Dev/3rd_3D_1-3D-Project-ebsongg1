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

        [Header("실행 상태 확인 (Current Target은 직접 연결하지 않음)")]
        [SerializeField] private MagicTarget currentTarget;
        [SerializeField] private bool logTargetChanges;
        [SerializeField] private bool drawDebugRay = true;

        public MagicTarget CurrentTarget => currentTarget;
        public Transform PlayerRoot => playerRoot;
        public Collider CurrentCollider { get; private set; }
        public event Action<MagicTarget> TargetChanged;

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
            float nearestDistance = float.PositiveInfinity;
            RaycastHit nearestHit = default;
            foreach (RaycastHit hit in hits)
            {
                if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot))
                    continue;
                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestHit = hit;
                    CurrentCollider = hit.collider;
                }
            }

            // Coplanar CursePatch aim boxes can still overlap at their edges. RaycastAll
            // does not guarantee hit order, so keep the closest patch center as the winner.
            MagicTarget nearestTarget = CurrentCollider != null
                ? CurrentCollider.GetComponentInParent<MagicTarget>()
                : null;
            if (nearestTarget != null && nearestTarget.GetComponent<CursePatch>() != null)
            {
                const float coplanarTieDistance = 0.02f;
                bool keptCurrentTarget = false;
                if (currentTarget != null && currentTarget.IsAvailable
                    && currentTarget.GetComponent<CursePatch>() != null)
                {
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.collider == null || Mathf.Abs(hit.distance - nearestDistance) > coplanarTieDistance)
                            continue;
                        if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot))
                            continue;
                        if (hit.collider.GetComponentInParent<MagicTarget>() != currentTarget)
                            continue;

                        nearestTarget = currentTarget;
                        CurrentCollider = hit.collider;
                        keptCurrentTarget = true;
                        break;
                    }
                }

                // Keep a still-hit current patch during a cast; otherwise choose the nearest center.
                if (!keptCurrentTarget)
                {
                    float bestCenterDistance = PlanarDistanceSqr(nearestHit.point, nearestTarget.transform.position);
                    int bestInstanceId = nearestTarget.GetInstanceID();
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.collider == null || Mathf.Abs(hit.distance - nearestDistance) > coplanarTieDistance)
                            continue;
                        if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot))
                            continue;

                        MagicTarget patchTarget = hit.collider.GetComponentInParent<MagicTarget>();
                        if (patchTarget == null || patchTarget.GetComponent<CursePatch>() == null)
                            continue;

                        float centerDistance = PlanarDistanceSqr(hit.point, patchTarget.transform.position);
                        int instanceId = patchTarget.GetInstanceID();
                        if (centerDistance > bestCenterDistance
                            || (Mathf.Approximately(centerDistance, bestCenterDistance) && instanceId >= bestInstanceId))
                            continue;

                        bestCenterDistance = centerDistance;
                        bestInstanceId = instanceId;
                        nearestTarget = patchTarget;
                        CurrentCollider = hit.collider;
                    }
                }
            }

            // 가장 가까운 표면만 확인하여 벽 뒤의 대상이 선택되지 않도록 함.
            MagicTarget candidate = CurrentCollider != null
                ? CurrentCollider.GetComponentInParent<MagicTarget>()
                : null;
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

        private static float PlanarDistanceSqr(Vector3 point, Vector3 center)
        {
            point.y = 0f;
            center.y = 0f;
            return (point - center).sqrMagnitude;
        }

        private void RefreshCrosshair()
        {
            if (crosshair != null)
                crosshair.color = currentTarget != null ? targetColor : normalColor;
        }

        private void OnDisable()
        {
            CurrentCollider = null;
            SetTarget(null);
        }
    }
}
