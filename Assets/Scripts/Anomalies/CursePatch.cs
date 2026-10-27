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
        [Header("패치 외형 (선택)")]
        [Tooltip("지정하면 기본 원형 MeshRenderer 대신 이 프리팹을 패치 외형으로 표시합니다. Idle VFX 슬롯과 같은 프리팹이면 한 인스턴스만 생성합니다.")]
        [SerializeField] private GameObject patchAppearancePrefab;
        [SerializeField, Min(0f)] private float patchAppearanceScale = 1f;
        [Tooltip("VFX 크기와 독립적인 패치 로컬 조준 반경입니다. 0이면 기존 Target Collider 크기를 유지합니다. 정화 중에는 Collider가 줄지 않습니다.")]
        [SerializeField, Min(0f)] private float patchAppearanceAimRadius;
        [Header("개별 패치 VFX (선택)")]
        [Tooltip("패치가 활성 상태인 동안 반복 재생할 오염 효과입니다.")]
        [SerializeField] private GameObject idleVfxPrefab;
        [Tooltip("이 패치의 정화가 진행되는 동안 반복 재생할 효과입니다.")]
        [SerializeField] private GameObject purifyingVfxPrefab;
        [Tooltip("이 패치가 정화 완료되는 순간 월드에 남길 효과입니다.")]
        [SerializeField] private GameObject purifiedVfxPrefab;
        [Tooltip("비워 두면 패치 루트 위치를 사용합니다. 지정하면 세 효과의 위치 기준으로 사용합니다.")]
        [SerializeField] private Transform vfxAnchor;
        [SerializeField, Min(0.1f)] private float purifiedVfxLifetime = 2f;
        [SerializeField, Min(0.1f)] private float dangerRadius = 0.65f;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.02f;
        [SerializeField] private Color stageOneColor = new Color(0.055f, 0.018f, 0.09f, 0.72f);
        [SerializeField] private Color stageTwoColor = new Color(0.12f, 0.025f, 0.2f, 0.78f);
        [SerializeField] private Color stageThreeColor = new Color(0.28f, 0.025f, 0.12f, 0.86f);
        [SerializeField] private Color purifyingColor = new Color(0.72f, 0.48f, 1f, 0.5f);

        private Transform visualTransform;
        private Vector3 initialVisualScale;
        private Vector3 initialAimBoxSize;
        private float initialAimSphereRadius;
        private int dangerStage = 1;
        private bool purified;
        private MaterialPropertyBlock propertyBlock;
        private GameObject idleVfxInstance;
        private GameObject purifyingVfxInstance;
        private GameObject patchAppearanceInstance;
        private Vector3 initialPatchAppearanceScale;

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
            if (aimCollider is BoxCollider aimBox) initialAimBoxSize = aimBox.size;
            if (aimCollider is SphereCollider aimSphere) initialAimSphereRadius = aimSphere.radius;
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
            RefreshPatchRendererVisibility();
            if (purificationTarget != null)
            {
                purificationTarget.Completed += OnPurificationCompleted;
                purificationTarget.CastStarted += StartPurifyingVfx;
                purificationTarget.CastStopped += StopPurifyingVfx;
            }
            if (IsPurified)
            {
                gameObject.SetActive(false);
                return;
            }
            if (moteParticles != null) moteParticles.Play(false);
            StartPatchAppearance();
            StartIdleVfx();
            if (purificationTarget != null && purificationTarget.IsCasting) StartPurifyingVfx();
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
                purificationTarget.CastStarted -= StartPurifyingVfx;
                purificationTarget.CastStopped -= StopPurifyingVfx;
                if (purificationTarget.IsCasting) purificationTarget.CancelCast();
            }
            StopPurifyingVfx();
            StopIdleVfx();
            StopPatchAppearance();
            if (moteParticles != null && moteParticles.isPlaying)
                moteParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDestroy()
        {
            if (purificationTarget != null)
            {
                purificationTarget.Completed -= OnPurificationCompleted;
                purificationTarget.CastStarted -= StartPurifyingVfx;
                purificationTarget.CastStopped -= StopPurifyingVfx;
            }
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
            StopPurifyingVfx();
            StopIdleVfx();
            StopPatchAppearance();
            MagicVfxUtility.SpawnDetached(purifiedVfxPrefab, VfxAnchor.position,
                VfxAnchor.rotation, purifiedVfxLifetime);
            Purified?.Invoke(this);
        }

        /// <summary>DAY 실패 등으로 패치 효과를 정리할 때 호출합니다.</summary>
        public void StopEffectsForDayEnd()
        {
            if (purificationTarget != null && purificationTarget.IsCasting)
                purificationTarget.CancelCast();
            StopPurifyingVfx();
            StopIdleVfx();
            StopPatchAppearance();
            if (moteParticles != null && moteParticles.isPlaying)
                moteParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void StartIdleVfx()
        {
            if (idleVfxInstance != null || idleVfxPrefab == null) return;
            if (patchAppearancePrefab != null && idleVfxPrefab == patchAppearancePrefab
                && patchAppearanceInstance != null)
            {
                idleVfxInstance = patchAppearanceInstance;
                return;
            }
            idleVfxInstance = MagicVfxUtility.SpawnAttached(idleVfxPrefab, VfxAnchor);
        }

        private void StopIdleVfx()
        {
            if (idleVfxInstance != patchAppearanceInstance)
                MagicVfxUtility.StopAndDestroy(idleVfxInstance);
            idleVfxInstance = null;
        }

        private void StartPatchAppearance()
        {
            if (patchAppearanceInstance != null || patchAppearancePrefab == null) return;
            patchAppearanceInstance = MagicVfxUtility.SpawnAttached(patchAppearancePrefab, transform);
            if (patchAppearanceInstance != null)
                initialPatchAppearanceScale = patchAppearanceInstance.transform.localScale;
            RefreshPatchRendererVisibility();
        }

        private void StopPatchAppearance()
        {
            if (idleVfxInstance == patchAppearanceInstance) idleVfxInstance = null;
            MagicVfxUtility.StopAndDestroy(patchAppearanceInstance);
            patchAppearanceInstance = null;
            RefreshPatchRendererVisibility();
        }

        private void RefreshPatchRendererVisibility()
        {
            if (patchRenderer != null)
                patchRenderer.enabled = patchAppearancePrefab == null;
        }

        private void StartPurifyingVfx()
        {
            if (!isActiveAndEnabled || purificationTarget == null || !purificationTarget.IsCasting
                || purifyingVfxInstance != null || purifyingVfxPrefab == null) return;
            purifyingVfxInstance = MagicVfxUtility.SpawnAttached(purifyingVfxPrefab, VfxAnchor);
        }

        private void StopPurifyingVfx()
        {
            MagicVfxUtility.StopAndDestroy(purifyingVfxInstance);
            purifyingVfxInstance = null;
        }

        private Transform VfxAnchor => vfxAnchor != null ? vfxAnchor : transform;

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
            if (purificationTarget == null) return;
            float progress = purificationTarget.IsCasting ? purificationTarget.Progress : 0f;
            float visualScale = StageAreaScale * Mathf.Lerp(1f, 0.46f, progress);
            RefreshAimColliderFootprint();
            // Keep the aim collider fixed so shrinking visuals cannot cancel the cast.
            if (visualTransform != null)
                visualTransform.localScale = initialVisualScale * visualScale;
            if (patchAppearanceInstance != null)
                patchAppearanceInstance.transform.localScale = initialPatchAppearanceScale
                    * (visualScale * Mathf.Max(0f, patchAppearanceScale));

            // MagicTarget owns the temporary Q highlight. Restore this patch's gradual look otherwise.
            if (patchRenderer == null || purificationTarget.IsDetected) return;
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

        private void RefreshAimColliderFootprint()
        {
            if (aimCollider == null) return;
            float aimRadius = patchAppearancePrefab != null
                ? Mathf.Max(0f, patchAppearanceAimRadius) * StageAreaScale
                : 0f;

            if (aimCollider is BoxCollider box)
            {
                box.size = new Vector3(
                    Mathf.Max(initialAimBoxSize.x, aimRadius * 2f),
                    initialAimBoxSize.y,
                    Mathf.Max(initialAimBoxSize.z, aimRadius * 2f));
            }
            else if (aimCollider is SphereCollider sphere)
            {
                sphere.radius = Mathf.Max(initialAimSphereRadius, aimRadius);
            }
        }

        private float StageAreaScale => dangerStage >= 3 ? StageThreeAreaScale
            : dangerStage == 2 ? StageTwoAreaScale : StageOneAreaScale;
    }
}
