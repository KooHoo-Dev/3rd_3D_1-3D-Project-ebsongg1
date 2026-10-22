using System;
using UnityEngine;
using UnityEngine.Events;

namespace WardShift
{
    /// <summary>두 봉인점이 모두 완료되면 효과와 완료 이벤트를 한 번 발생시키고 균열을 제거.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10003)]
    public sealed class ManaRift : MonoBehaviour
    {
        [Header("봉인점 (서로 다른 자식 MagicTarget)")]
        [SerializeField] private MagicTarget sealPoint01;
        [SerializeField] private MagicTarget sealPoint02;

        [Header("균열 전체 완료 효과")]
        [Tooltip("기존 완료 VFX 프리팹을 재사용. 비워 두어도 진압은 가능.")]
        [SerializeField] private GameObject completeVFXPrefab;
        [Tooltip("비워 두면 균열 루트 위치와 회전을 사용.")]
        [SerializeField] private Transform completeVFXAnchor;
        [SerializeField, Min(0.1f)] private float completeVFXLifetime = 1.5f;

        [Header("진압 완료")]
        [SerializeField] private UnityEvent onResolved = new UnityEvent();
        [SerializeField] private bool isResolved;
        private bool completionPending;

        [Header("DAY 4 성장 (생성 후 누적 초)")]
        [SerializeField, Min(0.1f)] private float stageTwoAt = 10f;
        [SerializeField, Min(0.2f)] private float stageThreeAt = 20f;
        [SerializeField, Min(0.3f)] private float overloadAt = 30f;
        [SerializeField, Min(0f)] private float overloadDamage = 20f;
        [Header("주변 오염")]
        [SerializeField, Min(0f)] private float dangerRadius = 3f;
        [SerializeField, Min(0f)] private float stageTwoContamination = 3f;
        [SerializeField, Min(0f)] private float stageThreeContamination = 6f;
        [Header("위험 상태 (실행 중 확인용)")]
        [SerializeField] private float age;
        [SerializeField] private int dangerStage = 1;
        [SerializeField] private bool hasOverloaded;

        public int DangerStage => dangerStage;
        public bool HasOverloaded => hasOverloaded;
        public bool IsHazardActive => isActiveAndEnabled && !isResolved && CompletedSealCount < 2;

        // WardStatus가 시전/봉인 처리 이후 호출. 피해를 반환하므로 폭주당 한 번만 적용됨.
        public float AdvanceHazard(float deltaTime)
        {
            if (!IsHazardActive || deltaTime <= 0f) return 0f;
            age += deltaTime;
            float second = Mathf.Max(0.1f, stageTwoAt);
            float third = Mathf.Max(second + 0.1f, stageThreeAt);
            float overload = Mathf.Max(third + 0.1f, overloadAt);
            dangerStage = age >= third ? 3 : age >= second ? 2 : 1;
            if (hasOverloaded || age < overload) return 0f;
            hasOverloaded = true;
            Debug.Log("마력 균열 폭주: 시설 안정도 피해", this);
            return Mathf.Max(0f, overloadDamage);
        }

        public float ContaminationRateAt(Vector3 position)
        {
            if (!IsHazardActive || (position - transform.position).sqrMagnitude > dangerRadius * dangerRadius)
                return 0f;
            return dangerStage == 3 ? Mathf.Max(0f, stageThreeContamination)
                : dangerStage == 2 ? Mathf.Max(0f, stageTwoContamination) : 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = dangerStage == 3 ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, dangerRadius));
        }

        public bool IsResolved => isResolved;
        public int CompletedSealCount => (sealPoint01 != null && sealPoint01.IsCompleted ? 1 : 0)
            + (sealPoint02 != null && sealPoint02.IsCompleted ? 1 : 0);

        /// <summary>균열 전체 진압 시 한 번 발생. 이후 보상/시설 시스템에서 구독 가능.</summary>
        public event Action<ManaRift> Resolved;

        private void OnEnable()
        {
            if (!ValidateSealPoints())
            {
                Debug.LogError("ManaRift: 서로 다른 자식 봉인점 두 개를 연결하세요. 각 대상은 Seal 및 Use Timing Cast 설정이 필요합니다.", this);
                enabled = false;
                return;
            }
            sealPoint01.Completed += OnSealCompleted;
            sealPoint02.Completed += OnSealCompleted;
            // 비활성화 중 완료된 경우에도 재활성화 시 실제 상태를 확인.
            completionPending = !isResolved && CompletedSealCount == 2;
        }

        private bool ValidateSealPoints()
        {
            return sealPoint01 != null && sealPoint02 != null && sealPoint01 != sealPoint02
                && sealPoint01.transform != transform && sealPoint02.transform != transform
                && sealPoint01.transform.IsChildOf(transform) && sealPoint02.transform.IsChildOf(transform)
                && !sealPoint01.transform.IsChildOf(sealPoint02.transform)
                && !sealPoint02.transform.IsChildOf(sealPoint01.transform)
                && sealPoint01.RequiredMagic == MagicType.Seal && sealPoint02.RequiredMagic == MagicType.Seal
                && sealPoint01.UseTimingCast && sealPoint02.UseTimingCast;
        }

        private void OnSealCompleted(MagicTarget target)
        {
            if (!isResolved && CompletedSealCount == 2) completionPending = true;
        }

        private void LateUpdate()
        {
            if (!completionPending || isResolved) return;
            completionPending = false;
            if (CompletedSealCount != 2) return;
            // 봉인점 Completed 콜백 내부에서 부모를 끄지 않음.
            // 마지막 봉인점의 VFX와 다른 완료 구독자가 모두 실행된 뒤 균열을 처리.
            isResolved = true;
            PlayCompleteEffect();
            Debug.Log("마력 균열 진압 완료 (2/2)", this);
            try
            {
                Resolved?.Invoke(this);
                onResolved.Invoke();
            }
            finally
            {
                if (this != null) Destroy(gameObject);
            }
        }

        private void PlayCompleteEffect()
        {
            if (completeVFXPrefab == null) return;
            Transform anchor = completeVFXAnchor != null ? completeVFXAnchor : transform;
            // 루트 제거 후에도 효과가 끝까지 재생되도록 부모 없이 생성.
            GameObject effect = Instantiate(completeVFXPrefab, anchor.position, anchor.rotation);
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
            Destroy(effect, Mathf.Max(0.1f, completeVFXLifetime));
        }

        private void OnDisable()
        {
            if (sealPoint01 != null) sealPoint01.Completed -= OnSealCompleted;
            if (sealPoint02 != null) sealPoint02.Completed -= OnSealCompleted;
            completionPending = false;
        }
    }
}
