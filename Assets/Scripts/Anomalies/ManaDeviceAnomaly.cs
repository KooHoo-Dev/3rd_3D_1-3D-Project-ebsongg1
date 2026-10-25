using UnityEngine;

namespace WardShift
{
    /// <summary>영구 마력 장치에 연결되는 폭주 수명주기와 회피형 방출을 관리합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class ManaDeviceAnomaly : AnomalyBehaviour
    {
        [Header("방출과 회피")]
        [SerializeField, Min(0.1f)] private float firstBurstDelaySeconds = 4.5f;
        [SerializeField, Min(0.1f)] private float burstWarningSeconds = 1.5f;
        [Tooltip("방출부터 다음 방출까지의 주기. 다음 방출 전 1.5초는 경고로 사용합니다.")]
        [SerializeField, Min(0.2f)] private float burstPeriodSeconds = 6.5f;
        [SerializeField, Min(0.1f)] private float burstRadius = 4.25f;
        [SerializeField, Min(0f)] private float stageOneBurstContamination = 10f;
        [SerializeField, Min(0f)] private float stageTwoBurstContamination = 15f;
        [SerializeField, Min(0f)] private float stageThreeBurstContamination = 20f;

        [Header("성장·파열 (생성 후 누적 초)")]
        [SerializeField, Min(0.1f)] private float stageTwoAt = 20f;
        [SerializeField, Min(0.2f)] private float stageThreeAt = 40f;
        [SerializeField, Min(0.3f)] private float ruptureAt = 60f;
        [Header("시설 안정도 손실")]
        [SerializeField, Min(0f)] private float stageTwoStabilityLossPerSecond = 0.5f;
        [SerializeField, Min(0f)] private float stageThreeStabilityLossPerSecond = 1f;
        [SerializeField, Min(0f)] private float ruptureStabilityDamage = 20f;

        [Header("실행 상태 (읽기 전용)")]
        [SerializeField] private float age;
        [SerializeField] private int dangerStage = 1;
        [SerializeField] private float secondsUntilNextBurst;
        [SerializeField] private bool isBurstWarningActive;
        [SerializeField] private bool hasRuptured;
        [SerializeField] private bool isResolved;

        private ManaDeviceVisuals deviceVisuals;
        private MagicTarget stabilizationTarget;
        private bool configured;

        public override string DisplayName => "장치형 마력 폭주";
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
        public override AnomalySpawnPoint.SurfaceType SpawnSurface => AnomalySpawnPoint.SurfaceType.Device;
        public override bool IsHazardActive => isActiveAndEnabled && configured && !isResolved
            && !hasRuptured && !HasEnded;
        public float Age => age;
        public float BurstRadius => Mathf.Max(Mathf.Max(0.1f, burstRadius),
            deviceVisuals != null ? deviceVisuals.StabilizeRange + 1.25f : 0.1f);
        public bool IsBurstWarningActive => isBurstWarningActive;
        /// <summary>HUD에서 읽는 기존 장치 대상의 누적 진행도.</summary>
        public float StabilizationProgress => stabilizationTarget != null ? stabilizationTarget.Progress : 0f;
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

        /// <summary>Device SpawnPoint의 영구 외형에 폭주 로직을 연결합니다.</summary>
        public bool ConfigureFromSpawnPoint(AnomalySpawnPoint spawnPoint)
        {
            if (spawnPoint == null) return false;
            deviceVisuals = spawnPoint.DeviceVisuals;
            stabilizationTarget = deviceVisuals != null ? deviceVisuals.StabilizationTarget : null;
            if (deviceVisuals == null || stabilizationTarget == null
                || stabilizationTarget.RequiredMagic != MagicType.Stabilize
                || stabilizationTarget.UseTimingCast || !stabilizationTarget.PreserveProgressOnCancel)
            {
                Debug.LogError($"{spawnPoint.name}: 장치 외형에 누적 안정화가 가능한 Stabilize MagicTarget을 연결하세요.", spawnPoint);
                return false;
            }
            if (!deviceVisuals.BeginOverload(BurstRadius)) return false;

            age = 0f;
            dangerStage = 1;
            hasRuptured = false;
            isResolved = false;
            isBurstWarningActive = false;
            secondsUntilNextBurst = Mathf.Max(0.1f, firstBurstDelaySeconds)
                + Mathf.Max(0.1f, burstWarningSeconds);
            configured = true;
            stabilizationTarget.Completed -= OnStabilizationCompleted;
            stabilizationTarget.Completed += OnStabilizationCompleted;
            deviceVisuals.SetBurstWarning(false, BurstRadius, 0f);
            return true;
        }

        /// <summary>성장 경계를 적분하고 파열 시 기존 공통 시설 안정도 피해를 반환합니다.</summary>
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

            if (ruptureReached)
            {
                hasRuptured = true;
                if (stabilizationTarget != null) stabilizationTarget.Completed -= OnStabilizationCompleted;
                if (deviceVisuals != null) deviceVisuals.MarkRuptured();
                stabilityLoss += Mathf.Max(0f, ruptureStabilityDamage);
                Debug.Log("장치형 마력 폭주 파열: 시설 안정도 피해 후 폭주 로직 제거", this);
                NotifyRuptured();
                Destroy(gameObject);
                return stabilityLoss;
            }

            AdvanceBurstCycle(deltaTime);
            return stabilityLoss;
        }

        private void AdvanceBurstCycle(float deltaTime)
        {
            float remaining = deltaTime;
            int transitions = 0;
            float warningDuration = Mathf.Max(0.1f, burstWarningSeconds);
            float period = Mathf.Max(warningDuration + 0.1f, burstPeriodSeconds);
            while (remaining > 0f && transitions++ < 8)
            {
                float toTransition = isBurstWarningActive
                    ? Mathf.Max(0f, secondsUntilNextBurst)
                    : Mathf.Max(0f, secondsUntilNextBurst - warningDuration);
                float step = Mathf.Min(remaining, toTransition);
                secondsUntilNextBurst -= step;
                remaining -= step;

                if (!isBurstWarningActive && secondsUntilNextBurst <= warningDuration)
                {
                    isBurstWarningActive = true;
                    if (deviceVisuals != null)
                        deviceVisuals.SetBurstWarning(true, BurstRadius,
                            secondsUntilNextBurst / warningDuration);
                    continue;
                }

                if (isBurstWarningActive && secondsUntilNextBurst <= 0f)
                {
                    FireBurst();
                    secondsUntilNextBurst += period;
                    isBurstWarningActive = false;
                    if (deviceVisuals != null) deviceVisuals.SetBurstWarning(false, BurstRadius, 0f);
                    continue;
                }

                if (step <= 0f) break;
            }

            if (isBurstWarningActive && deviceVisuals != null)
                deviceVisuals.SetBurstWarning(true, BurstRadius,
                    secondsUntilNextBurst / warningDuration);
        }

        private void FireBurst()
        {
            if (deviceVisuals != null) deviceVisuals.ShowBurstImpact();
            PlayerContamination player = FindFirstObjectByType<PlayerContamination>();
            if (player == null || player.InSafeZone
                || PlanarDistance(player.transform.position, transform.position) > BurstRadius)
                return;

            float amount = dangerStage >= 3 ? stageThreeBurstContamination
                : dangerStage == 2 ? stageTwoBurstContamination : stageOneBurstContamination;
            player.AddContamination(Mathf.Max(0f, amount));
            Debug.Log($"장치 마력 방출 피격: 오염도 +{Mathf.Max(0f, amount):F0}", player);
        }

        private void OnStabilizationCompleted(MagicTarget target)
        {
            if (target != stabilizationTarget || isResolved || hasRuptured || HasEnded) return;
            isResolved = true;
            isBurstWarningActive = false;
            if (deviceVisuals != null) deviceVisuals.ReturnToNormal();
            Debug.Log("장치형 마력 폭주 진압 완료", this);
            try { NotifyResolved(); }
            finally { if (this != null) Destroy(gameObject); }
        }

        private static float PlanarDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        protected override void OnDisable()
        {
            if (stabilizationTarget != null) stabilizationTarget.Completed -= OnStabilizationCompleted;
            if (!isResolved && !hasRuptured && deviceVisuals != null) deviceVisuals.ReturnToNormal();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (stabilizationTarget != null) stabilizationTarget.Completed -= OnStabilizationCompleted;
            if (!isResolved && !hasRuptured && deviceVisuals != null) deviceVisuals.ReturnToNormal();
            base.OnDestroy();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = isBurstWarningActive ? new Color(1f, 0.1f, 0.05f, 0.9f)
                : new Color(1f, 0.45f, 0.05f, 0.75f);
            float radius = Mathf.Max(Mathf.Max(0.1f, burstRadius),
                deviceVisuals != null ? deviceVisuals.StabilizeRange + 1.25f : 0.1f);
            Vector3 center = transform.position;
            center.y += 0.06f;
            Gizmos.DrawWireSphere(center, radius);
        }
    }
}
