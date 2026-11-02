using UnityEngine;

namespace WardShift
{
    /// <summary>영구 마력 장치에 연결되는 폭주 수명주기와 공격 주기를 관리합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class ManaDeviceAnomaly : AnomalyBehaviour
    {
        public enum AttackKind { RadialBurst, TrackingBolt }
        [Header("UI 표시")]
        [InspectorName("이상현상 UI 이름")]
        [SerializeField] private string displayName = "마력 장치 폭주";
        [SerializeField, Min(0f), InspectorName("장치 경고 UI 표시 거리"),
         Tooltip("플레이어가 이 거리 안에 있을 때만 장치 단계·공격 경고 UI를 표시합니다. 공격 동작에는 영향을 주지 않습니다.")]
        private float warningUiDisplayDistance = 8f;
        [Header("방출과 회피")]
        [Tooltip("장치 생성 후 첫 원형 방출 경고가 시작될 때까지의 대기 시간입니다. 실제 첫 공격은 이 값에 방출 예고 시간을 더한 뒤 발생합니다.")]
        [SerializeField, Min(0.1f)] private float firstBurstDelaySeconds = 4.5f;
        [SerializeField, Min(0.1f)] private float burstWarningSeconds = 1.5f;
        [Tooltip("공격부터 다음 공격까지의 주기. 각 공격의 경고 시간은 별도로 설정합니다.")]
        [SerializeField, Min(0.2f)] private float burstPeriodSeconds = 9.5f;
        [SerializeField, Min(0.1f)] private float burstRadius = 4.25f;
        [SerializeField, Min(0f)] private float stageOneBurstContamination = 12f;
        [SerializeField, Min(0f)] private float stageTwoBurstContamination = 18f;
        [SerializeField, Min(0f)] private float stageThreeBurstContamination = 24f;

        [Header("추적 마력탄")]
        [Tooltip("외형 프리팹. 비우면 기본 발광 구체를 생성합니다. 충돌 판정은 별도 컴포넌트가 관리합니다.")]
        [SerializeField] private GameObject projectileVisualPrefab;
        [SerializeField, Min(0.1f)] private float projectileWarningSeconds = 1f;
        [SerializeField, Min(0.1f)] private float projectileSpeed = 4.5f;
        [SerializeField, Min(0f)] private float projectileHomingSeconds = 0.7f;
        [SerializeField, Min(0.05f)] private float projectileCollisionRadius = 0.3f;
        [SerializeField, Min(0.1f)] private float projectileLifetime = 5f;
        [SerializeField, Min(0f)] private float projectileSpawnHeight = 1.5f;
        [SerializeField, Min(0f)] private float projectileContamination = 15f;
        [SerializeField] private LayerMask projectileCollisionLayers = ~0;

        [Header("실제 피격 연출")]
        [Tooltip("피격 시 플레이어가 밀려나는 최대 거리(m). 벽에 막히면 실제 이동 거리는 줄어듭니다.")]
        [SerializeField, Min(0f)] private float hitKnockbackDistance = 0.65f;
        [Tooltip("넉백 이동에 걸리는 시간(초). 이동 입력은 계속 사용할 수 있습니다.")]
        [SerializeField, Min(0.01f)] private float hitKnockbackDuration = 0.22f;
        [Tooltip("카메라 추적 지점의 최대 흔들림 거리(m). 0이면 흔들리지 않습니다.")]
        [SerializeField, Min(0f)] private float hitShakeIntensity = 0.055f;
        [SerializeField, Min(0.01f)] private float hitShakeDuration = 0.22f;

        [Header("방출 회피 후 안정화")]
        [SerializeField, Min(0f)] private float recoverySeconds = 2f;
        [SerializeField, Min(1f)] private float recoveryStabilizationMultiplier = 1.5f;

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
        [Header("진단 (선택)")]
        [SerializeField] private bool logHitDiagnostics;

        private ManaDeviceVisuals deviceVisuals;
        private MagicTarget stabilizationTarget;
        private bool configured;
        private bool workedThisBurstCycle;
        private float recoveryRemaining;
        private bool alternateAttacks;
        private bool resolutionSfxPlayed;
        private AttackKind nextAttack = AttackKind.RadialBurst;

        public override string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? "마력 장치 폭주" : displayName.Trim();
        public float WarningUiDisplayDistance => Mathf.Max(0f, warningUiDisplayDistance);
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
        public AttackKind NextAttack => nextAttack;
        public string NextAttackLabel => nextAttack == AttackKind.RadialBurst ? "원형 방출" : "추적 마력탄";
        public float SecondsUntilNextAttack => SecondsUntilNextBurst;
        public float SecondsUntilNextBurst => IsHazardActive ? Mathf.Max(0f, secondsUntilNextBurst) : 0f;
        public float RecoveryRemaining => IsHazardActive ? Mathf.Max(0f, recoveryRemaining) : 0f;
        public void PlayAttackWarningSfx(WardShiftSoundId soundId)
        {
            if (!IsHazardActive || deviceVisuals == null) return;
            deviceVisuals.PlayAttackWarningSfx(soundId);
        }

        public float StabilizationSpeedMultiplier => RecoveryRemaining > 0f
            ? Mathf.Max(1f, recoveryStabilizationMultiplier) : 1f;
        public void ConfigureAttackPattern(bool alternate)
        {
            alternateAttacks = alternate;
            nextAttack = AttackKind.RadialBurst;
        }
        public void RecordStabilizationWork(MagicTarget target)
        {
            if (IsHazardActive && target == stabilizationTarget && target.IsAvailable)
                workedThisBurstCycle = true;
        }
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
            resolutionSfxPlayed = false;
            isBurstWarningActive = false;
            nextAttack = AttackKind.RadialBurst;
            workedThisBurstCycle = false;
            recoveryRemaining = 0f;
            secondsUntilNextBurst = Mathf.Max(0.1f, firstBurstDelaySeconds)
                + Mathf.Max(0.1f, burstWarningSeconds);
            configured = true;
            deviceVisuals.SetActiveAnomaly(this);
            stabilizationTarget.Completed -= OnStabilizationCompleted;
            stabilizationTarget.Completed += OnStabilizationCompleted;
            deviceVisuals.SetBurstWarning(false, BurstRadius, 0f);
            return true;
        }

        /// <summary>성장 경계를 적분하고 파열 시 기존 공통 시설 안정도 피해를 반환합니다.</summary>
        public override float AdvanceHazard(float deltaTime)
        {
            float persistentLoss = AdvanceHazard(deltaTime, out float immediateDamage);
            return persistentLoss + immediateDamage;
        }

        public override float AdvanceHazard(float deltaTime, out float immediateStabilityDamage)
        {
            immediateStabilityDamage = 0f;
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
                recoveryRemaining = 0f;
                workedThisBurstCycle = false;
                if (stabilizationTarget != null) stabilizationTarget.Completed -= OnStabilizationCompleted;
                if (deviceVisuals != null) deviceVisuals.MarkRuptured();
                immediateStabilityDamage = Mathf.Max(0f, ruptureStabilityDamage);
                Debug.Log("장치형 마력 폭주 파열: 시설 안정도 피해 후 폭주 로직 제거", this);
                PlayRuptureSfx(deviceVisuals != null ? deviceVisuals.transform.position : transform.position);
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
            while (remaining > 0f && transitions++ < 8)
            {
                float warningDuration = WarningDuration;
                float toTransition = isBurstWarningActive
                    ? Mathf.Max(0f, secondsUntilNextBurst)
                    : Mathf.Max(0f, secondsUntilNextBurst - warningDuration);
                float step = Mathf.Min(remaining, toTransition);
                secondsUntilNextBurst -= step;
                remaining -= step;
                if (recoveryRemaining > 0f)
                {
                    recoveryRemaining = Mathf.Max(0f, recoveryRemaining - step);
                    if (recoveryRemaining <= 0f && deviceVisuals != null)
                        deviceVisuals.SetRecovery(false);
                }

                if (!isBurstWarningActive && secondsUntilNextBurst <= warningDuration)
                {
                    isBurstWarningActive = true;
                    UpdateWarningVisual(secondsUntilNextBurst / warningDuration);
                    continue;
                }

                if (isBurstWarningActive && secondsUntilNextBurst <= 0f)
                {
                    if (nextAttack == AttackKind.RadialBurst) FireBurst();
                    else FireProjectile();
                    nextAttack = alternateAttacks && nextAttack == AttackKind.RadialBurst
                        ? AttackKind.TrackingBolt : AttackKind.RadialBurst;
                    secondsUntilNextBurst += Mathf.Max(WarningDuration + 0.1f, burstPeriodSeconds);
                    isBurstWarningActive = false;
                    if (deviceVisuals != null) deviceVisuals.SetAttackWarning(false,
                        BurstRadius, 0f, false);
                    continue;
                }

                if (step <= 0f) break;
            }

            if (isBurstWarningActive) UpdateWarningVisual(secondsUntilNextBurst / WarningDuration);
        }

        private float WarningDuration => nextAttack == AttackKind.TrackingBolt
            ? Mathf.Max(0.1f, projectileWarningSeconds) : Mathf.Max(0.1f, burstWarningSeconds);

        private void UpdateWarningVisual(float remainingFraction)
        {
            if (deviceVisuals != null) deviceVisuals.SetAttackWarning(true,
                BurstRadius, remainingFraction, nextAttack == AttackKind.TrackingBolt);
        }

        private void FireBurst()
        {
            if (deviceVisuals != null) deviceVisuals.ShowBurstImpact();
            PlayerContamination player = FindFirstObjectByType<PlayerContamination>();
            bool outsideRadius = player != null
                && PlanarDistance(player.transform.position, transform.position) > BurstRadius;
            bool earnedRecovery = workedThisBurstCycle && outsideRadius;
            workedThisBurstCycle = false;
            recoveryRemaining = earnedRecovery ? Mathf.Max(0f, recoverySeconds) : 0f;
            if (deviceVisuals != null) deviceVisuals.SetRecovery(recoveryRemaining > 0f);
            if (player == null || outsideRadius) return;

            float amount = dangerStage >= 3 ? stageThreeBurstContamination
                : dangerStage == 2 ? stageTwoBurstContamination : stageOneBurstContamination;
            ApplyDeviceHit(player, amount);
        }

        private void FireProjectile()
        {
            PlayerContamination player = FindFirstObjectByType<PlayerContamination>();
            if (deviceVisuals != null) deviceVisuals.ShowProjectileLaunch();
            workedThisBurstCycle = false;
            if (player == null) return;
            Vector3 origin = deviceVisuals != null ? deviceVisuals.transform.position : transform.position;
            origin += Vector3.up * Mathf.Max(0f, projectileSpawnHeight);
            Vector3 aim = player.transform.position + Vector3.up;
            Vector3 direction = (aim - origin).normalized;
            origin += direction * (Mathf.Max(0.05f, projectileCollisionRadius) + 0.4f);
            GameObject bolt = new GameObject("ManaTrackingBolt");
            bolt.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            ManaDeviceProjectile projectile = bolt.AddComponent<ManaDeviceProjectile>();
            projectile.Initialize(this, player, deviceVisuals != null ? deviceVisuals.transform : transform,
                projectileVisualPrefab, projectileCollisionLayers, projectileSpeed,
                projectileHomingSeconds, projectileCollisionRadius, projectileLifetime,
                projectileContamination, direction);
        }

        public void ApplyDeviceHit(PlayerContamination player, float amount, Vector3? impactOrigin = null)
        {
            if (player == null || amount <= 0f) return;
            float applied = player.AddContamination(amount);
            if (applied <= 0f) return;
            if (player.GetComponent<CharacterController>() != null)
            {
                DeviceHitReaction reaction = player.GetComponent<DeviceHitReaction>();
                if (reaction == null) reaction = player.gameObject.AddComponent<DeviceHitReaction>();
                reaction.Play(impactOrigin ?? transform.position, hitKnockbackDistance,
                    hitKnockbackDuration, hitShakeIntensity, hitShakeDuration);
            }
            if (logHitDiagnostics)
                Debug.Log($"장치 공격 피격: 오염도 +{applied:0.##}", player);
        }

        private void OnStabilizationCompleted(MagicTarget target)
        {
            if (target != stabilizationTarget || isResolved || hasRuptured || HasEnded) return;
            isResolved = true;
            isBurstWarningActive = false;
            recoveryRemaining = 0f;
            workedThisBurstCycle = false;
            if (deviceVisuals != null) deviceVisuals.MarkResolved();
            Debug.Log("장치형 마력 폭주 진압 완료", this);
            if (!resolutionSfxPlayed)
            {
                resolutionSfxPlayed = true;
                AudioService.Play3DAt(WardShiftSoundId.ManaDeviceStabilized,
                    deviceVisuals != null ? deviceVisuals.transform.position : transform.position,
                    GetInstanceID());
            }
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
