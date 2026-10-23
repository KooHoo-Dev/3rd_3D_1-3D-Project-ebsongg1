using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace WardShift
{
    /// <summary>두 봉인점이 모두 완료되면 효과와 완료 이벤트를 한 번 발생시키고 균열을 제거.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10003)]
    public sealed class ManaRift : AnomalyBehaviour
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

        [Header("성장·파열 (생성 후 누적 초)")]
        [SerializeField, Min(0.1f)] private float stageTwoAt = 20f;
        [SerializeField, Min(0.2f)] private float stageThreeAt = 40f;
        [FormerlySerializedAs("overloadAt"), SerializeField, Min(0.3f)] private float ruptureAt = 60f;
        [Header("시설 안정도 손실")]
        [SerializeField, Min(0f)] private float stageTwoStabilityLossPerSecond = 0.5f;
        [SerializeField, Min(0f)] private float stageThreeStabilityLossPerSecond = 1f;
        [FormerlySerializedAs("overloadDamage"), SerializeField, Min(0f)] private float ruptureStabilityDamage = 20f;
        [Header("주변 오염")]
        [SerializeField, Min(0f)] private float dangerRadius = 3f;
        [SerializeField, Min(0f)] private float stageTwoContamination = 3f;
        [SerializeField, Min(0f)] private float stageThreeContamination = 6f;
        [Header("위험 상태 (실행 중 확인용)")]
        [SerializeField] private float age;
        [SerializeField] private int dangerStage = 1;
        [FormerlySerializedAs("hasOverloaded"), SerializeField] private bool hasRuptured;

        public float Age => age;
        public override string DisplayName => "마력 균열";
        public override int DangerStage => dangerStage;
        public override bool HasOverloaded => hasRuptured;
        public override int ResolutionReward => dangerStage;
        public override AnomalySpawnPoint.SurfaceType SpawnSurface => AnomalySpawnPoint.SurfaceType.Wall;
        public override bool IsHazardActive => isActiveAndEnabled && !isResolved && !hasRuptured && CompletedSealCount < 2;
        public float SecondsUntilNextStage
        {
            get
            {
                float second = Mathf.Max(0.1f, stageTwoAt);
                float third = Mathf.Max(second + 0.1f, stageThreeAt);
                float rupture = Mathf.Max(third + 0.1f, ruptureAt);
                float target = dangerStage >= 3 ? rupture : dangerStage == 2 ? third : second;
                return Mathf.Max(0f, target - age);
            }
        }
        public string NextStageLabel => dangerStage >= 3 ? "파열까지" : $"{dangerStage + 1}단계까지";

        // WardStatus가 프레임별 손실을 합산해 적용. 단계 경계와 프레임이 겹쳐도 초당 손실을 정확히 적분한다.
        public override float AdvanceHazard(float deltaTime)
        {
            if (!IsHazardActive || deltaTime <= 0f) return 0f;
            float previousAge = Mathf.Max(0f, age);
            float second = Mathf.Max(0.1f, stageTwoAt);
            float third = Mathf.Max(second + 0.1f, stageThreeAt);
            float rupture = Mathf.Max(third + 0.1f, ruptureAt);
            float nextAge = previousAge + deltaTime;
            bool ruptureReached = nextAge >= rupture;
            float currentAge = Mathf.Min(nextAge, rupture);
            age = currentAge;
            dangerStage = currentAge >= third ? 3 : currentAge >= second ? 2 : 1;

            float stageTwoSeconds = Mathf.Max(0f, Mathf.Min(currentAge, third) - Mathf.Max(previousAge, second));
            float stageThreeSeconds = Mathf.Max(0f, Mathf.Min(currentAge, rupture) - Mathf.Max(previousAge, third));
            float stabilityLoss = stageTwoSeconds * Mathf.Max(0f, stageTwoStabilityLossPerSecond)
                + stageThreeSeconds * Mathf.Max(0f, stageThreeStabilityLossPerSecond);

            if (!ruptureReached) return stabilityLoss;
            hasRuptured = true;
            stabilityLoss += Mathf.Max(0f, ruptureStabilityDamage);
            Debug.Log("마력 균열 파열: 시설 안정도 피해 후 제거", this);
            NotifyRuptured();
            Destroy(gameObject);
            return stabilityLoss;
        }

        public override float ContaminationRateAt(Vector3 position)
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

        protected override void OnEnable()
        {
            base.OnEnable();
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
            if (!completionPending || isResolved || hasRuptured || HasEnded) return;
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
                NotifyResolved();
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

        protected override void OnDisable()
        {
            if (sealPoint01 != null) sealPoint01.Completed -= OnSealCompleted;
            if (sealPoint02 != null) sealPoint02.Completed -= OnSealCompleted;
            completionPending = false;
            base.OnDisable();
        }
    }
}
