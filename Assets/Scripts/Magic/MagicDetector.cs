using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WardShift
{
    /// <summary>감지 키를 유지하는 동안 주변의 보이는 마법 대상을 강조.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10001)]
    public sealed class MagicDetector : MonoBehaviour
    {
        [Header("감지 설정")]
        [Tooltip("실제 화면을 렌더링하는 MainCamera를 연결.")]
        [SerializeField] private Camera senseCamera;
        [Tooltip("자신과 자식의 Collider를 제외할 플레이어 루트.")]
        [SerializeField] private Transform playerRoot;
        [SerializeField, Min(0.1f)] private float detectionRadius = 8f;
        [Tooltip("대상과 장애물을 모두 포함합니다. 처음에는 Everything으로 설정.")]
        [SerializeField] private LayerMask detectionLayers = ~0;
        [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.1f);
        [Tooltip("감지 중 물리 검사를 반복하는 간격. 기본값은 0.1초.")]
        [SerializeField, Min(0.02f)] private float scanInterval = 0.1f;

        [Header("입력")]
        [Tooltip("Starter Assets 입력과 별도로 사용하는 감지 액션. 기본 조작은 Q 유지.")]
        [SerializeField] private InputAction senseAction = new InputAction("Sense", InputActionType.Button, "<Keyboard>/q");

        [Header("실행 상태 확인")]
        [SerializeField] private bool isSensing;
        [SerializeField] private int detectedCount;
        public bool IsSensing => isSensing;
        public int DetectedCount => detectedCount;
        public float DetectionRadius => detectionRadius;
        public Camera SenseCamera => senseCamera;

        private HashSet<MagicTarget> detected = new HashSet<MagicTarget>();
        private HashSet<MagicTarget> nextDetected = new HashSet<MagicTarget>();
        private float nextScanTime;
        private PlayerInteractor interactor;
        private ManaDeviceVisuals diagnosedVisuals;
        public ManaDeviceAnomaly DiagnosedDevice => diagnosedVisuals != null
            ? diagnosedVisuals.ActiveAnomaly : null;

        private void Awake()
        {
            if (playerRoot == null) playerRoot = transform;
            interactor = GetComponent<PlayerInteractor>();
            if (senseCamera == null) senseCamera = Camera.main;
            if (senseCamera == null)
            {
                Debug.LogError("MagicDetector: Sense Camera에 MainCamera를 연결하세요.", this);
                enabled = false;
            }
        }

        private void OnEnable() => senseAction.Enable();

        private void LateUpdate()
        {
            // 일시 정지하거나 입력을 놓으면 강조를 즉시 해제.
            if (Time.timeScale <= 0f || senseCamera == null || !senseCamera.isActiveAndEnabled || !senseAction.IsPressed())
            {
                ClearDetection();
                return;
            }

            bool starting = !isSensing;
            isSensing = true;
            if (starting || Time.unscaledTime >= nextScanTime)
            {
                Scan();
                nextScanTime = Time.unscaledTime + scanInterval;
            }
        }

        private void Scan()
        {
            nextDetected.Clear();
            Vector3 origin = senseCamera.transform.position;
            // 작은 프로토타입에서는 누락 위험이 없는 배열 반환 방식으로 시작.
            foreach (Collider collider in Physics.OverlapSphere(origin, detectionRadius, detectionLayers, QueryTriggerInteraction.Ignore))
            {
                if (collider.transform.IsChildOf(playerRoot)) continue;
                MagicTarget target = collider.GetComponentInParent<MagicTarget>();
                if (target == null || !target.IsAvailable || nextDetected.Contains(target)) continue;
                if (HasLineOfSight(origin, collider, target)) nextDetected.Add(target);
            }

            foreach (MagicTarget target in detected)
                if (target != null && !nextDetected.Contains(target)) target.SetDetected(false, highlightColor);
            foreach (MagicTarget target in nextDetected)
                target.SetDetected(true, highlightColor);

            // 집합을 재사용하며 Collider가 여러 개인 대상도 한 번만 계산.
            var previous = detected;
            detected = nextDetected;
            nextDetected = previous;
            detectedCount = detected.Count;
            SelectDiagnosis();
        }

        private void SelectDiagnosis()
        {
            MagicTarget preferred = interactor != null ? interactor.CurrentTarget : null;
            MagicTarget selected = null;
            if (preferred != null && detected.Contains(preferred))
            {
                ManaDeviceVisuals preferredVisuals = preferred.GetComponentInParent<ManaDeviceVisuals>();
                if (preferredVisuals != null && preferredVisuals.ActiveAnomaly != null
                    && preferredVisuals.ActiveAnomaly.IsHazardActive) selected = preferred;
            }
            float bestScreenDistance = float.PositiveInfinity;
            if (selected == null)
            {
                foreach (MagicTarget target in detected)
                {
                    if (target == null) continue;
                    ManaDeviceVisuals visuals = target.GetComponentInParent<ManaDeviceVisuals>();
                    if (visuals == null || visuals.ActiveAnomaly == null
                        || !visuals.ActiveAnomaly.IsHazardActive) continue;
                    Vector3 screen = senseCamera.WorldToViewportPoint(target.transform.position);
                    if (screen.z <= 0f) continue;
                    float distance = (new Vector2(screen.x - 0.5f, screen.y - 0.5f)).sqrMagnitude;
                    if (distance >= bestScreenDistance) continue;
                    selected = target;
                    bestScreenDistance = distance;
                }
            }
            ManaDeviceVisuals next = selected != null
                ? selected.GetComponentInParent<ManaDeviceVisuals>() : null;
            if (next != null && (next.ActiveAnomaly == null || !next.ActiveAnomaly.IsHazardActive))
                next = null;
            if (diagnosedVisuals != null && diagnosedVisuals != next)
                diagnosedVisuals.SetDiagnosis(false, 0f);
            diagnosedVisuals = next;
            if (diagnosedVisuals != null)
                diagnosedVisuals.SetDiagnosis(true, diagnosedVisuals.ActiveAnomaly.BurstRadius);
        }

        private bool HasLineOfSight(Vector3 origin, Collider collider, MagicTarget target)
        {
            Vector3 direction = collider.bounds.center - origin;
            if (direction.sqrMagnitude < 0.0001f) return false;
            Collider closest = null;
            float nearest = float.PositiveInfinity;
            // 가장 가까운 충돌이 해당 대상이어야 함. 벽 뒤의 대상은 강조하지 않음.
            foreach (RaycastHit hit in Physics.RaycastAll(origin, direction.normalized, detectionRadius,
                         detectionLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(playerRoot) || hit.distance >= nearest) continue;
                closest = hit.collider;
                nearest = hit.distance;
            }
            return closest != null && closest.GetComponentInParent<MagicTarget>() == target;
        }

        private void ClearDetection()
        {
            foreach (MagicTarget target in detected)
                if (target != null) target.SetDetected(false, highlightColor);
            detected.Clear();
            nextDetected.Clear();
            detectedCount = 0;
            isSensing = false;
            if (diagnosedVisuals != null) diagnosedVisuals.SetDiagnosis(false, 0f);
            diagnosedVisuals = null;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 창 전환 시 눌린 입력 상태까지 초기화하여 강조가 남지 않도록 함.
            if (!hasFocus)
            {
                senseAction.Disable();
                ClearDetection();
            }
            else if (isActiveAndEnabled) senseAction.Enable();
        }

        private void OnDisable()
        {
            senseAction.Disable();
            ClearDetection();
        }

        private void OnDestroy() => senseAction.Dispose();

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = highlightColor;
            Gizmos.DrawWireSphere(senseCamera != null ? senseCamera.transform.position : transform.position, detectionRadius);
        }
    }
}
