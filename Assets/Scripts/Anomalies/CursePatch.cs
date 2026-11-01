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
        [Tooltip("원래 외형 크기에서 정화할 수 있는 평면 반경입니다. 0이면 패치 Renderer 또는 Collider에서 반경을 계산합니다.")]
        [SerializeField, Min(0f)] private float patchAppearanceAimRadius;
        [Header("개별 패치 VFX (선택)")]
        [Tooltip("패치가 활성 상태인 동안 반복 재생할 오염 효과입니다.")]
        [SerializeField] private GameObject idleVfxPrefab;
        [Tooltip("이 패치의 정화가 진행되는 동안 반복 재생할 효과입니다.")]
        [SerializeField] private GameObject purifyingVfxPrefab;
        [Tooltip("정화 중 작은 입자를 표시할 프리팹. 비워 두면 청록색 입자를 간단히 생성합니다.")]
        [SerializeField] private GameObject purifyingMotesVfxPrefab;
        [SerializeField] private Color purifyingMoteColor = new Color(0.25f, 0.95f, 0.88f, 0.9f);
        [SerializeField, Min(0f)] private float purifyingMoteRate = 12f;
        [SerializeField, Min(0.005f)] private float purifyingMoteSize = 0.035f;
        [SerializeField, Min(0.01f)] private float purifyingMoteRadius = 0.2f;
        [Tooltip("이 패치가 정화 완료되는 순간 월드에 남길 효과입니다.")]
        [SerializeField] private GameObject purifiedVfxPrefab;
        [Tooltip("비워 두면 패치 루트 위치를 사용합니다. 지정하면 세 효과의 위치 기준으로 사용합니다.")]
        [SerializeField] private Transform vfxAnchor;
        [SerializeField, Min(0.1f)] private float purifiedVfxLifetime = 1.25f;
        [SerializeField, Min(0.1f)] private float purifiedVfxSizeMultiplier = 1f;
        [Tooltip("완료 프리팹이 비어 있을 때 사용할 작은 입자 파동의 크기와 지속 시간입니다.")]
        [SerializeField, Min(1)] private int purifiedFallbackParticleCount = 10;
        [SerializeField, Min(0.005f)] private float purifiedFallbackParticleSize = 0.035f;
        [SerializeField, Min(0.1f)] private float purifiedFallbackParticleLifetime = 0.65f;
        [SerializeField, Min(0f)] private float purifiedFallbackParticleSpeed = 0.7f;
        [SerializeField, Min(0.1f)] private float dangerRadius = 0.65f;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.02f;
        [Tooltip("단계별 색상입니다. 파티클은 원본 텍스처 밝기를 보존하도록 RGB 색조와 알파를 적용합니다.")]
        [SerializeField] private Color stageOneColor = new Color(0.055f, 0.018f, 0.09f, 0.72f);
        [SerializeField] private Color stageTwoColor = new Color(0.18f, 0.025f, 0.18f, 0.82f);
        [SerializeField] private Color stageThreeColor = new Color(0.28f, 0.025f, 0.12f, 0.86f);
        [SerializeField] private Color purifyingColor = new Color(0.72f, 0.48f, 1f, 0.5f);
        [Header("성장 단계 색상 전환 및 맥동")]
        [Tooltip("성장 단계가 바뀔 때 기본 패치와 파티클 색상이 전환되는 시간입니다.")]
        [SerializeField, Min(0f)] private float stageColorTransitionSeconds = 0.45f;
        [Tooltip("맥동 속도는 초당 반복 횟수(Hz), 강도는 기본 크기 기준 ±배율입니다.")]
        [SerializeField] private bool stageOnePulseEnabled;
        [SerializeField, Min(0f)] private float stageOnePulseSpeed = 0.25f;
        [SerializeField, Range(0f, 0.2f)] private float stageOnePulseIntensity = 0.02f;
        [SerializeField] private bool stageTwoPulseEnabled = true;
        [SerializeField, Min(0f)] private float stageTwoPulseSpeed = 0.45f;
        [SerializeField, Range(0f, 0.2f)] private float stageTwoPulseIntensity = 0.05f;
        [SerializeField] private bool stageThreePulseEnabled = true;
        [SerializeField, Min(0f)] private float stageThreePulseSpeed = 0.8f;
        [SerializeField, Range(0f, 0.2f)] private float stageThreePulseIntensity = 0.08f;

        private Transform visualTransform;
        private Vector3 initialVisualScale;
        private Vector3 initialAimBoxSize;
        private float initialMoteShapeRadius;
        private bool hasCircularMoteShape;
        private int dangerStage = 1;
        private bool purified;
        private MaterialPropertyBlock propertyBlock;
        private GameObject idleVfxInstance;
        private GameObject purifyingVfxInstance;
        private GameObject purifyingMotesInstance;
        private GameObject patchAppearanceInstance;
        private Vector3 initialPatchAppearanceScale;
        private Renderer[] patchAppearanceRenderers;
        private MaterialPropertyBlock[] patchAppearanceBlocks;
        private Vector3 initialIdleVfxScale;
        private Renderer[] idleVfxRenderers;
        private MaterialPropertyBlock[] idleVfxBlocks;
        private float currentGrowthScale = 1f;
        private float targetGrowthScale = 1f;
        private float growthScaleTransitionStart = 1f;
        private float growthScaleTransitionSeconds;
        private float growthScaleTransitionElapsed;
        private float purificationAreaMultiplier = 1f;
        private float damageAreaMultiplier = 1f;
        private float fallbackAimRadius;
        private Color currentStageColor;
        private Color stageColorTransitionStart;
        private Color targetStageColor;
        private float stageColorTransitionElapsed;
        private bool hasAppearanceTint;
        private Color lastAppearanceTint;
        private bool hasIdleTint;
        private Color lastIdleTint;
        private float pulsePhaseRadians;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int ParticleTintColor = Shader.PropertyToID("_TintColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
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
                return Mathf.Max(0.1f, dangerRadius * currentGrowthScale
                    * damageAreaMultiplier * purificationScale);
            }
        }
        /// <summary>시각 패치와 맞춘 평면 정화 조준 반경입니다.</summary>
        public float AimRadius => GetBaseAimRadius() * currentGrowthScale * purificationAreaMultiplier;
        /// <summary>바닥에서 패치 콜라이더 중심까지 띄울 높이입니다.</summary>
        public float SurfaceOffset => Mathf.Max(0f, surfaceOffset);

        /// <summary>패치가 정화되었을 때 소유 이상현상에 알립니다.</summary>
        public event Action<CursePatch> Purified;

        private void Awake()
        {
            ResolveReferences();
            if (aimCollider is BoxCollider initialBox) initialAimBoxSize = initialBox.size;
            if (patchRenderer != null)
            {
                visualTransform = patchRenderer.transform;
                initialVisualScale = visualTransform.localScale;
                Vector3 rootScale = transform.lossyScale;
                fallbackAimRadius = Mathf.Max(
                    patchRenderer.bounds.extents.x / Mathf.Max(0.0001f, Mathf.Abs(rootScale.x)),
                    patchRenderer.bounds.extents.z / Mathf.Max(0.0001f, Mathf.Abs(rootScale.z)));
            }
            if (fallbackAimRadius < 0.1f)
            {
                if (aimCollider is BoxCollider fallbackBox)
                    fallbackAimRadius = Mathf.Max(fallbackBox.size.x, fallbackBox.size.z) * 0.5f;
                else if (aimCollider is SphereCollider aimSphere)
                    fallbackAimRadius = aimSphere.radius;
            }
            if (moteParticles != null)
            {
                ParticleSystem.ShapeModule shape = moteParticles.shape;
                hasCircularMoteShape = shape.enabled && shape.shapeType == ParticleSystemShapeType.Circle;
                if (hasCircularMoteShape) initialMoteShapeRadius = shape.radius;
            }

            currentStageColor = stageOneColor;
            stageColorTransitionStart = currentStageColor;
            targetStageColor = currentStageColor;
            pulsePhaseRadians = Mathf.Repeat(GetInstanceID() * 0.6180339887f, 1f) * Mathf.PI * 2f;
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
            AdvanceGrowthScaleTransition();
            AdvanceStageColorTransition();
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

        /// <summary>CharacterController 캡슐의 바닥 투영 면적이 패치 원형 범위와 겹치는지 확인합니다.</summary>
        public bool ContainsDangerFootprint(CharacterController controller, float contactMargin)
        {
            if (!IsHazardActive || controller == null || !controller.enabled || !controller.isGrounded) return false;

            Bounds footprint = controller.bounds;
            Vector3 offset = footprint.center - transform.position;
            offset.y = 0f;
            float footprintRadius = Mathf.Max(footprint.extents.x, footprint.extents.z);
            float margin = Mathf.Max(0f, contactMargin);
            float combinedRadius = DangerRadius + footprintRadius + margin;
            if (offset.sqrMagnitude > combinedRadius * combinedRadius) return false;

            // 패치 높이와 발바닥 높이를 비교하여 공중에서 패치 위를 지날 때는 접촉으로 보지 않습니다.
            float feetToPatchHeight = Mathf.Abs(footprint.min.y - transform.position.y);
            float verticalTolerance = Mathf.Max(0.05f, controller.skinWidth) + margin;
            return feetToPatchHeight <= verticalTolerance;
        }

        /// <summary>성장 단계에 맞춰 강조 색과 단계별 파티클 상태를 갱신합니다.</summary>
        public void SetDangerStage(int stage)
        {
            int nextStage = Mathf.Clamp(stage, 1, 3);
            if (dangerStage == nextStage) return;
            dangerStage = nextStage;
            stageColorTransitionStart = currentStageColor;
            targetStageColor = GetStageColor(nextStage);
            stageColorTransitionElapsed = 0f;
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy || stageColorTransitionSeconds <= 0f)
                currentStageColor = targetStageColor;
            RefreshAppearance();
        }

        /// <summary>소유 이상현상의 단계 크기와 정화/피해 범위를 적용합니다.</summary>
        public void SetGrowthProfile(int stage, float sizeMultiplier, float transitionSeconds,
            float purifyRangeMultiplier, float contaminationRangeMultiplier)
        {
            SetDangerStage(stage);
            purificationAreaMultiplier = Mathf.Max(0.1f, purifyRangeMultiplier);
            damageAreaMultiplier = Mathf.Max(0.1f, contaminationRangeMultiplier);
            float nextScale = Mathf.Max(0.1f, sizeMultiplier);
            if (Mathf.Approximately(nextScale, targetGrowthScale))
            {
                RefreshAppearance();
                return;
            }

            growthScaleTransitionStart = currentGrowthScale;
            targetGrowthScale = nextScale;
            growthScaleTransitionSeconds = Mathf.Max(0f, transitionSeconds);
            growthScaleTransitionElapsed = 0f;

            // Inactive templates have no visible transition to animate. Set them to the current
            // stage before activation so newly spawned patches never appear at the old size.
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy || growthScaleTransitionSeconds <= 0f)
            {
                currentGrowthScale = targetGrowthScale;
                growthScaleTransitionSeconds = 0f;
            }
            RefreshAppearance();
        }

        /// <summary>정화 레이 접촉점이 패치의 원형 평면 영역 안에 있는지 확인합니다.</summary>
        public bool ContainsAimPoint(Vector3 worldPoint)
        {
            if (!IsHazardActive || AimRadius <= 0f) return false;
            float radius = AimRadius;
            return AimCenterDistanceSqr(worldPoint) <= radius * radius;
        }

        public float AimCenterDistanceSqr(Vector3 worldPoint)
        {
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            return localPoint.x * localPoint.x + localPoint.z * localPoint.z;
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
            Transform completionAnchor = VfxAnchor;
            Vector3 completionPosition = completionAnchor.position;
            Quaternion completionRotation = completionAnchor.rotation;
            if (moteParticles != null)
                moteParticles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            StopPurifyingVfx();
            StopIdleVfx();
            StopPatchAppearance();
            if (purifiedVfxPrefab != null)
            {
                GameObject effect = MagicVfxUtility.SpawnDetached(purifiedVfxPrefab,
                    completionPosition, completionRotation, purifiedVfxLifetime, true);
                if (effect != null)
                {
                    effect.transform.localScale *= Mathf.Max(0.1f, purifiedVfxSizeMultiplier);
                    MagicVfxUtility.UseUnscaledParticleTime(effect);
                }
            }
            else
            {
                MagicVfxUtility.SpawnParticleBurst(completionPosition, completionRotation,
                    purifyingMoteColor, purifiedFallbackParticleCount, purifiedFallbackParticleSize,
                    purifiedFallbackParticleLifetime, purifiedFallbackParticleSpeed, 0.035f);
            }
            AudioService.Play3DAt(WardShiftSoundId.CursePatchPurified,
                completionPosition, GetInstanceID());
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
            if (idleVfxInstance != null)
                initialIdleVfxScale = idleVfxInstance.transform.localScale;
            if (idleVfxInstance != null && idleVfxInstance != patchAppearanceInstance)
                CacheVfxRenderers(idleVfxInstance, out idleVfxRenderers, out idleVfxBlocks);
        }

        private void StopIdleVfx()
        {
            if (idleVfxInstance != patchAppearanceInstance)
                MagicVfxUtility.StopAndDestroy(idleVfxInstance);
            idleVfxInstance = null;
            initialIdleVfxScale = Vector3.one;
            idleVfxRenderers = null;
            idleVfxBlocks = null;
            hasIdleTint = false;
        }

        private void StartPatchAppearance()
        {
            if (patchAppearanceInstance != null || patchAppearancePrefab == null) return;
            patchAppearanceInstance = MagicVfxUtility.SpawnAttached(patchAppearancePrefab, transform);
            if (patchAppearanceInstance != null)
            {
                initialPatchAppearanceScale = patchAppearanceInstance.transform.localScale;
                CacheVfxRenderers(patchAppearanceInstance, out patchAppearanceRenderers, out patchAppearanceBlocks);
            }
            RefreshPatchRendererVisibility();
        }

        private void StopPatchAppearance()
        {
            if (idleVfxInstance == patchAppearanceInstance) idleVfxInstance = null;
            MagicVfxUtility.StopAndDestroy(patchAppearanceInstance);
            patchAppearanceInstance = null;
            patchAppearanceRenderers = null;
            patchAppearanceBlocks = null;
            hasAppearanceTint = false;
            RefreshPatchRendererVisibility();
        }

        private void RefreshPatchRendererVisibility()
        {
            if (patchRenderer != null)
                patchRenderer.enabled = patchAppearancePrefab == null;
        }

        private void StartPurifyingVfx()
        {
            if (!isActiveAndEnabled || purificationTarget == null || !purificationTarget.IsCasting) return;
            if (purifyingVfxInstance == null && purifyingVfxPrefab != null)
                purifyingVfxInstance = MagicVfxUtility.SpawnAttached(purifyingVfxPrefab, VfxAnchor);
            if (purifyingMotesInstance == null)
            {
                purifyingMotesInstance = purifyingMotesVfxPrefab != null
                    ? MagicVfxUtility.SpawnAttached(purifyingMotesVfxPrefab, VfxAnchor)
                    : MagicVfxUtility.SpawnPurificationMotesAttached(VfxAnchor, purifyingMoteColor,
                        purifyingMoteRate, purifyingMoteSize, purifyingMoteRadius);
            }
        }

        private void StopPurifyingVfx()
        {
            MagicVfxUtility.StopAndDestroy(purifyingVfxInstance);
            purifyingVfxInstance = null;
            MagicVfxUtility.StopAndDestroy(purifyingMotesInstance);
            purifyingMotesInstance = null;
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
            float visualScale = currentGrowthScale * Mathf.Lerp(1f, 0.46f, progress) * GetPulseScale();
            RefreshAimColliderFootprint();
            // Keep the aim collider fixed so shrinking visuals cannot cancel the cast.
            if (visualTransform != null)
                visualTransform.localScale = ScaleFootprint(initialVisualScale, visualScale);
            if (patchAppearanceInstance != null)
                patchAppearanceInstance.transform.localScale = ScaleVfxFootprint(
                    initialPatchAppearanceScale * Mathf.Max(0f, patchAppearanceScale),
                    patchAppearanceInstance.transform, visualScale);
            if (idleVfxInstance != null && idleVfxInstance != patchAppearanceInstance)
                idleVfxInstance.transform.localScale = ScaleVfxFootprint(
                    initialIdleVfxScale, idleVfxInstance.transform, visualScale);
            if (moteParticles != null && hasCircularMoteShape)
            {
                ParticleSystem.ShapeModule shape = moteParticles.shape;
                shape.radius = initialMoteShapeRadius * visualScale;
            }

            Color color = Color.Lerp(currentStageColor, purifyingColor, progress);
            UpdateVfxTint(patchAppearanceRenderers, patchAppearanceBlocks,
                ref hasAppearanceTint, ref lastAppearanceTint, color);
            UpdateVfxTint(idleVfxRenderers, idleVfxBlocks, ref hasIdleTint, ref lastIdleTint, color);

            // MagicTarget owns the temporary Q highlight for the fallback mesh renderer.
            if (patchRenderer == null || purificationTarget.IsDetected) return;
            propertyBlock ??= new MaterialPropertyBlock();
            patchRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColor, color);
            propertyBlock.SetColor(LegacyColor, color);
            propertyBlock.SetColor(EmissionColor, Color.Lerp(currentStageColor, Color.white, progress) * 0.35f);
            patchRenderer.SetPropertyBlock(propertyBlock);
        }

        private void RefreshAimColliderFootprint()
        {
            if (aimCollider == null) return;
            // The Collider covers the configured circular footprint; PlayerInteractor also
            // checks the hit point against that circle so square corners remain untargetable.
            float aimRadius = AimRadius;

            if (aimCollider is BoxCollider box)
            {
                box.size = new Vector3(aimRadius * 2f, initialAimBoxSize.y, aimRadius * 2f);
            }
            else if (aimCollider is SphereCollider sphere)
            {
                sphere.radius = aimRadius;
            }
        }

        private float GetBaseAimRadius()
        {
            return patchAppearanceAimRadius > 0f
                ? patchAppearanceAimRadius : Mathf.Max(0.1f, fallbackAimRadius);
        }

        private void AdvanceGrowthScaleTransition()
        {
            if (Mathf.Approximately(currentGrowthScale, targetGrowthScale)) return;
            growthScaleTransitionElapsed += Time.deltaTime;
            float duration = Mathf.Max(0.0001f, growthScaleTransitionSeconds);
            float progress = Mathf.Clamp01(growthScaleTransitionElapsed / duration);
            progress = progress * progress * (3f - 2f * progress);
            currentGrowthScale = Mathf.Lerp(growthScaleTransitionStart, targetGrowthScale, progress);
            if (progress >= 1f) currentGrowthScale = targetGrowthScale;
        }

        private void AdvanceStageColorTransition()
        {
            if (currentStageColor == targetStageColor) return;
            stageColorTransitionElapsed += Time.deltaTime;
            float duration = Mathf.Max(0.0001f, stageColorTransitionSeconds);
            float progress = Mathf.Clamp01(stageColorTransitionElapsed / duration);
            progress = progress * progress * (3f - 2f * progress);
            currentStageColor = Color.Lerp(stageColorTransitionStart, targetStageColor, progress);
            if (progress >= 1f) currentStageColor = targetStageColor;
        }

        private Color GetStageColor(int stage)
        {
            return stage >= 3 ? stageThreeColor : stage == 2 ? stageTwoColor : stageOneColor;
        }

        private float GetPulseScale()
        {
            bool enabled;
            float speed;
            float intensity;
            if (dangerStage >= 3)
            {
                enabled = stageThreePulseEnabled;
                speed = stageThreePulseSpeed;
                intensity = stageThreePulseIntensity;
            }
            else if (dangerStage == 2)
            {
                enabled = stageTwoPulseEnabled;
                speed = stageTwoPulseSpeed;
                intensity = stageTwoPulseIntensity;
            }
            else
            {
                enabled = stageOnePulseEnabled;
                speed = stageOnePulseSpeed;
                intensity = stageOnePulseIntensity;
            }

            if (!enabled || speed <= 0f || intensity <= 0f) return 1f;
            float wave = Mathf.Sin(Time.time * speed * Mathf.PI * 2f + pulsePhaseRadians);
            return 1f + wave * Mathf.Clamp(intensity, 0f, 0.2f);
        }

        private void CacheVfxRenderers(GameObject root, out Renderer[] renderers,
            out MaterialPropertyBlock[] blocks)
        {
            renderers = root.GetComponentsInChildren<Renderer>(true);
            blocks = new MaterialPropertyBlock[renderers.Length];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = new MaterialPropertyBlock();
        }

        private static void UpdateVfxTint(Renderer[] renderers, MaterialPropertyBlock[] blocks,
            ref bool hasLastTint, ref Color lastTint, Color color)
        {
            if (renderers == null || blocks == null || hasLastTint && lastTint == color) return;

            // The current AreaDamageShadow particle materials expose _Color and _TintColor.
            // Renderer property blocks keep each patch independent without cloning shared assets.
            Color tint = NormalizeTintHue(color);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                MaterialPropertyBlock block = blocks[i];
                renderer.GetPropertyBlock(block);
                block.SetColor(ParticleTintColor, tint);
                block.SetColor(BaseColor, tint);
                block.SetColor(LegacyColor, tint);
                renderer.SetPropertyBlock(block);
            }
            lastTint = color;
            hasLastTint = true;
        }

        private static Color NormalizeTintHue(Color color)
        {
            float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (max <= 0.0001f) return color;
            return new Color(color.r / max, color.g / max, color.b / max, color.a);
        }

        private static Vector3 ScaleFootprint(Vector3 original, float scale)
        {
            // This mesh is authored in the patch's local XZ floor plane.
            return new Vector3(original.x * scale, original.y, original.z * scale);
        }

        private Vector3 ScaleVfxFootprint(Vector3 originalScale, Transform vfxRoot, float scale)
        {
            // AreaDamageShadow is authored with a -90 degree X rotation. Its local XY plane is
            // the patch floor and local Z is height, so scaling XZ would stretch one floor axis
            // while changing height. Find the prefab-local axis aligned to the patch normal and
            // leave it unchanged; the two floor-aligned axes receive the same growth factor.
            Vector3 xAxis = transform.InverseTransformDirection(vfxRoot.right).normalized;
            Vector3 yAxis = transform.InverseTransformDirection(vfxRoot.up).normalized;
            Vector3 zAxis = transform.InverseTransformDirection(vfxRoot.forward).normalized;
            float alignX = Mathf.Abs(Vector3.Dot(xAxis, Vector3.up));
            float alignY = Mathf.Abs(Vector3.Dot(yAxis, Vector3.up));
            float alignZ = Mathf.Abs(Vector3.Dot(zAxis, Vector3.up));
            int verticalAxis = alignX >= alignY && alignX >= alignZ ? 0 : alignY >= alignZ ? 1 : 2;
            float verticalAlignment = Mathf.Max(alignX, Mathf.Max(alignY, alignZ));

            if (verticalAlignment < 0.85f)
                return originalScale * scale;

            Vector3 factors = Vector3.one * scale;
            if (verticalAxis == 0) factors.x = 1f;
            else if (verticalAxis == 1) factors.y = 1f;
            else factors.z = 1f;
            return Vector3.Scale(originalScale, factors);
        }
    }
}
