using UnityEngine;

namespace WardShift
{
    /// <summary>씬에 남는 마력 장치 외형과 폭주 상태를 표시합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class ManaDeviceVisuals : MonoBehaviour
    {
        [Header("교체 가능한 외형")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private MagicTarget stabilizationTarget;
        [SerializeField] private Renderer[] stateRenderers = new Renderer[0];
        [SerializeField] private Renderer warningBeaconRenderer;
        [SerializeField] private Light crystalLight;
        [SerializeField] private Light warningLight;
        [SerializeField] private LineRenderer warningRing;

        [Header("선택 VFX 프리팹")]
        [SerializeField] private GameObject overloadLoopVFX;
        [SerializeField] private GameObject stabilizingVFX;
        [SerializeField] private GameObject warningVFX;
        [SerializeField] private GameObject burstVFX;
        [SerializeField] private GameObject recoveryVFX;
        [SerializeField] private GameObject resolvedVFX;
        [SerializeField, Min(0.1f)] private float burstVFXLifetime = 1.5f;
        [SerializeField, Min(0.1f)] private float resolvedVFXLifetime = 1.5f;

        [Header("안정화와 표시 색상")]
        [SerializeField, Min(0.1f)] private float stabilizeRange = 2.75f;
        [SerializeField] private Color normalColor = new Color(0.15f, 0.78f, 1f, 1f);
        [SerializeField] private Color overloadColor = new Color(1f, 0.2f, 0.5f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.08f, 0.025f, 1f);
        [SerializeField] private Color recoveryColor = new Color(0.16f, 1f, 0.65f, 1f);
        [SerializeField] private Color diagnosisColor = new Color(0.15f, 0.85f, 1f, 0.7f);
        [SerializeField, Min(0f)] private float normalEmission = 1.7f;
        [SerializeField, Min(0f)] private float overloadEmission = 3.3f;
        [SerializeField, Min(0f)] private float warningEmission = 5f;
        [SerializeField, Min(0f)] private float warningPulseSpeed = 14f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private bool overloaded;
        private bool ruptured;
        private bool warningActive;
        private bool diagnosisActive;
        private bool recoveryActive;
        private bool impactWasActive;
        private float warningFraction;
        private float impactUntil;
        private float warningRadius;
        private float builtRingRadius = -1f;
        private MaterialPropertyBlock propertyBlock;
        private LineRenderer diagnosisRing;
        private Material diagnosisMaterial;
        private GameObject overloadInstance;
        private GameObject stabilizingInstance;
        private GameObject warningInstance;
        private GameObject recoveryInstance;
        private GameObject burstInstance;
        private GameObject resolvedInstance;
        public ManaDeviceAnomaly ActiveAnomaly { get; private set; }

        public Transform VisualRoot => visualRoot;
        public MagicTarget StabilizationTarget => stabilizationTarget;
        public float StabilizeRange => Mathf.Max(0.1f, stabilizeRange);
        public bool IsOverloaded => overloaded;
        public bool IsRuptured => ruptured;
        public bool CanBeginOverload => isActiveAndEnabled && !overloaded && !ruptured
            && stabilizationTarget != null
            && stabilizationTarget.RequiredMagic == MagicType.Stabilize;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (visualRoot == null) visualRoot = transform;
            if (stabilizationTarget == null) stabilizationTarget = GetComponentInChildren<MagicTarget>(true);
            if (stabilizationTarget != null) stabilizationTarget.SetRuntimeAvailable(false);
            if (stabilizationTarget != null)
            {
                stabilizationTarget.CastStarted += RefreshVFX;
                stabilizationTarget.CastStopped += RefreshVFX;
            }
            ApplyAppearance();
        }

        private void Update()
        {
            bool impact = Time.time < impactUntil;
            if (!warningActive && !impact && !impactWasActive) return;
            impactWasActive = impact;
            ApplyAppearance();
        }

        /// <summary>공통 스포너가 폭주 로직을 연결할 때 마법 대상을 열고 경고 원 크기를 적용합니다.</summary>
        public bool BeginOverload(float radius)
        {
            if (!CanBeginOverload) return false;
            warningRadius = Mathf.Max(0.1f, radius);
            warningActive = false;
            warningFraction = 0f;
            diagnosisActive = false;
            recoveryActive = false;
            impactUntil = 0f;
            impactWasActive = false;
            overloaded = true;
            stabilizationTarget.ResetForNewUse();
            SetWarningRing(false);
            SetDiagnosisRing(false);
            ApplyAppearance();
            RefreshVFX();
            return true;
        }

        public void SetActiveAnomaly(ManaDeviceAnomaly anomaly) => ActiveAnomaly = anomaly;

        public void SetDiagnosis(bool active, float radius)
        {
            diagnosisActive = overloaded && active;
            if (diagnosisActive) EnsureDiagnosisRing(radius);
            SetDiagnosisRing(diagnosisActive && !warningActive);
            if (!diagnosisActive) ApplyAppearance();
        }

        public void SetRecovery(bool active)
        {
            recoveryActive = overloaded && active;
            ApplyAppearance();
            RefreshVFX();
        }

        public void SetBurstWarning(bool active, float radius, float remainingFraction)
        {
            warningActive = overloaded && active;
            warningRadius = Mathf.Max(0.1f, radius);
            warningFraction = Mathf.Clamp01(remainingFraction);
            SetWarningRing(warningActive);
            SetDiagnosisRing(diagnosisActive && !warningActive);
            ApplyAppearance();
            RefreshVFX();
        }

        public void ShowBurstImpact()
        {
            impactUntil = Time.time + 0.3f;
            impactWasActive = true;
            warningActive = false;
            SetWarningRing(false);
            ApplyAppearance();
            StopInstance(ref burstInstance);
            burstInstance = MagicVfxUtility.SpawnDetached(burstVFX, transform.position,
                transform.rotation, burstVFXLifetime);
            RefreshVFX();
        }

        public void MarkResolved()
        {
            ReturnToNormal();
            resolvedInstance = MagicVfxUtility.SpawnDetached(resolvedVFX, transform.position,
                transform.rotation, resolvedVFXLifetime);
        }

        /// <summary>진압 후 고정 장치는 남고 정상 외형으로 복귀합니다.</summary>
        public void ReturnToNormal()
        {
            overloaded = false;
            ActiveAnomaly = null;
            ruptured = false;
            warningActive = false;
            diagnosisActive = false;
            recoveryActive = false;
            warningFraction = 0f;
            impactUntil = 0f;
            impactWasActive = false;
            if (stabilizationTarget != null) stabilizationTarget.SetRuntimeAvailable(false);
            SetWarningRing(false);
            SetDiagnosisRing(false);
            ApplyAppearance();
            StopAllVFX();
        }

        /// <summary>파열 상태는 다음 DAY가 시작될 때까지 장치에 남습니다.</summary>
        public void MarkRuptured()
        {
            overloaded = false;
            ActiveAnomaly = null;
            ruptured = true;
            warningActive = false;
            diagnosisActive = false;
            recoveryActive = false;
            impactUntil = 0f;
            impactWasActive = false;
            if (stabilizationTarget != null) stabilizationTarget.SetRuntimeAvailable(false);
            SetWarningRing(false);
            SetDiagnosisRing(false);
            ApplyAppearance();
            StopAllVFX();
        }

        /// <summary>새 DAY 시작 시 테스트 장치와 안정화 대상을 초기화합니다.</summary>
        public void ResetForNewDay()
        {
            overloaded = false;
            ActiveAnomaly = null;
            ruptured = false;
            warningActive = false;
            diagnosisActive = false;
            recoveryActive = false;
            warningFraction = 0f;
            impactUntil = 0f;
            impactWasActive = false;
            if (stabilizationTarget != null) stabilizationTarget.ResetForNewUse(false);
            SetWarningRing(false);
            SetDiagnosisRing(false);
            ApplyAppearance();
            StopAllVFX();
        }

        private void EnsureDiagnosisRing(float radius)
        {
            if (warningRing == null) return;
            if (diagnosisRing == null)
            {
                GameObject ring = new GameObject("Mana_Diagnosis_Ring");
                ring.transform.SetParent(warningRing.transform.parent, false);
                ring.transform.localPosition = warningRing.transform.localPosition;
                ring.transform.localRotation = warningRing.transform.localRotation;
                ring.transform.localScale = warningRing.transform.localScale;
                diagnosisRing = ring.AddComponent<LineRenderer>();
                diagnosisRing.startWidth = warningRing.startWidth * 0.7f;
                diagnosisRing.endWidth = warningRing.endWidth * 0.7f;
                diagnosisRing.loop = true;
                diagnosisRing.useWorldSpace = false;
                diagnosisRing.alignment = warningRing.alignment;
                if (warningRing.sharedMaterial != null)
                {
                    diagnosisMaterial = new Material(warningRing.sharedMaterial);
                    if (diagnosisMaterial.HasProperty(BaseColorId))
                        diagnosisMaterial.SetColor(BaseColorId, diagnosisColor);
                    if (diagnosisMaterial.HasProperty(LegacyColorId))
                        diagnosisMaterial.SetColor(LegacyColorId, diagnosisColor);
                    diagnosisRing.sharedMaterial = diagnosisMaterial;
                }
            }
            const int segments = 96;
            diagnosisRing.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                diagnosisRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius,
                    0.065f, Mathf.Sin(angle) * radius));
            }
            diagnosisRing.startColor = diagnosisColor;
            diagnosisRing.endColor = diagnosisColor;
        }

        private void SetDiagnosisRing(bool active)
        {
            if (diagnosisRing != null) diagnosisRing.enabled = active;
        }

        private void RefreshVFX()
        {
            if (!isActiveAndEnabled) { StopAllVFX(); return; }
            SyncLoop(ref overloadInstance, overloadLoopVFX, overloaded);
            SyncLoop(ref stabilizingInstance, stabilizingVFX, overloaded
                && stabilizationTarget != null && stabilizationTarget.IsCasting);
            SyncLoop(ref warningInstance, warningVFX, overloaded && warningActive);
            SyncLoop(ref recoveryInstance, recoveryVFX, overloaded && recoveryActive);
        }

        private void SyncLoop(ref GameObject instance, GameObject prefab, bool active)
        {
            if (!active || prefab == null) { StopInstance(ref instance); return; }
            if (instance == null)
                instance = MagicVfxUtility.SpawnAttached(prefab,
                    visualRoot != null ? visualRoot : transform);
        }

        private static void StopInstance(ref GameObject instance)
        {
            MagicVfxUtility.StopAndDestroy(instance);
            instance = null;
        }

        private void StopAllVFX()
        {
            StopInstance(ref overloadInstance);
            StopInstance(ref stabilizingInstance);
            StopInstance(ref warningInstance);
            StopInstance(ref recoveryInstance);
            StopInstance(ref burstInstance);
            StopInstance(ref resolvedInstance);
        }

        private void SetWarningRing(bool enabled)
        {
            if (warningRing == null) return;
            bool rebuildPositions = enabled && (!warningRing.enabled
                || !Mathf.Approximately(builtRingRadius, warningRadius));
            warningRing.enabled = enabled;
            if (!rebuildPositions) return;

            const int segments = 96;
            warningRing.useWorldSpace = false;
            warningRing.loop = true;
            warningRing.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                warningRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * warningRadius,
                    0.045f, Mathf.Sin(angle) * warningRadius));
            }
            builtRingRadius = warningRadius;
        }

        private void ApplyAppearance()
        {
            bool warning = warningActive;
            bool impact = Time.time < impactUntil;
            Color color = ruptured ? warningColor * 0.65f
                : warning || impact ? warningColor
                : recoveryActive ? recoveryColor
                : overloaded ? overloadColor : normalColor;
            float emission = ruptured ? warningEmission * 0.55f
                : warning || impact ? warningEmission
                : recoveryActive ? overloadEmission * 1.25f
                : overloaded ? overloadEmission : normalEmission;
            if (warning)
            {
                float pulse = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * warningPulseSpeed));
                emission *= pulse;
            }

            if (stateRenderers != null)
                foreach (Renderer renderer in stateRenderers)
                    ApplyRendererColor(renderer, color, emission);
            ApplyRendererColor(warningBeaconRenderer, warning ? warningColor : overloadColor,
                warning ? warningEmission : overloadEmission);
            if (warningBeaconRenderer != null)
                warningBeaconRenderer.enabled = warning || impact || ruptured;

            if (crystalLight != null)
            {
                crystalLight.color = color;
                crystalLight.intensity = warning || impact ? 1.5f + emission * 0.3f
                    : ruptured ? 0.5f : overloaded ? 1.1f : 0.65f;
                crystalLight.enabled = true;
            }
            if (warningLight != null)
            {
                warningLight.color = warningColor;
                warningLight.enabled = warning || impact || ruptured;
                warningLight.intensity = warning
                    ? 1f + (1f - warningFraction) * 2.5f : impact ? 3f : ruptured ? 0.8f : 0f;
            }
            if (warningRing != null && warning)
            {
                float pulse = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * warningPulseSpeed));
                Color ringColor = warningColor * pulse;
                warningRing.startColor = ringColor;
                warningRing.endColor = ringColor;
            }
        }

        private void ApplyRendererColor(Renderer renderer, Color color, float emission)
        {
            if (renderer == null || propertyBlock == null) return;
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(LegacyColorId, color);
            propertyBlock.SetColor(EmissionColorId, color * emission);
            renderer.SetPropertyBlock(propertyBlock);
        }

        private void OnDisable()
        {
            SetDiagnosisRing(false);
            StopAllVFX();
        }

        private void OnEnable()
        {
            if (propertyBlock != null) RefreshVFX();
        }

        private void OnDestroy()
        {
            if (stabilizationTarget != null)
            {
                stabilizationTarget.CastStarted -= RefreshVFX;
                stabilizationTarget.CastStopped -= RefreshVFX;
            }
            StopAllVFX();
            if (diagnosisMaterial != null) Destroy(diagnosisMaterial);
        }
    }
}
