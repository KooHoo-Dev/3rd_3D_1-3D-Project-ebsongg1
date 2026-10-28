using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace WardShift
{
    /// <summary>DAY 시작, 근무 시간, 진압 보상, CLEAR/FAILED와 다음 DAY 전환을 관리.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10006)]
    public sealed partial class DayFlowManager : MonoBehaviour, IDayRetryCheckpointParticipant
    {
        [Header("DAY 설정")]
        [SerializeField, Min(1f)] private float dayDurationSeconds = 180f;
        [SerializeField, Min(0f)] private float spawnStopBeforeEndSeconds = 30f;
        [SerializeField, Min(1)] private int finalDay = 7;
        [FormerlySerializedAs("minimumRiftReward"), SerializeField, Min(0)] private int minimumAnomalyReward = 1;
        [FormerlySerializedAs("maximumRiftReward"), SerializeField, Min(0)] private int maximumAnomalyReward = 2;
        [Header("강화 설정")]
        [SerializeField, Min(0)] private int[] upgradeStagePrices = { 4, 8 };
        [Header("DAY 스킵 (Editor / Development Build)")]
        [SerializeField] private bool enableDaySkipCheat = true;
        [Tooltip("Development Build에서 F8 DAY 스킵을 허용합니다. 일반 빌드에서는 이 값과 관계없이 비활성화됩니다.")]
        [SerializeField] private bool demoMode;
        [SerializeField] private Key daySkipCheatKey = Key.F8;
        [Header("씬 참조")]
        [SerializeField] private WardStatus wardStatus;
        [SerializeField] private AnomalySpawner anomalySpawner;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MagicCaster magicCaster;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform startPoint;
        [SerializeField] private PlayerPickupInteractor pickupInteractor;
        [SerializeField] private FacilityZoneTracker zoneTracker;

        [Header("시작 설정")]
        [FormerlySerializedAs("currentDay"), SerializeField, Min(1)] private int startingDay = 1;
        [Header("시작 강화 단계")]
        [FormerlySerializedAs("mobilityUpgradeLevel"), SerializeField, Range(0, 2)] private int startingMobilityLevel;
        [FormerlySerializedAs("contaminationResistanceLevel"), SerializeField, Range(0, 2)] private int startingResistanceLevel;
        [FormerlySerializedAs("fieldPurificationLevel"), SerializeField, Range(0, 2)] private int startingPurificationLevel;

        private DaySession session;
        private DayRetryCheckpoint checkpoint;
        private DayInputController dayInput;
        private DayPlayerUpgradeEffects playerUpgradeEffects;
        private DayUpgradeController upgrades;
        private DaySupplyController supply;
        private DayUiController dayUi;
        private DaySceneController dayScene;
        private DayAnomalyRewardController anomalyRewards;
#if UNITY_EDITOR
        private bool useFloodConditionTestOverride;
        private FloodCondition floodConditionTestOverride;
#endif
        private DaySession Session => session ?? (session = new DaySession(startingDay));
        private int currentDay => Session.CurrentDay;
        private DayState state => Session.State;
        private float remainingSeconds => Session.RemainingSeconds;
        private int dayRandomSeed => Session.DayRandomSeed;
        private FloodCondition activeFloodCondition => Session.ActiveFloodCondition;
        private FloodCondition nextDayFloodCondition => Session.NextDayFloodCondition;
        private FloodCondition previousDayFloodCondition => Session.PreviousDayFloodCondition;

        private bool CanUseDaySkip
        {
            get
            {
#if UNITY_EDITOR
                return enableDaySkipCheat;
#elif DEVELOPMENT_BUILD
                return enableDaySkipCheat && demoMode;
#else
                return false;
#endif
            }
        }

        internal bool CanUseDaySkipInput => CanUseDaySkip;

        private void Awake()
        {
            _ = Session;
            if (wardStatus == null) wardStatus = FindFirstObjectByType<WardStatus>();
            if (anomalySpawner == null) anomalySpawner = FindFirstObjectByType<AnomalySpawner>();
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (magicCaster == null) magicCaster = FindFirstObjectByType<MagicCaster>();
            if (playerRoot == null && wardStatus != null && wardStatus.Player != null)
                playerRoot = wardStatus.Player.transform;
            if (pickupInteractor == null) pickupInteractor = FindFirstObjectByType<PlayerPickupInteractor>();
            if (zoneTracker == null) zoneTracker = FindFirstObjectByType<FacilityZoneTracker>();
            checkpoint = GetComponent<DayRetryCheckpoint>();
            if (checkpoint == null) checkpoint = gameObject.AddComponent<DayRetryCheckpoint>();
            supply = GetComponent<DaySupplyController>();
            if (supply == null) supply = gameObject.AddComponent<DaySupplyController>();
            supply.Configure(this, wardStatus, inventory, checkpoint);
            dayUi = GetComponent<DayUiController>();
            if (dayUi == null) dayUi = gameObject.AddComponent<DayUiController>();
            dayUi.Configure(this, wardStatus, open => MapVisibilityChanged?.Invoke(open));
            dayScene = GetComponent<DaySceneController>();
            if (dayScene == null) dayScene = gameObject.AddComponent<DaySceneController>();
            dayScene.Configure(anomalySpawner, playerRoot, startPoint);
            anomalyRewards = GetComponent<DayAnomalyRewardController>();
            if (anomalyRewards == null) anomalyRewards = gameObject.AddComponent<DayAnomalyRewardController>();
            anomalyRewards.Configure(this, inventory);
            playerUpgradeEffects = GetComponent<DayPlayerUpgradeEffects>();
            if (playerUpgradeEffects == null) playerUpgradeEffects = gameObject.AddComponent<DayPlayerUpgradeEffects>();
            playerUpgradeEffects.Configure(playerRoot);
            playerUpgradeEffects.CaptureBaseSpeeds();
            upgrades = GetComponent<DayUpgradeController>();
            if (upgrades == null) upgrades = gameObject.AddComponent<DayUpgradeController>();
            upgrades.Configure(inventory, playerUpgradeEffects,
                startingMobilityLevel, startingResistanceLevel, startingPurificationLevel);
            dayInput = GetComponent<DayInputController>();
            if (dayInput == null) dayInput = gameObject.AddComponent<DayInputController>();
            dayInput.Configure(this, CanUseDaySkip, daySkipCheatKey);
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
        }

        private void OnEnable()
        {
            if (dayInput != null) dayInput.enabled = true;
            if (wardStatus != null) wardStatus.Failed += OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned += OnAnomalySpawned;
        }

        private void Start()
        {
            if (!IsConfigured)
            {
                Debug.LogError("DayFlowManager: Setup DAY Progression 메뉴로 씬 참조를 연결하세요.", this);
                enabled = false;
                return;
            }
            if (wardStatus.HasFailed) OnDayFailed(wardStatus.FailureReason);
            else BeginDay();
        }

        private void LateUpdate()
        {
            if (dayInput != null && dayInput.SuppressDayTickThisFrame) return;
            if (state != DayState.Playing || Time.timeScale <= 0f) return;
            if (wardStatus != null && wardStatus.HasFailed)
            {
                OnDayFailed(wardStatus.FailureReason);
                return;
            }

            if (remainingSeconds > 0f)
            {
                session.AdvanceTime(Time.deltaTime);
                if (anomalySpawner != null)
                    anomalySpawner.SetSpawnWindowOpen(remainingSeconds > Mathf.Max(0f, spawnStopBeforeEndSeconds));
            }

            if (remainingSeconds <= 0f)
            {
                if (anomalySpawner != null)
                {
                    anomalySpawner.SetSpawningEnabled(false);
                    anomalySpawner.SetSpawnWindowOpen(false);
                }
                int activeCount = anomalySpawner != null ? anomalySpawner.ActiveAnomalyCount : 0;
                if (activeCount > 0)
                {
                    if (!session.IsCleaningUp)
                    {
                        anomalyRewards.FreezeRewardsForCleanup(anomalySpawner.ActiveAnomalies);
                        session.BeginCleanup();
                    }
                    return;
                }
                if (HasSurvivedDay()) CompleteDay();
                else if (wardStatus == null || !wardStatus.HasFailed)
                    OnDayFailed("근무 종료 시 생존 조건을 만족하지 못했습니다.");
            }
        }

        /// <summary>DAY CLEAR 화면의 다음 날 버튼이 호출. 중복 클릭은 상태 검사로 무시.</summary>
        public void BeginNextDay()
        {
            if (state != DayState.DayClear) return;
            session.MoveToNextDay(FinalDay, CreateDaySeed);
            BeginDay();
        }

        /// <summary>디버그용 DAY 스킵. 진행 중 현상은 보상 없이 정리하고 다음 DAY를 시작합니다.</summary>
        public void CheatAdvanceDay()
        {
            if (!CanUseDaySkip) return;
            if (wardStatus != null && wardStatus.HasFailed && state == DayState.Playing)
            {
                OnDayFailed(wardStatus.FailureReason);
                return;
            }

            if (state == DayState.Playing)
            {
                if (anomalySpawner != null)
                {
                    anomalySpawner.SetSpawningEnabled(false);
                    anomalySpawner.SetSpawnWindowOpen(false);
                }
                dayScene.ClearAllAnomalies();
                CompleteDay();
            }

            if (state == DayState.DayClear) BeginNextDay();
        }

        /// <summary>FAILED 화면에서 현재 DAY 시작 체크포인트로 재도전.</summary>
        public void RetryCurrentDay()
        {
            if (state != DayState.DayFailed) return;
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawningEnabled(false);
                anomalySpawner.SetSpawnWindowOpen(false);
            }
            anomalyRewards.UnsubscribeAll();
            dayScene.ClearAllAnomalies();
            session.PrepareRetry();
            dayUi.SetMapOpen(false, false);
            checkpoint.Restore(inventory);
            if (pickupInteractor != null) pickupInteractor.ClearCurrentPickup();
            StartCoroutine(BeginDayNextFrame());
        }

        /// <summary>기존 UI/호출부 호환용 이름. 동작은 현재 DAY 재도전입니다.</summary>
        public void RestartFromDayOne() => RetryCurrentDay();

        private void BeginDay(bool isRetry = false)
        {
            dayUi.CloseInventory();
            supply.ClosePanel();
            dayUi.SetMapOpen(false, false);
            anomalyRewards.ResetForDay();
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawningEnabled(false);
                anomalySpawner.SetSpawnWindowOpen(false);
            }
            dayScene.ClearAllAnomalies();
            dayScene.ResetManaDevices();
            dayScene.TeleportPlayerToStart();
            if (dayRandomSeed == 0) session.EnsureDaySeed(CreateDaySeed());
            FloodCondition selectedCondition = isRetry ? activeFloodCondition
                : SelectFloodCondition(currentDay, dayRandomSeed, previousDayFloodCondition);
            session.SelectConditionForDay(isRetry, selectedCondition);
            inventory.BeginDay();
            if (wardStatus != null) wardStatus.ResetForNewDay();
            if (!isRetry) upgrades.ResetPurchaseForNewPhase();
            supply.BeginDay(currentDay, isRetry);
            ApplyUpgradeEffects();
            session.BeginDay(dayDurationSeconds);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
            if (magicCaster != null)
            {
                magicCaster.ResetForNewDay();
                magicCaster.SetInputBlocked(false);
            }
            if (anomalySpawner != null)
            {
                anomalySpawner.ConfigureForDay(currentDay, dayRandomSeed);
                anomalySpawner.SetSpawnWindowOpen(remainingSeconds > Mathf.Max(0f, spawnStopBeforeEndSeconds));
                checkpoint.Capture(inventory);
                anomalySpawner.SetSpawningEnabled(true);
            }
            else checkpoint.Capture(inventory);

            if (supply.Pending) OpenDaySupplyChoice();
        }

        private void CompleteDay()
        {
            if (state != DayState.Playing) return;
            session.CompleteDay(FinalDay);
            ApplyUpgradeEffects();
            PrepareNextDayCondition();
            upgrades.ResetPurchaseForNewPhase();
            supply.ClearAfterDay();
            dayUi.SetMapOpen(false, false);
            dayUi.CloseInventory();
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawningEnabled(false);
                anomalySpawner.SetSpawnWindowOpen(false);
            }
            if (magicCaster != null) magicCaster.SetInputBlocked(true);
            wardStatus.PauseGameForUI();
        }

        private void OnDayFailed(string reason)
        {
            if (state == DayState.DayClear || state == DayState.DayFailed) return;
            session.FailDay();
            dayUi.SetMapOpen(false, false);
            dayUi.CloseInventory();
            supply.ClosePanel();
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            if (anomalySpawner != null) anomalySpawner.SetSpawnWindowOpen(false);
            if (magicCaster != null) magicCaster.SetInputBlocked(true);
            if (wardStatus != null) wardStatus.PauseGameForUI();
        }

        private void OnAnomalySpawned(AnomalyBehaviour anomaly)
        {
            if (anomalyRewards != null) anomalyRewards.Register(anomaly);
        }

        private void OnAnomalyResolved(AnomalyBehaviour anomaly)
        {
            if (anomalyRewards != null) anomalyRewards.RewardResolution(anomaly);
        }

        private bool HasSurvivedDay()
        {
            return wardStatus != null && !wardStatus.HasFailed && wardStatus.Stability > 0f
                && wardStatus.Player != null && wardStatus.Player.Value < 100f;
        }

        public object CaptureDayRetryState()
        {
            return new DayRetryState(upgrades.CaptureState(), activeFloodCondition,
                previousDayFloodCondition, supply.Pending, supply.Claimed);
        }

        public void RestoreDayRetryState(object stateSnapshot)
        {
            if (!(stateSnapshot is DayRetryState snapshot)) return;
            upgrades.RestoreState(snapshot.Upgrade);
            session.RestoreConditions(snapshot.ActiveFloodCondition, snapshot.PreviousDayFloodCondition);
            supply.RestoreState(snapshot.DaySupplyPending, snapshot.DaySupplyClaimed);
            ApplyUpgradeEffects();
        }

        private void ApplyUpgradeEffects()
        {
            if (upgrades != null) upgrades.ApplyEffects(SafeZoneRecoveryMultiplier, Player);
        }

        private FloodCondition SelectFloodCondition(int day, int seed, FloodCondition previousCondition)
        {
#if UNITY_EDITOR
            return FloodConditionRules.SelectForDay(day, seed, previousCondition,
                useFloodConditionTestOverride, floodConditionTestOverride);
#else
            return FloodConditionRules.SelectForDay(day, seed, previousCondition);
#endif
        }

        private void PrepareNextDayCondition()
        {
            session.ClearNextDayPlan();
            if (currentDay >= FinalDay) return;

            int nextDay = currentDay + 1;
            int seed = CreateDaySeed();
            session.PlanNextDay(nextDay, seed,
                SelectFloodCondition(nextDay, seed, previousDayFloodCondition));
        }

        private void EnsureUpgradePrices()
        {
            if (upgradeStagePrices == null || upgradeStagePrices.Length != 2)
                upgradeStagePrices = new[] { 4, 8 };
        }

        private void OnValidate()
        {
            EnsureUpgradePrices();
            startingMobilityLevel = DayUpgradeRules.ClampLevel(startingMobilityLevel);
            startingResistanceLevel = DayUpgradeRules.ClampLevel(startingResistanceLevel);
            startingPurificationLevel = DayUpgradeRules.ClampLevel(startingPurificationLevel);
        }

        private static int CreateDaySeed() => UnityEngine.Random.Range(1, int.MaxValue);

        private sealed class DayRetryState
        {
            public readonly DayUpgradeController.UpgradeSnapshot Upgrade;
            public readonly FloodCondition ActiveFloodCondition;
            public readonly FloodCondition PreviousDayFloodCondition;
            public readonly bool DaySupplyPending;
            public readonly bool DaySupplyClaimed;

            public DayRetryState(DayUpgradeController.UpgradeSnapshot upgrade,
                FloodCondition activeFloodCondition, FloodCondition previousDayFloodCondition,
                bool daySupplyPending, bool daySupplyClaimed)
            {
                Upgrade = upgrade;
                ActiveFloodCondition = activeFloodCondition;
                PreviousDayFloodCondition = previousDayFloodCondition;
                DaySupplyPending = daySupplyPending;
                DaySupplyClaimed = daySupplyClaimed;
            }
        }

        private IEnumerator BeginDayNextFrame()
        {
            yield return null;
            BeginDay(true);
        }

        private void OnDisable()
        {
            if (dayInput != null) dayInput.enabled = false;
            if (wardStatus != null) wardStatus.Failed -= OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned -= OnAnomalySpawned;
            if (anomalyRewards != null) anomalyRewards.UnsubscribeAll();
        }
    }

    /// <summary>하루 진행의 외부에서 확인 가능한 상태.</summary>
    public enum DayState { Preparing, Playing, DayClear, DayFailed, Ending }
    public enum DayUpgradeType { Mobility, ContaminationResistance, FieldPurification }

}
