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
        [Header("연쇄 봉인")]
        [Tooltip("첫 봉인점 클릭부터 두 번째 클릭까지 허용되는 시간입니다.")]
        [SerializeField, Range(1.5f, 3f)] private float sealChainTimeout = 2f;

        [Header("균열 전체 완료 효과")]
        [Tooltip("프로젝트 창의 GameObject 프리팹. 비워 두어도 진압은 가능합니다.")]
        [SerializeField] private GameObject completeVFXPrefab;
        [Tooltip("프리팹 내부의 생성 위치 Transform입니다. 비워 두면 균열 루트를 사용합니다.")]
        [SerializeField] private Transform completeVFXAnchor;
        [SerializeField, Min(0.1f)] private float completeVFXLifetime = 1.5f;

        [Header("진압 완료")]
        [SerializeField] private UnityEvent onResolved = new UnityEvent();
        [SerializeField] private bool isResolved;
        private bool completionPending;
        private int sealStep;
        private MagicTarget firstSealPoint;
        private MagicTarget secondSealPoint;
        private bool sealOrderInitialized;
        private float sealChainDeadline;
        private float sealFailureUntil;
        private bool showingSealFailure;

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
        public override float SecondsUntilRupture
        {
            get
            {
                float second = Mathf.Max(0.1f, stageTwoAt);
                float third = Mathf.Max(second + 0.1f, stageThreeAt);
                float rupture = Mathf.Max(third + 0.1f, ruptureAt);
                return Mathf.Max(0f, rupture - age);
            }
        }
        public override bool HasOverloaded => hasRuptured;
        public override int ResolutionReward => dangerStage;
        public override AnomalySpawnPoint.SurfaceType SpawnSurface => AnomalySpawnPoint.SurfaceType.Wall;
        public override bool IsHazardActive => isActiveAndEnabled && !isResolved && !hasRuptured && CompletedSealCount < 2;
        public override float SecondsUntilNextStage
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
            int previousStage = dangerStage;
            age = currentAge;
            dangerStage = currentAge >= third ? 3 : currentAge >= second ? 2 : 1;
            if (dangerStage != previousStage) DangerStageChanged?.Invoke(this, previousStage, dangerStage);

            float stageTwoSeconds = Mathf.Max(0f, Mathf.Min(currentAge, third) - Mathf.Max(previousAge, second));
            float stageThreeSeconds = Mathf.Max(0f, Mathf.Min(currentAge, rupture) - Mathf.Max(previousAge, third));
            float stabilityLoss = stageTwoSeconds * Mathf.Max(0f, stageTwoStabilityLossPerSecond)
                + stageThreeSeconds * Mathf.Max(0f, stageThreeStabilityLossPerSecond);

            if (!ruptureReached) return stabilityLoss;
            hasRuptured = true;
            stabilityLoss += Mathf.Max(0f, ruptureStabilityDamage);
            Debug.Log("마력 균열 파열: 시설 안정도 피해 후 제거", this);
            Ruptured?.Invoke(this);
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
        public bool IsSealChainInProgress => sealStep == 1 && !isResolved && !hasRuptured;
        public float SealChainTimeRemaining => IsSealChainInProgress
            ? Mathf.Max(0f, sealChainDeadline - Time.time) : 0f;
        public MagicTarget FirstSealPoint => firstSealPoint != null ? firstSealPoint : sealPoint01;
        public MagicTarget SecondSealPoint => secondSealPoint != null ? secondSealPoint : sealPoint02;
        public MagicTarget ExpectedSealPoint => sealStep == 0 ? FirstSealPoint
            : sealStep == 1 ? SecondSealPoint : null;
        public bool IsShowingSealFailure => showingSealFailure && Time.time <= sealFailureUntil;
        public float SealChainTimeout => Mathf.Clamp(sealChainTimeout, 1.5f, 3f);
        public bool HasValidSealPoints => ValidateSealPoints();
        public int CompletedSealCount => (sealPoint01 != null && sealPoint01.IsCompleted ? 1 : 0)
            + (sealPoint02 != null && sealPoint02.IsCompleted ? 1 : 0);
        public bool IsSealCastInProgress => (sealPoint01 != null && sealPoint01.IsCasting)
            || (sealPoint02 != null && sealPoint02.IsCasting);
        public float SealCastProgress => Mathf.Max(
            sealPoint01 != null && sealPoint01.IsCasting ? sealPoint01.Progress : 0f,
            sealPoint02 != null && sealPoint02.IsCasting ? sealPoint02.Progress : 0f);

        /// <summary>균열 전체 진압 시 한 번 발생. 이후 보상/시설 시스템에서 구독 가능.</summary>
        public event Action<ManaRift> Resolved;
        /// <summary>제한 시간 초과 또는 잘못된 대상 클릭으로 연쇄가 초기화될 때 발생.</summary>
        public event Action<ManaRift> SealChainFailed;
        /// <summary>위험 단계가 변경될 때 이전 값과 새 값을 전달합니다.</summary>
        public event Action<ManaRift, int, int> DangerStageChanged;
        /// <summary>균열 파열 VFX가 루트 제거 전에 재생될 수 있도록 알립니다.</summary>
        public event Action<ManaRift> Ruptured;

        public bool OwnsSealPoint(MagicTarget target)
        {
            return target != null && (target == sealPoint01 || target == sealPoint02);
        }

        /// <summary>봉인 마법의 한 번의 클릭을 처리합니다. 두 번째 점만 연쇄 중 유효합니다.</summary>
        public bool TryCompleteSealClick(MagicTarget target)
        {
            if (!IsHazardActive) return false;
            if (sealStep == 0)
            {
                // Q 홀드 감지로 발견한 첫 번째 점만 봉인을 시작할 수 있습니다.
                if (target != FirstSealPoint || !target.IsDetected) return false;
                if (!target.CompleteInstantly(MagicType.Seal)) return false;
                if (this == null || isResolved || hasRuptured) return true;
                sealStep = 1;
                sealChainDeadline = Time.time + SealChainTimeout;
                showingSealFailure = false;
                RefreshSealPointVisuals();
                return true;
            }

            if (sealStep != 1) return false;
            MagicTarget expected = SecondSealPoint;
            if (Time.time > sealChainDeadline || target != expected
                || expected == null || !expected.CompleteInstantly(MagicType.Seal))
            {
                FailSealChain();
                return false;
            }

            sealStep = 2;
            sealChainDeadline = 0f;
            RefreshSealPointVisuals();
            return true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!ValidateSealPoints())
            {
                Debug.LogError("ManaRift: 서로 다른 자식 봉인점 두 개를 연결하고 두 대상의 마법을 Seal로 설정하세요.", this);
                // 생성 전에 AnomalySpawnPoint도 설정을 검사하며, 직접 활성화된 경우 Removed로 점유를 반환합니다.
                enabled = false;
                Destroy(gameObject);
                return;
            }
            InitializeSealOrder();
            EnsureSealPointVisuals();
            sealPoint01.Completed += OnSealCompleted;
            sealPoint02.Completed += OnSealCompleted;
            sealPoint01.DetectionChanged += OnSealPointDetectionChanged;
            sealPoint02.DetectionChanged += OnSealPointDetectionChanged;
            // 비활성화 중 완료된 경우에도 재활성화 시 실제 상태를 확인.
            completionPending = !isResolved && CompletedSealCount == 2;
            RefreshSealPointVisuals();
        }

        private void Awake()
        {
            InitializeSealOrder();
        }

        private void InitializeSealOrder()
        {
            if (sealOrderInitialized || sealPoint01 == null || sealPoint02 == null) return;
            if (UnityEngine.Random.Range(0, 2) == 0)
            {
                firstSealPoint = sealPoint01;
                secondSealPoint = sealPoint02;
            }
            else
            {
                firstSealPoint = sealPoint02;
                secondSealPoint = sealPoint01;
            }
            sealOrderInitialized = true;
        }

        private bool ValidateSealPoints()
        {
            return sealPoint01 != null && sealPoint02 != null && sealPoint01 != sealPoint02
                && sealPoint01.transform != transform && sealPoint02.transform != transform
                && sealPoint01.transform.IsChildOf(transform) && sealPoint02.transform.IsChildOf(transform)
                && !sealPoint01.transform.IsChildOf(sealPoint02.transform)
                && !sealPoint02.transform.IsChildOf(sealPoint01.transform)
                && sealPoint01.RequiredMagic == MagicType.Seal && sealPoint02.RequiredMagic == MagicType.Seal;
        }

        private void EnsureSealPointVisuals()
        {
            if (sealPoint01.GetComponent<SealPointVisual>() == null)
                sealPoint01.gameObject.AddComponent<SealPointVisual>();
            if (sealPoint02.GetComponent<SealPointVisual>() == null)
                sealPoint02.gameObject.AddComponent<SealPointVisual>();
        }

        private void OnSealPointDetectionChanged(MagicTarget target, bool detected)
        {
            RefreshSealPointVisuals();
        }

        private void RefreshSealPointVisuals()
        {
            MagicTarget expectedFirst = FirstSealPoint;
            MagicTarget expectedSecond = SecondSealPoint;
            if (expectedFirst == null || expectedSecond == null) return;
            SealPointVisual first = expectedFirst.GetComponent<SealPointVisual>();
            SealPointVisual second = expectedSecond.GetComponent<SealPointVisual>();
            if (first == null || second == null) return;

            if (IsShowingSealFailure)
            {
                first.SetState(SealPointVisual.State.Failed);
                second.SetState(SealPointVisual.State.Failed);
                return;
            }

            if (sealStep == 1)
            {
                first.SetState(SealPointVisual.State.Completed);
                second.SetState(SealPointVisual.State.Active);
                return;
            }
            if (sealStep == 2 || isResolved)
            {
                first.SetState(SealPointVisual.State.Completed);
                second.SetState(SealPointVisual.State.Completed);
                return;
            }

            first.SetState(expectedFirst.IsDetected
                ? SealPointVisual.State.Active : SealPointVisual.State.Hidden);
            second.SetState(expectedSecond.IsDetected
                ? SealPointVisual.State.Discovered : SealPointVisual.State.Hidden);
        }

        private void FailSealChain()
        {
            if (sealStep != 1) return;
            sealStep = 0;
            sealChainDeadline = 0f;
            sealPoint01.ResetForNewUse();
            sealPoint02.ResetForNewUse();
            sealPoint01.SetDetected(false, Color.white);
            sealPoint02.SetDetected(false, Color.white);
            showingSealFailure = true;
            sealFailureUntil = Time.time + 0.45f;
            RefreshSealPointVisuals();
            SealChainFailed?.Invoke(this);
        }

        private void Update()
        {
            // Time.time is scaled game time, so the chain freezes while the game is paused.
            if (IsSealChainInProgress && Time.time > sealChainDeadline)
                FailSealChain();
            else if (showingSealFailure && Time.time > sealFailureUntil)
            {
                showingSealFailure = false;
                RefreshSealPointVisuals();
            }
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
            foreach (Collider effectCollider in effect.GetComponentsInChildren<Collider>(true))
                effectCollider.enabled = false;
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
            Destroy(effect, Mathf.Max(0.1f, completeVFXLifetime));
        }

        protected override void OnDisable()
        {
            if (sealPoint01 != null) sealPoint01.Completed -= OnSealCompleted;
            if (sealPoint02 != null) sealPoint02.Completed -= OnSealCompleted;
            if (sealPoint01 != null) sealPoint01.DetectionChanged -= OnSealPointDetectionChanged;
            if (sealPoint02 != null) sealPoint02.DetectionChanged -= OnSealPointDetectionChanged;
            completionPending = false;
            if (!isResolved && !hasRuptured && sealStep == 1)
            {
                sealStep = 0;
                sealChainDeadline = 0f;
                if (sealPoint01 != null) sealPoint01.ResetForNewUse();
                if (sealPoint02 != null) sealPoint02.ResetForNewUse();
            }
            base.OnDisable();
        }
    }
}
