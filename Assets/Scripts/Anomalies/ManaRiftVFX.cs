using UnityEngine;

namespace WardShift
{
    /// <summary>선택형 균열 VFX 연결부. 봉인 판정은 ManaRift가 담당합니다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ManaRift))]
    public sealed class ManaRiftVFX : MonoBehaviour
    {
        [Header("균열 지속 효과")]
        [Tooltip("프로젝트 창의 프리팹 루트 GameObject를 지정합니다. ParticleSystem 컴포넌트가 아닌 .prefab 에셋을 넣으세요.")]
        [SerializeField] private GameObject persistentRiftPrefab;
        [Tooltip("균열 프리팹 내부의 생성 위치 Transform입니다. 비워 두면 균열 루트를 사용합니다.")]
        [SerializeField] private Transform persistentRiftAnchor;

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

        private ManaRift rift;
        private GameObject persistentInstance;

        private void Awake()
        {
            rift = GetComponent<ManaRift>();
        }

        private void OnEnable()
        {
            if (rift == null) rift = GetComponent<ManaRift>();
            if (rift != null)
            {
                rift.DangerStageChanged += OnDangerStageChanged;
                rift.SealChainFailed += OnSealChainFailed;
                rift.Ruptured += OnRuptured;
            }
            StartPersistentEffect();
        }

        private void OnDisable()
        {
            if (rift != null)
            {
                rift.DangerStageChanged -= OnDangerStageChanged;
                rift.SealChainFailed -= OnSealChainFailed;
                rift.Ruptured -= OnRuptured;
            }
            if (persistentInstance != null)
            {
                Destroy(persistentInstance);
                persistentInstance = null;
            }
        }

        private void StartPersistentEffect()
        {
            if (persistentInstance != null || persistentRiftPrefab == null) return;
            Transform anchor = persistentRiftAnchor != null ? persistentRiftAnchor : transform;
            persistentInstance = Instantiate(persistentRiftPrefab, anchor.position, anchor.rotation, anchor);
            PrepareEffect(persistentInstance);
        }

        private void OnDangerStageChanged(ManaRift source, int previousStage, int newStage)
        {
            PlayTransient(dangerStageChangePrefab, dangerStageChangeAnchor);
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
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
        }
    }
}
