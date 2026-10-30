using System;
using UnityEngine;

namespace WardShift
{
    /// <summary>균열 지속 VFX의 성장 단계별 표현만 관리합니다. 봉인 판정과 충돌체는 건드리지 않습니다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ManaRift))]
    public sealed class ManaRiftVFX : MonoBehaviour
    {
        [Header("균열 지속 효과")]
        [Tooltip("프로젝트 창의 GameObject 프리팹. 단계별 프리팹이 비어 있으면 이 에셋을 사용합니다.")]
        [SerializeField] private GameObject persistentRiftPrefab;
        [Tooltip("균열 프리팹 내부의 생성 위치 Transform입니다. 비워 두면 균열 루트를 사용합니다.")]
        [SerializeField] private Transform persistentRiftAnchor;
        [Tooltip("ManaRift 루트 아래의 충돌체 없는 중심 균열 Mesh Transform입니다. 크기·색만 적용합니다.")]
        [SerializeField] private Transform coreRiftVisual;
        [Tooltip("중심 구체(RiftVisual과 지속 효과 루트 렌더러)의 원래 색과 밝기를 유지합니다. 단계별 색상·발광은 바깥 효과에 적용됩니다.")]
        [SerializeField] private bool preserveCoreOrbBrightness = true;

        [Header("1단계 균열 표현")]
        [Tooltip("지정하면 기본 지속 효과 대신 1단계에서 사용할 균열 효과 프리팹입니다.")]
        [SerializeField] private GameObject stageOneRiftPrefab;
        [SerializeField, Min(0.1f)] private float stageOneSizeMultiplier = 1f;
        [SerializeField] private Color stageOneTint = Color.white;
        [Tooltip("지원되는 Light와 셰이더 발광에 적용합니다. 파티클 생성 수는 바꾸지 않습니다.")]
        [SerializeField, Min(0f)] private float stageOneEmissionIntensity = 1f;
        [Tooltip("지정하면 중심 메쉬와 지속 효과의 MeshRenderer에 이 Material을 인스턴스별로 적용합니다. 파티클 재질은 유지됩니다.")]
        [SerializeField] private Material stageOneMaterialOverride;

        [Header("2단계 균열 표현")]
        [Tooltip("지정하면 기본 지속 효과 대신 2단계에서 사용할 균열 효과 프리팹입니다.")]
        [SerializeField] private GameObject stageTwoRiftPrefab;
        [SerializeField, Min(0.1f)] private float stageTwoSizeMultiplier = 1.2f;
        [SerializeField] private Color stageTwoTint = new Color(0.8f, 0.55f, 1f, 1f);
        [Tooltip("지원되는 Light와 셰이더 발광에 적용합니다. 파티클 생성 수는 바꾸지 않습니다.")]
        [SerializeField, Min(0f)] private float stageTwoEmissionIntensity = 1.25f;
        [SerializeField] private Material stageTwoMaterialOverride;

        [Header("3단계 균열 표현")]
        [Tooltip("지정하면 기본 지속 효과 대신 3단계에서 사용할 균열 효과 프리팹입니다.")]
        [SerializeField] private GameObject stageThreeRiftPrefab;
        [SerializeField, Min(0.1f)] private float stageThreeSizeMultiplier = 1.45f;
        [SerializeField] private Color stageThreeTint = new Color(1f, 0.28f, 0.22f, 1f);
        [Tooltip("지원되는 Light와 셰이더 발광에 적용합니다. 파티클 생성 수는 바꾸지 않습니다.")]
        [SerializeField, Min(0f)] private float stageThreeEmissionIntensity = 1.6f;
        [SerializeField] private Material stageThreeMaterialOverride;

        [Header("위험 단계 변경 효과")]
        [Tooltip("프로젝트 창의 GameObject .prefab을 지정합니다.")]
        [SerializeField] private GameObject dangerStageChangePrefab;
        [Tooltip("균열 프리팹 내부의 생성 위치 Transform입니다. 비워 두면 균열 루트를 사용합니다.")]
        [SerializeField] private Transform dangerStageChangeAnchor;

        [Header("연쇄 실패 효과")]
        [Tooltip("프로젝트 창의 GameObject .prefab을 지정합니다.")]
        [SerializeField] private GameObject sealChainFailurePrefab;
        [Tooltip("균열 프리팹 내부의 생성 위치 Transform입니다. 비워 두면 균열 루트를 사용합니다.")]
        [SerializeField] private Transform sealChainFailureAnchor;

        [Header("파열 효과")]
        [Tooltip("프로젝트 창의 GameObject .prefab을 지정합니다.")]
        [SerializeField] private GameObject rupturePrefab;
        [Tooltip("균열 프리팹 내부의 생성 위치 Transform입니다. 비워 두면 균열 루트를 사용합니다.")]
        [SerializeField] private Transform ruptureAnchor;

        [SerializeField, Min(0.1f)] private float transientLifetime = 2.5f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColor = Shader.PropertyToID("_Color");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private ManaRift rift;
        private GameObject persistentInstance;
        private GameObject activePersistentPrefab;
        private Vector3 persistentBaseScale = Vector3.one;
        private RendererVisualState[] rendererStates = Array.Empty<RendererVisualState>();
        private RendererVisualState[] coreRendererStates = Array.Empty<RendererVisualState>();
        private Vector3 coreBaseScale = Vector3.one;
        private Light[] lights = Array.Empty<Light>();
        private float[] lightBaseIntensities = Array.Empty<float>();

        private sealed class RendererVisualState
        {
            public Renderer renderer;
            public Material[] materials;
            public MaterialPropertyBlock[] propertyBlocks;
        }

        private void Awake()
        {
            rift = GetComponent<ManaRift>();
            if (coreRiftVisual == null)
            {
                Transform candidate = transform.Find("RiftVisual");
                if (candidate != null) coreRiftVisual = candidate;
            }
            if (coreRiftVisual != null)
            {
                coreBaseScale = coreRiftVisual.localScale;
                coreRendererStates = CaptureRendererStates(
                    coreRiftVisual.GetComponentsInChildren<Renderer>(true));
            }
        }

        private void OnEnable()
        {
            if (rift == null) rift = GetComponent<ManaRift>();
            if (rift != null)
            {
                rift.DangerStageChanged += OnDangerStageChanged;
                rift.SealChainFailed += OnSealChainFailed;
                rift.Ruptured += OnRuptured;
                ApplyDangerStage(rift.DangerStage, false);
            }
        }

        private void OnDisable()
        {
            if (rift != null)
            {
                rift.DangerStageChanged -= OnDangerStageChanged;
                rift.SealChainFailed -= OnSealChainFailed;
                rift.Ruptured -= OnRuptured;
            }
            if (coreRiftVisual != null) coreRiftVisual.localScale = coreBaseScale;
            ResetRendererStates(coreRendererStates);
            DestroyPersistentEffect();
        }

        private void ApplyDangerStage(int stage, bool playTransitionEffect)
        {
            GameObject prefab = GetStagePrefab(stage);
            if (persistentInstance == null || activePersistentPrefab != prefab)
            {
                DestroyPersistentEffect();
                if (prefab != null) StartPersistentEffect(prefab);
            }
            ApplyStageMaterial(stage);
            ApplyStageAppearance(stage);
            if (playTransitionEffect)
                PlayTransient(dangerStageChangePrefab, dangerStageChangeAnchor);
        }

        private GameObject GetStagePrefab(int stage)
        {
            GameObject stagePrefab = stage >= 3 ? stageThreeRiftPrefab
                : stage == 2 ? stageTwoRiftPrefab : stageOneRiftPrefab;
            return stagePrefab != null ? stagePrefab : persistentRiftPrefab;
        }

        private void StartPersistentEffect(GameObject prefab)
        {
            Transform anchor = persistentRiftAnchor != null ? persistentRiftAnchor : transform;
            persistentInstance = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
            persistentInstance.transform.localPosition = Vector3.zero;
            persistentInstance.transform.localRotation = Quaternion.identity;
            persistentBaseScale = persistentInstance.transform.localScale;
            activePersistentPrefab = prefab;
            PrepareEffect(persistentInstance);
            CachePersistentVisuals();
        }

        private void DestroyPersistentEffect()
        {
            if (persistentInstance != null)
            {
                persistentInstance.SetActive(false);
                Destroy(persistentInstance);
            }
            persistentInstance = null;
            activePersistentPrefab = null;
            rendererStates = Array.Empty<RendererVisualState>();
            lights = Array.Empty<Light>();
            lightBaseIntensities = Array.Empty<float>();
        }

        private void CachePersistentVisuals()
        {
            rendererStates = CaptureRendererStates(persistentInstance.GetComponentsInChildren<Renderer>(true));

            lights = persistentInstance.GetComponentsInChildren<Light>(true);
            lightBaseIntensities = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) lightBaseIntensities[i] = lights[i].intensity;
        }

        private static RendererVisualState[] CaptureRendererStates(Renderer[] renderers)
        {
            var states = new RendererVisualState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] materials = renderer.sharedMaterials;
                MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[materials.Length];
                for (int materialIndex = 0; materialIndex < blocks.Length; materialIndex++)
                {
                    blocks[materialIndex] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(blocks[materialIndex], materialIndex);
                }
                states[i] = new RendererVisualState
                {
                    renderer = renderer,
                    materials = materials,
                    propertyBlocks = blocks
                };
            }
            return states;
        }

        private void ApplyStageAppearance(int stage)
        {
            float scale = stage >= 3 ? stageThreeSizeMultiplier
                : stage == 2 ? stageTwoSizeMultiplier : stageOneSizeMultiplier;
            Color tint = stage >= 3 ? stageThreeTint : stage == 2 ? stageTwoTint : stageOneTint;
            float intensity = stage >= 3 ? stageThreeEmissionIntensity
                : stage == 2 ? stageTwoEmissionIntensity : stageOneEmissionIntensity;
            if (persistentInstance != null)
            {
                persistentInstance.transform.localScale = persistentBaseScale * Mathf.Max(0.1f, scale);
                Renderer preservedCoreRenderer = preserveCoreOrbBrightness
                    ? persistentInstance.GetComponent<Renderer>() : null;
                ApplyStageTint(rendererStates, tint, intensity, preservedCoreRenderer);
            }
            if (coreRiftVisual != null)
                coreRiftVisual.localScale = coreBaseScale * Mathf.Max(0.1f, scale);

            if (!preserveCoreOrbBrightness)
                ApplyStageTint(coreRendererStates, tint, intensity);

            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = lightBaseIntensities[i] * intensity;
        }

        private static void ApplyStageTint(RendererVisualState[] states, Color tint, float intensity,
            Renderer excludedRenderer = null)
        {
            for (int i = 0; i < states.Length; i++)
            {
                RendererVisualState state = states[i];
                if (state.renderer == null || state.renderer == excludedRenderer) continue;
                Material[] activeMaterials = state.renderer.sharedMaterials;
                int materialCount = Mathf.Min(activeMaterials.Length, state.propertyBlocks.Length);
                for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
                {
                    Material material = activeMaterials[materialIndex];
                    if (material == null) continue;
                    state.renderer.SetPropertyBlock(state.propertyBlocks[materialIndex], materialIndex);
                    var block = new MaterialPropertyBlock();
                    state.renderer.GetPropertyBlock(block, materialIndex);
                    bool hasTint = false;
                    if (material.HasProperty(BaseColor))
                    {
                        block.SetColor(BaseColor, material.GetColor(BaseColor) * tint);
                        hasTint = true;
                    }
                    if (material.HasProperty(LegacyColor))
                    {
                        block.SetColor(LegacyColor, material.GetColor(LegacyColor) * tint);
                        hasTint = true;
                    }
                    if (material.HasProperty(EmissionColor))
                        block.SetColor(EmissionColor, material.GetColor(EmissionColor) * intensity);
                    // Materials without color properties keep their authored appearance; stage prefabs/materials are the fallback.
                    if (hasTint || material.HasProperty(EmissionColor))
                        state.renderer.SetPropertyBlock(block, materialIndex);
                }
            }
        }

        private void ApplyStageMaterial(int stage)
        {
            Material overrideMaterial = stage >= 3 ? stageThreeMaterialOverride
                : stage == 2 ? stageTwoMaterialOverride : stageOneMaterialOverride;
            ApplyMaterialOverride(rendererStates, overrideMaterial);
            if (preserveCoreOrbBrightness)
                ResetRendererStates(coreRendererStates);
            else
                ApplyMaterialOverride(coreRendererStates, overrideMaterial);
        }

        private static void ApplyMaterialOverride(RendererVisualState[] states, Material overrideMaterial)
        {
            foreach (RendererVisualState state in states)
            {
                // Preserve particle materials and replace only regular mesh materials on this effect instance.
                if (!(state.renderer is MeshRenderer) || state.renderer == null) continue;
                if (overrideMaterial == null)
                {
                    state.renderer.sharedMaterials = state.materials;
                    continue;
                }
                var replacements = new Material[state.materials.Length];
                for (int i = 0; i < replacements.Length; i++) replacements[i] = overrideMaterial;
                state.renderer.sharedMaterials = replacements;
            }
        }

        private static void ResetRendererStates(RendererVisualState[] states)
        {
            foreach (RendererVisualState state in states)
            {
                if (state.renderer == null) continue;
                if (state.renderer is MeshRenderer) state.renderer.sharedMaterials = state.materials;
                for (int i = 0; i < state.propertyBlocks.Length; i++)
                    state.renderer.SetPropertyBlock(state.propertyBlocks[i], i);
            }
        }

        private void OnDangerStageChanged(ManaRift source, int previousStage, int newStage)
        {
            ApplyDangerStage(newStage, true);
        }

        private void OnSealChainFailed(ManaRift source)
        {
            PlayTransient(sealChainFailurePrefab, sealChainFailureAnchor);
        }

        private void OnRuptured(ManaRift source)
        {
            PlayTransient(rupturePrefab, ruptureAnchor);
        }

        private void PlayTransient(GameObject prefab, Transform configuredAnchor)
        {
            if (prefab == null) return;
            Transform anchor = configuredAnchor != null ? configuredAnchor : transform;
            GameObject effect = Instantiate(prefab, anchor.position, anchor.rotation);
            PrepareEffect(effect);
            Destroy(effect, Mathf.Max(0.1f, transientLifetime));
        }

        private static void PrepareEffect(GameObject effect)
        {
            foreach (Collider effectCollider in effect.GetComponentsInChildren<Collider>(true))
                effectCollider.enabled = false;
            foreach (Collider2D effectCollider in effect.GetComponentsInChildren<Collider2D>(true))
                effectCollider.enabled = false;
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
        }
    }
}
