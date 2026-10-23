using System;
using UnityEngine;

namespace WardShift
{
    /// <summary>저주 오염의 바닥 패치 하나와 전용 정화 대상, 시각 진행도를 관리합니다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MagicTarget))]
    public sealed class CursePatch : MonoBehaviour
    {
        [Header("패치 구성")]
        [SerializeField] private MagicTarget purificationTarget;
        [SerializeField] private Renderer patchRenderer;
        [SerializeField] private Collider aimCollider;
        [SerializeField] private ParticleSystem moteParticles;
        [SerializeField, Min(0.1f)] private float dangerRadius = 0.65f;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.02f;
        [SerializeField] private Color stageOneColor = new Color(0.055f, 0.018f, 0.09f, 0.72f);
        [SerializeField] private Color stageTwoColor = new Color(0.12f, 0.025f, 0.2f, 0.78f);
        [SerializeField] private Color stageThreeColor = new Color(0.28f, 0.025f, 0.12f, 0.86f);
        [SerializeField] private Color purifyingColor = new Color(0.72f, 0.48f, 1f, 0.5f);

        private Transform visualTransform;
        private Vector3 initialVisualScale;
        private int dangerStage = 1;
        private bool purified;
        private MaterialPropertyBlock propertyBlock;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private const float StageOneAreaScale = 0.55f;
        private const float StageTwoAreaScale = 0.78f;
        private const float StageThreeAreaScale = 1f;

        /// <summary>이 패치의 개별 정화 진행과 완료를 관리하는 MagicTarget입니다.</summary>
        public MagicTarget PurificationTarget => purificationTarget;
        /// <summary>화면 중앙 조준과 Q 감지에 쓰는 패치 Collider입니다.</summary>
        public Collider AimCollider => aimCollider;
        /// <summary>한 번 정화된 뒤에는 성장으로 다시 활성화되지 않습니다.</summary>
        public bool IsPurified => purified || (purificationTarget != null && purificationTarget.IsCompleted);
        /// <summary>현재 오염 위험으로 활성화되어 있는 패치인지 반환합니다.</summary>
        public bool IsHazardActive => isActiveAndEnabled && gameObject.activeInHierarchy
            && purificationTarget != null && purificationTarget.IsAvailable && !IsPurified;
        /// <summary>성장 및 정화 진행에 맞춘 보이는 패치의 평면 위험 반경입니다.</summary>
        public float DangerRadius
        {
            get
            {
                float progress = purificationTarget != null && purificationTarget.IsCasting
                    ? purificationTarget.Progress : 0f;
                float purificationScale = Mathf.Lerp(1f, 0.46f, progress);
                return Mathf.Max(0.1f, dangerRadius * StageAreaScale * purificationScale);
            }
        }
        /// <summary>바닥에서 패치 콜라이더 중심까지 띄울 높이입니다.</summary>
        public float SurfaceOffset => Mathf.Max(0f, surfaceOffset);

        /// <summary>패치가 정화되었을 때 소유 이상현상에 알립니다.</summary>
        public event Action<CursePatch> Purified;

        private void Awake()
        {
            ResolveReferences();
            if (patchRenderer != null)
            {
                visualTransform = patchRenderer.transform;
                initialVisualScale = visualTransform.localScale;
            }

            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (purificationTarget != null)
                purificationTarget.Completed += OnPurificationCompleted;
            if (IsPurified)
            {
                gameObject.SetActive(false);
                return;
            }
            if (moteParticles != null) moteParticles.Play(false);
            RefreshAppearance();
        }

        private void LateUpdate()
        {
            RefreshAppearance();
        }

        private void OnDisable()
        {
            if (purificationTarget != null)
            {
                purificationTarget.Completed -= OnPurificationCompleted;
                if (purificationTarget.IsCasting) purificationTarget.CancelCast();
            }
            if (moteParticles != null && moteParticles.isPlaying)
                moteParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy()
        {
            if (purificationTarget != null)
                purificationTarget.Completed -= OnPurificationCompleted;
        }

        /// <summary>패치가 화면에 보이는 원형 위험 범위 안에 위치하는지 확인합니다.</summary>
        public bool ContainsDangerPosition(Vector3 position)
        {
            if (!IsHazardActive) return false;
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            float radius = DangerRadius;
            return offset.sqrMagnitude <= radius * radius;
        }

        /// <summary>성장 단계에 맞춰 강조 색과 단계별 파티클 상태를 갱신합니다.</summary>
        public void SetDangerStage(int stage)
        {
            dangerStage = Mathf.Clamp(stage, 1, 3);
            RefreshAppearance();
        }

        /// <summary>선택된 패치의 정화 대상 활성 여부를 설정하고, 정화된 패치는 비활성 상태로 둡니다.</summary>
        public void SetStageAvailability(bool available, int stage)
        {
            SetDangerStage(stage);
            bool shouldBeActive = available && !IsPurified;
            if (gameObject.activeSelf != shouldBeActive)
                gameObject.SetActive(shouldBeActive);
        }

        private void OnPurificationCompleted(MagicTarget target)
        {
            if (target != purificationTarget || purified) return;
            purified = true;
            if (moteParticles != null)
                moteParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            Purified?.Invoke(this);
        }

        private void ResolveReferences()
        {
            if (purificationTarget == null) purificationTarget = GetComponent<MagicTarget>();
            if (patchRenderer == null)
            {
                foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
                {
                    if (candidate is ParticleSystemRenderer) continue;
                    patchRenderer = candidate;
                    break;
                }
            }
            if (aimCollider == null) aimCollider = GetComponent<Collider>();
            if (moteParticles == null) moteParticles = GetComponentInChildren<ParticleSystem>(true);
            if (visualTransform == null && patchRenderer != null)
            {
                visualTransform = patchRenderer.transform;
                initialVisualScale = visualTransform.localScale;
            }
        }

        private void RefreshAppearance()
        {
            if (purificationTarget == null || patchRenderer == null) return;
            float progress = purificationTarget.IsCasting ? purificationTarget.Progress : 0f;
            float visualScale = StageAreaScale * Mathf.Lerp(1f, 0.46f, progress);
            // Keep the aim collider fixed so shrinking visuals cannot cancel the cast.
            if (visualTransform != null)
                visualTransform.localScale = initialVisualScale * visualScale;

            // MagicTarget owns the temporary Q highlight. Restore this patch's gradual look otherwise.
            if (purificationTarget.IsDetected) return;
            Color stageColor = dangerStage >= 3 ? stageThreeColor
                : dangerStage == 2 ? stageTwoColor : stageOneColor;
            Color color = Color.Lerp(stageColor, purifyingColor, progress);
            propertyBlock ??= new MaterialPropertyBlock();
            patchRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColor, color);
            propertyBlock.SetColor(LegacyColor, color);
            propertyBlock.SetColor(EmissionColor, Color.Lerp(stageColor, Color.white, progress) * 0.35f);
            patchRenderer.SetPropertyBlock(propertyBlock);
        }

        private float StageAreaScale => dangerStage >= 3 ? StageThreeAreaScale
            : dangerStage == 2 ? StageTwoAreaScale : StageOneAreaScale;
    }
}
