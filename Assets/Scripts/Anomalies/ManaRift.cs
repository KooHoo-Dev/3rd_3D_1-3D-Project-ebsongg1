using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace WardShift
{
    /// <summary>성장 단계에 맞는 봉인점 연쇄가 완료되면 균열을 제거합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10003)]
    public sealed class ManaRift : AnomalyBehaviour
    {
        [Header("봉인점 (서로 다른 자식 MagicTarget)")]
        [SerializeField] private MagicTarget sealPoint01;
        [SerializeField] private MagicTarget sealPoint02;
        [Tooltip("3단계에 사용할 추가 봉인점입니다. 3번, 4번 순서로 배치하세요.")]
        [SerializeField] private MagicTarget[] additionalSealPoints = new MagicTarget[2];

        [Header("성장 단계별 연쇄 봉인 제한 시간")]
        [Tooltip("첫 봉인점 성공부터 남은 봉인점을 모두 처리할 때까지의 시간입니다. 중간에 초기화되지 않습니다.")]
        [FormerlySerializedAs("sealChainTimeout"), SerializeField, Min(0.1f)] private float stageOneSealChainTimeout = 2f;
        [SerializeField, Min(0.1f)] private float stageTwoSealChainTimeout = 2f;
        [SerializeField, Min(0.1f)] private float stageThreeSealChainTimeout = 2f;

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
        private readonly List<MagicTarget> configuredSealPoints = new List<MagicTarget>(4);
        private readonly List<MagicTarget> sealOrder = new List<MagicTarget>(4);
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
        public override bool IsHazardActive => isActiveAndEnabled && !isResolved && !hasRuptured
            && CompletedSealCount < SealPointCount;
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

        /// <summary>현재 성장 단계에서 필요한 봉인점 수입니다.</summary>
        public int SealPointCount => GetSealPointCount(dangerStage);

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
            if (dangerStage != previousStage)
            {
                ResetSealChainForStageChange();
                DangerStageChanged?.Invoke(this, previousStage, dangerStage);
            }

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
        public bool IsSealChainInProgress => sealStep > 0 && sealStep < SealPointCount
            && !isResolved && !hasRuptured;
        public float SealChainTimeRemaining => IsSealChainInProgress
            ? Mathf.Max(0f, sealChainDeadline - Time.time) : 0f;
        public MagicTarget FirstSealPoint => sealOrder.Count > 0 ? sealOrder[0] : sealPoint01;
        public MagicTarget SecondSealPoint => sealOrder.Count > 1 ? sealOrder[1] : sealPoint02;
        public MagicTarget ExpectedSealPoint => sealStep >= 0 && sealStep < sealOrder.Count
            ? sealOrder[sealStep] : null;
        public bool IsShowingSealFailure => showingSealFailure && Time.time <= sealFailureUntil;
        public float SealChainTimeout => Mathf.Max(0.1f, dangerStage == 3 ? stageThreeSealChainTimeout
            : dangerStage == 2 ? stageTwoSealChainTimeout : stageOneSealChainTimeout);
        public bool HasValidSealPoints => ValidateSealPoints();
        public int CompletedSealCount
        {
            get
            {
                int completed = 0;
                int count = Mathf.Min(SealPointCount, configuredSealPoints.Count);
                for (int i = 0; i < count; i++)
                    if (configuredSealPoints[i] != null && configuredSealPoints[i].IsCompleted) completed++;
                return completed;
            }
        }
        public bool IsSealCastInProgress
        {
            get
            {
                int count = Mathf.Min(SealPointCount, configuredSealPoints.Count);
                for (int i = 0; i < count; i++)
                    if (configuredSealPoints[i] != null && configuredSealPoints[i].IsCasting) return true;
                return false;
            }
        }
        public float SealCastProgress
        {
            get
            {
                float progress = 0f;
                int count = Mathf.Min(SealPointCount, configuredSealPoints.Count);
                for (int i = 0; i < count; i++)
                {
                    MagicTarget target = configuredSealPoints[i];
                    if (target != null && target.IsCasting) progress = Mathf.Max(progress, target.Progress);
                }
                return progress;
            }
        }

        /// <summary>균열 전체 진압 시 한 번 발생. 이후 보상/시설 시스템에서 구독 가능.</summary>
        public event Action<ManaRift> Resolved;
        /// <summary>제한 시간 초과 또는 잘못된 대상 클릭으로 연쇄가 초기화될 때 발생.</summary>
        public event Action<ManaRift> SealChainFailed;
        /// <summary>성장으로 인해 진행 중인 봉인 연쇄가 초기화될 때 발생합니다.</summary>
        public event Action<ManaRift> SealChainReset;
        /// <summary>위험 단계가 변경될 때 이전 값과 새 값을 전달합니다.</summary>
        public event Action<ManaRift, int, int> DangerStageChanged;
        /// <summary>균열 파열 VFX가 루트 제거 전에 재생될 수 있도록 알립니다.</summary>
        public event Action<ManaRift> Ruptured;

        public bool OwnsSealPoint(MagicTarget target)
        {
            return target != null && configuredSealPoints.Contains(target);
        }

        /// <summary>봉인 마법의 한 번의 클릭을 처리합니다. 첫 성공부터 전체 연쇄 시간 제한을 적용합니다.</summary>
        public bool TryCompleteSealClick(MagicTarget target)
        {
            if (!IsHazardActive) return false;
            if (sealStep == 0)
            {
                MagicTarget first = ExpectedSealPoint;
                // Q 홀드 감지로 발견한 첫 번째 점만 봉인을 시작할 수 있습니다.
                if (target != first || first == null || !first.IsDetected) return false;
                if (!first.CompleteInstantly(MagicType.Seal)) return false;
                if (this == null || isResolved || hasRuptured) return true;
                sealStep = 1;
                sealChainDeadline = Time.time + SealChainTimeout;
                showingSealFailure = false;
                RefreshSealPointVisuals();
                return true;
            }

            if (!IsSealChainInProgress) return false;
            MagicTarget expected = ExpectedSealPoint;
            if (Time.time > sealChainDeadline || target != expected || expected == null
                || !expected.CompleteInstantly(MagicType.Seal))
            {
                FailSealChain();
                return false;
            }

            sealStep++;
            if (sealStep >= SealPointCount) sealChainDeadline = 0f;
            RefreshSealPointVisuals();
            return true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            CacheConfiguredSealPoints();
            if (!ValidateSealPoints())
            {
                Debug.LogError("ManaRift: 자식 봉인점 네 개를 연결하고 모든 대상의 마법을 Seal로 설정하세요.", this);
                // 생성 전에 AnomalySpawnPoint도 설정을 검사하며, 직접 활성화된 경우 Removed로 점유를 반환합니다.
                enabled = false;
                Destroy(gameObject);
                return;
            }

            ResetSealTargetsForCurrentStage();
            RandomizeSealOrder();
            EnsureSealPointVisuals();
            foreach (MagicTarget target in configuredSealPoints)
            {
                target.Completed += OnSealCompleted;
                target.DetectionChanged += OnSealPointDetectionChanged;
            }
            completionPending = !isResolved && CompletedSealCount == SealPointCount;
            RefreshSealPointVisuals();
        }

        private void Awake()
        {
            CacheConfiguredSealPoints();
        }

        private void CacheConfiguredSealPoints()
        {
            configuredSealPoints.Clear();
            if (sealPoint01 != null) configuredSealPoints.Add(sealPoint01);
            if (sealPoint02 != null) configuredSealPoints.Add(sealPoint02);
            if (additionalSealPoints == null) return;
            foreach (MagicTarget target in additionalSealPoints)
                if (target != null) configuredSealPoints.Add(target);
        }

        private void RandomizeSealOrder()
        {
            sealOrder.Clear();
            int count = Mathf.Min(SealPointCount, configuredSealPoints.Count);
            for (int i = 0; i < count; i++) sealOrder.Add(configuredSealPoints[i]);
            for (int i = sealOrder.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (sealOrder[i], sealOrder[j]) = (sealOrder[j], sealOrder[i]);
            }
        }

        private bool ValidateSealPoints()
        {
            CacheConfiguredSealPoints();
            if (sealPoint01 == null || sealPoint02 == null || configuredSealPoints.Count != 4)
                return false;

            for (int i = 0; i < configuredSealPoints.Count; i++)
            {
                MagicTarget target = configuredSealPoints[i];
                if (target == null || target.transform == transform || !target.transform.IsChildOf(transform)
                    || target.RequiredMagic != MagicType.Seal) return false;
                for (int j = i + 1; j < configuredSealPoints.Count; j++)
                {
                    MagicTarget other = configuredSealPoints[j];
                    if (target == other || target.transform.IsChildOf(other.transform)
                        || other.transform.IsChildOf(target.transform)) return false;
                }
            }
            return true;
        }

        private void EnsureSealPointVisuals()
        {
            foreach (MagicTarget target in configuredSealPoints)
                if (target.GetComponent<SealPointVisual>() == null)
                    target.gameObject.AddComponent<SealPointVisual>();
        }

        private void ResetSealTargetsForCurrentStage()
        {
            int activeCount = Mathf.Min(SealPointCount, configuredSealPoints.Count);
            for (int i = 0; i < configuredSealPoints.Count; i++)
            {
                MagicTarget target = configuredSealPoints[i];
                if (target == null) continue;
                target.ResetForNewUse(i < activeCount);
                target.SetDetected(false, Color.white);
                if (i < activeCount)
                {
                    if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
                    target.SetRuntimeAvailable(true);
                }
                else
                {
                    target.SetRuntimeAvailable(false);
                    target.gameObject.SetActive(false);
                }
            }
        }

        private void ResetSealChainForStageChange()
        {
            bool interruptedChain = IsSealChainInProgress;
            sealStep = 0;
            sealChainDeadline = 0f;
            showingSealFailure = false;
            completionPending = false;
            ResetSealTargetsForCurrentStage();
            RandomizeSealOrder();
            RefreshSealPointVisuals();
            if (interruptedChain) SealChainReset?.Invoke(this);
        }

        private void OnSealPointDetectionChanged(MagicTarget target, bool detected)
        {
            RefreshSealPointVisuals();
        }

        private void RefreshSealPointVisuals()
        {
            if (sealOrder.Count == 0) return;
            for (int i = 0; i < configuredSealPoints.Count; i++)
            {
                MagicTarget target = configuredSealPoints[i];
                if (target == null) continue;
                SealPointVisual visual = target.GetComponent<SealPointVisual>();
                if (visual == null) continue;
                int order = sealOrder.IndexOf(target);
                visual.SetOrder(order >= 0 ? order + 1 : 0);
                if (order < 0)
                {
                    visual.SetState(SealPointVisual.State.Hidden);
                    continue;
                }

                if (IsShowingSealFailure)
                {
                    visual.SetState(SealPointVisual.State.Failed);
                    continue;
                }
                if (order < sealStep || isResolved)
                {
                    visual.SetState(SealPointVisual.State.Completed);
                    continue;
                }
                if (sealStep > 0)
                {
                    // Q를 놓은 뒤에도 현재와 이후 순서의 룬·번호를 볼 수 있게 유지합니다.
                    visual.SetState(order == sealStep
                        ? SealPointVisual.State.Active : SealPointVisual.State.Queued);
                    continue;
                }

                visual.SetState(target.IsDetected
                    ? order == 0 ? SealPointVisual.State.Active : SealPointVisual.State.Discovered
                    : SealPointVisual.State.Hidden);
            }
        }

        private void FailSealChain()
        {
            if (!IsSealChainInProgress) return;
            sealStep = 0;
            sealChainDeadline = 0f;
            ResetSealTargetsForCurrentStage();
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
            if (!isResolved && CompletedSealCount == SealPointCount) completionPending = true;
        }

        private void LateUpdate()
        {
            if (!completionPending || isResolved || hasRuptured || HasEnded) return;
            completionPending = false;
            if (CompletedSealCount != SealPointCount) return;
            // 마지막 봉인점의 VFX와 다른 완료 구독자가 모두 실행된 뒤 균열을 처리.
            isResolved = true;
            RefreshSealPointVisuals();
            PlayCompleteEffect();
            Debug.Log($"마력 균열 진압 완료 ({SealPointCount}/{SealPointCount})", this);
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
            foreach (MagicTarget target in configuredSealPoints)
            {
                if (target == null) continue;
                target.Completed -= OnSealCompleted;
                target.DetectionChanged -= OnSealPointDetectionChanged;
            }
            completionPending = false;
            if (!isResolved && !hasRuptured && IsSealChainInProgress)
            {
                sealStep = 0;
                sealChainDeadline = 0f;
                ResetSealTargetsForCurrentStage();
                SealChainReset?.Invoke(this);
            }
            base.OnDisable();
        }

        private static int GetSealPointCount(int stage)
        {
            return stage >= 3 ? 4 : stage == 2 ? 3 : 2;
        }
    }
}
