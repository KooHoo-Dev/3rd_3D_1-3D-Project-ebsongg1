using TMPro;
using UnityEngine;

namespace WardShift
{
    /// <summary>봉인점의 발견, 연쇄 강조, 성공과 실패 표시만 담당합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10001)]
    public sealed class SealPointVisual : MonoBehaviour
    {
        public enum State { Hidden, Discovered, Active, Queued, Completed, Failed }

        [Header("임시 연쇄 표시 색상")]
        [SerializeField] private Color discoveredColor = new Color(0.55f, 0.45f, 0.08f, 1f);
        [SerializeField] private Color activeColor = new Color(1f, 0.85f, 0.1f, 1f);
        [SerializeField] private Color completedColor = new Color(0.2f, 1f, 0.35f, 1f);
        [SerializeField] private Color failedColor = new Color(1f, 0.18f, 0.12f, 1f);

        [Header("마법진 카메라 정렬")]
        [Tooltip("마법진 VFX 자식만 연결합니다. MagicTarget/Collider가 있는 봉인점 루트는 회전시키지 않습니다.")]
        [SerializeField] private Transform billboardTarget;
        [Tooltip("비워 두면 MainCamera를 사용합니다.")]
        [SerializeField] private Camera billboardCamera;
        [Tooltip("에셋의 문양 면을 맞추는 회전 보정. 현재 마커는 X=90을 사용합니다. 앞뒤가 반대면 Y에 180을 더하세요.")]
        [SerializeField] private Vector3 billboardRotationOffset = new Vector3(90f, 0f, 0f);
        [Tooltip("월드 미터 기준 화면 오른쪽(X), 위(Y), 카메라 쪽(Z) 위치 보정입니다.")]
        [SerializeField] private Vector3 billboardPositionOffset = new Vector3(0f, 0f, 0.02f);
        [Header("봉인 순서 숫자")]
        [Tooltip("봉인점 중심에서 카메라 기준 오른쪽/위/앞으로 띄울 거리(미터).")]
        [SerializeField] private Vector3 numberOffset = new Vector3(0f, 0.42f, 0.07f);
        [Tooltip("월드 공간 숫자의 크기. 조준 Collider 크기에는 영향을 주지 않습니다.")]
        [SerializeField, Min(0.01f)] private float numberWorldScale = 0.22f;
        private Transform cachedBillboardTarget;
        private Vector3 billboardBaseLocalPosition;

        private Renderer[] renderers;
        private MaterialPropertyBlock[] originalBlocks;
        private bool[] originallyEnabled;
        private Collider[] targetColliders;
        private bool[] targetCollidersOriginallyEnabled;
        private State state;
        private bool detected;
        private Color detectionColor = Color.yellow;
        private TextMeshPro orderLabel;
        private int orderNumber;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public State CurrentState => state;

        private void Awake()
        {
            CacheRenderers();
            targetColliders = GetComponents<Collider>();
            targetCollidersOriginallyEnabled = new bool[targetColliders.Length];
            for (int i = 0; i < targetColliders.Length; i++)
                targetCollidersOriginallyEnabled[i] = targetColliders[i].enabled;
            CacheBillboardTarget();
            CreateOrderLabel();
            Apply();
        }

        private void CreateOrderLabel()
        {
            GameObject labelObject = new GameObject("SealOrderNumber");
            labelObject.transform.SetParent(transform, false);
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycast >= 0) labelObject.layer = ignoreRaycast;
            orderLabel = labelObject.AddComponent<TextMeshPro>();
            orderLabel.alignment = TextAlignmentOptions.Center;
            orderLabel.fontSize = 6f;
            orderLabel.fontStyle = FontStyles.Bold;
            orderLabel.textWrappingMode = TextWrappingModes.NoWrap;
            orderLabel.sortingOrder = 10;
            orderLabel.text = string.Empty;
            orderLabel.enabled = false;
        }

        private void CacheBillboardTarget()
        {
            cachedBillboardTarget = null;
            if (billboardTarget == null || billboardTarget == transform
                || !billboardTarget.IsChildOf(transform)) return;
            cachedBillboardTarget = billboardTarget;
            billboardBaseLocalPosition = billboardTarget.localPosition;
            // Mesh 파티클은 이미 생성된 입자도 외형 자식과 함께 회전해야 합니다.
            foreach (ParticleSystem particles in billboardTarget.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.alignment = ParticleSystemRenderSpace.Local;
            }
            foreach (Collider visualCollider in billboardTarget.GetComponentsInChildren<Collider>(true))
                visualCollider.enabled = false;
            foreach (Collider2D visualCollider in billboardTarget.GetComponentsInChildren<Collider2D>(true))
                visualCollider.enabled = false;
        }

        private void LateUpdate()
        {
            if (cachedBillboardTarget != billboardTarget) CacheBillboardTarget();
            if (billboardCamera == null) billboardCamera = Camera.main;
            if (billboardCamera == null) return;
            Transform view = billboardCamera.transform;
            if (orderLabel != null)
            {
                Transform label = orderLabel.transform;
                Vector3 towardCamera = (view.position - transform.position).normalized;
                label.position = transform.position + view.right * numberOffset.x
                    + view.up * numberOffset.y + towardCamera * numberOffset.z;
                // TextMeshPro의 앞면은 카메라 회전과 같은 방향일 때 정상적으로 읽힙니다.
                label.rotation = view.rotation;
                label.localScale = Vector3.one * numberWorldScale / transform.lossyScale.x;
            }
            if (cachedBillboardTarget == null) return;
            Vector3 anchor = transform.TransformPoint(billboardBaseLocalPosition);
            Vector3 toCamera = view.position - anchor;
            if (toCamera.sqrMagnitude < 0.000001f) return;
            Vector3 facing = toCamera.normalized;
            Vector3 position = anchor + view.right * billboardPositionOffset.x
                + view.up * billboardPositionOffset.y + facing * billboardPositionOffset.z;
            billboardTarget.SetPositionAndRotation(position,
                Quaternion.LookRotation(facing, view.up) * Quaternion.Euler(billboardRotationOffset));
        }

        public void SetDetected(bool value, Color color)
        {
            detected = value;
            detectionColor = color;
            Apply();
        }

        public void SetState(State value)
        {
            if (state == value) return;
            state = value;
            Apply();
        }

        public void SetOrder(int number)
        {
            if (orderNumber == number) return;
            orderNumber = number;
            if (orderLabel != null) orderLabel.text = number > 0 ? number.ToString() : string.Empty;
            Apply();
        }

        private void CacheRenderers()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            // 봉인점 자식으로 배치한 지속 마커의 파티클도 감지와 연쇄 상태를 따릅니다.
            originalBlocks = new MaterialPropertyBlock[renderers.Length];
            originallyEnabled = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                originalBlocks[i] = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(originalBlocks[i]);
                originallyEnabled[i] = renderers[i].enabled;
            }
        }

        private void Apply()
        {
            if (renderers == null) return;
            // The completed point keeps its MagicTarget state, but its rune and number disappear.
            bool visible = state == State.Active || state == State.Queued || state == State.Failed
                || (state == State.Discovered && detected);
            Color color = state == State.Active ? activeColor
                : state == State.Completed ? completedColor
                : state == State.Failed ? failedColor
                : state == State.Discovered || state == State.Queued ? discoveredColor : detectionColor;
            if (orderLabel != null)
            {
                orderLabel.enabled = visible && orderNumber > 0;
                orderLabel.color = color;
            }
            // 완료된 첫 점은 판정을 막고, 실패 시 ResetForNewUse 후 다시 엽니다.
            if (targetColliders != null)
                for (int i = 0; i < targetColliders.Length; i++)
                    if (targetColliders[i] != null)
                        targetColliders[i].enabled = state != State.Completed
                            && targetCollidersOriginallyEnabled[i];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!visible)
                {
                    renderer.SetPropertyBlock(originalBlocks[i]);
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = originallyEnabled[i];
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                block.SetColor(LegacyColor, color);
                block.SetColor(EmissionColor, color * (state == State.Active ? 3f : 1.5f));
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
