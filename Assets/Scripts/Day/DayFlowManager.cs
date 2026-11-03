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
        [FormerlySerializedAs("maximumRiftReward"), SerializeField, Min(0)] private int maximumAnomalyReward = 1;
        [Header("강화 설정")]
        [SerializeField, Min(0), InspectorName("단계별 마력 조각 가격"),
         Tooltip("1·2·3단계 구매에 각각 지불하는 가격입니다.")] private int[] upgradeStagePrices = { 4, 7, 10 };
        [Header("DAY 스킵")]
        [SerializeField] private bool enableDaySkipCheat = true;
        [Tooltip("기존 씬 설정을 유지하기 위한 필드입니다. F8 DAY 스킵 사용 여부에는 영향을 주지 않습니다.")]
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
        [FormerlySerializedAs("mobilityUpgradeLevel"), SerializeField, Range(0, 3)] private int startingMobilityLevel;
        [FormerlySerializedAs("contaminationResistanceLevel"), SerializeField, Range(0, 3)] private int startingResistanceLevel;
        [FormerlySerializedAs("startingPurificationLevel"), FormerlySerializedAs("fieldPurificationLevel"),
         SerializeField, Range(0, 3)] private int startingFacilityDefenseLevel;

        private DaySession session;
        private DayRetryCheckpoint checkpoint;
        private DayInputController dayInput;
        private DayBriefingController dayBriefing;
        private DayPlayerUpgradeEffects playerUpgradeEffects;
        private DayUpgradeController upgrades;
        private DaySupplyController supply;
        private DayUiController dayUi;
        private DaySceneController dayScene;
        private DayAnomalyRewardController anomalyRewards;
        private bool preparedDayReady;
        private bool briefingPending;
        public bool HasStartedDayBriefingThisRun { get; private set; }
        private DaySession Session => session ?? (session = new DaySession(startingDay));
        private int currentDay => Session.CurrentDay;
        private DayState state => Session.State;
        private float remainingSeconds => Session.RemainingSeconds;
        private int dayRandomSeed => Session.DayRandomSeed;

        private bool CanUseDaySkip
        {
            get => enableDaySkipCheat;
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
            supply.Configure(this, inventory);
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
                startingMobilityLevel, startingFacilityDefenseLevel, startingResistanceLevel);
            dayInput = GetComponent<DayInputController>();
            if (dayInput == null) dayInput = gameObject.AddComponent<DayInputController>();
            PauseMenuController pauseMenu = GetComponent<PauseMenuController>();
            if (pauseMenu == null) pauseMenu = gameObject.AddComponent<PauseMenuController>();
            pauseMenu.Configure(this, wardStatus);
            dayInput.Configure(this, pauseMenu, CanUseDaySkip, daySkipCheatKey);
            dayBriefing = GetComponent<DayBriefingController>();
            if (dayBriefing == null) dayBriefing = gameObject.AddComponent<DayBriefingController>();
            dayBriefing.Configure(this);
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
        }

        private void OnEnable()
        {
            if (dayInput != null) dayInput.enabled = true;
            if (wardStatus != null) wardStatus.Failed += OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned += OnAnomalySpawned;
            if (anomalyRewards != null)
            {
                anomalyRewards.ResolutionRewarded -= OnResolutionRewarded;
                anomalyRewards.ResolutionRewarded += OnResolutionRewarded;
            }
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
            else PrepareDayAndShowBriefing();
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

        /// <summary>DAY CLEAR에서 다음 진행을 요청. 상점이 남아 있으면 상점을 먼저 염.</summary>
        public void BeginNextDay()
        {
            if (state != DayState.DayClear) return;
            // 기존 다음 날 진입 API도 상점 단계로 먼저 연결.
            if (supply != null && supply.Pending)
            {
                supply.Open(InventoryOpen, IsMapOpen);
                return;
            }
            AdvanceToNextDay();
        }

        private void AdvanceToNextDay()
        {
            if (state != DayState.DayClear || supply != null && supply.Pending) return;
            session.MoveToNextDay(FinalDay, CreateDaySeed);
            PrepareDayAndShowBriefing();
        }

        /// <summary>디버그용 DAY 스킵. 진행 중 현상은 보상 없이 정리하고 다음 DAY를 시작.</summary>
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

            if (state == DayState.DayClear)
            {
                if (supply != null && supply.Pending)
                {
                    if (!supply.PanelOpen) supply.Open(false, false);
                    CompleteSupplyShopAndBeginNextDay();
                }
                else BeginNextDay();
            }
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

        /// <summary>기존 UI/호출부 호환용 이름. 동작은 현재 DAY 재도전.</summary>
        public void RestartFromDayOne() => RetryCurrentDay();

        private void PrepareDayAndShowBriefing(bool isRetry = false)
        {
            if (state != DayState.Preparing) return;
            
            preparedDayReady = false;
            briefingPending = false;
            dayUi.CloseInventory();
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
            inventory.BeginDay();
            
            if (wardStatus != null) wardStatus.ResetForNewDay();
            if (!isRetry) upgrades.ResetPurchaseForNewPhase();
            
            supply.BeginDay(currentDay, isRetry);
            ApplyUpgradeEffects();
            
            if (magicCaster != null)
            {
                magicCaster.ResetForNewDay();
                magicCaster.SetInputBlocked(true);
            }
            
            if (anomalySpawner != null)
            {
                anomalySpawner.ConfigureForDay(currentDay, dayRandomSeed);
            }
            
            checkpoint.Capture(inventory);
            
            if (wardStatus != null) wardStatus.PauseGameForUI();

            preparedDayReady = true;
            
            if (!isRetry && dayBriefing != null && dayBriefing.Show(currentDay))
            {
                briefingPending = true;
                return;
            }
            
            BeginPreparedDay();
        }

        /// <summary>운영 기록의 시작 버튼에서만 준비된 DAY를 실제 근무 상태로 전환.</summary>
        internal void StartPreparedDayFromBriefing()
        {
            if (!briefingPending || !preparedDayReady || state != DayState.Preparing) return;
            briefingPending = false;
            HasStartedDayBriefingThisRun = true;
            BeginPreparedDay();
        }

        private void BeginPreparedDay()
        {
            if (!preparedDayReady || state != DayState.Preparing) return;
            preparedDayReady = false;
            session.BeginDay(dayDurationSeconds);
            AudioService.PlayLoop(WardShiftSoundId.FacilityAmbient);
            AudioService.Play2D(WardShiftSoundId.UiDayStart);
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (magicCaster != null) magicCaster.SetInputBlocked(false);
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawnWindowOpen(remainingSeconds > Mathf.Max(0f, spawnStopBeforeEndSeconds));
                anomalySpawner.SetSpawningEnabled(true);
            }

        }

        private void CompleteDay()
        {
            if (state != DayState.Playing) return;
            session.CompleteDay(FinalDay);
            AudioService.StopLoop(WardShiftSoundId.FacilityAmbient);
            if (state == DayState.Ending)
            {
                AudioService.Play2D(WardShiftSoundId.UiEndingResult);
                AudioService.PlayLoop(WardShiftSoundId.EndingMusic);
            }
            else AudioService.Play2D(WardShiftSoundId.UiDayClear);
            ApplyUpgradeEffects();
            PrepareNextDaySeed();
            upgrades.ResetPurchaseForNewPhase();
            supply.PrepareForDayClear(currentDay < FinalDay);
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
            AudioService.StopLoop(WardShiftSoundId.FacilityAmbient);
            AudioService.Play2D(WardShiftSoundId.UiGameOver);
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

        private void OnResolutionRewarded(AnomalyBehaviour anomaly, int reward)
        {
            RaiseAnomalyResolutionRewarded(anomaly, reward);
        }

        private bool HasSurvivedDay()
        {
            return wardStatus != null && !wardStatus.HasFailed && wardStatus.Stability > 0f
                && wardStatus.Player != null && wardStatus.Player.Value < 100f;
        }

        public object CaptureDayRetryState()
        {
            return new DayRetryState(upgrades.CaptureState(), supply.Pending, supply.PurchasesThisShop);
        }

        public void RestoreDayRetryState(object stateSnapshot)
        {
            if (!(stateSnapshot is DayRetryState snapshot)) return;
            upgrades.RestoreState(snapshot.Upgrade);
            supply.RestoreState(snapshot.DaySupplyPending, snapshot.SupplyPurchases);
            ApplyUpgradeEffects();
        }

        private void ApplyUpgradeEffects()
        {
            if (upgrades != null) upgrades.ApplyEffects(Player, wardStatus);
        }

        private void PrepareNextDaySeed()
        {
            session.ClearNextDayPlan();
            if (currentDay >= FinalDay) return;

            session.PlanNextDay(currentDay + 1, CreateDaySeed());
        }

        private void EnsureUpgradePrices()
        {
            if (upgradeStagePrices == null || upgradeStagePrices.Length != DayUpgradeRules.MaximumLevel)
                upgradeStagePrices = new[] { 4, 7, 10 };
        }

        private void OnValidate()
        {
            EnsureUpgradePrices();
            startingMobilityLevel = DayUpgradeRules.ClampLevel(startingMobilityLevel);
            startingResistanceLevel = DayUpgradeRules.ClampLevel(startingResistanceLevel);
            startingFacilityDefenseLevel = DayUpgradeRules.ClampLevel(startingFacilityDefenseLevel);
        }

        private static int CreateDaySeed() => UnityEngine.Random.Range(1, int.MaxValue);

        private sealed class DayRetryState
        {
            public readonly DayUpgradeController.UpgradeSnapshot Upgrade;
            public readonly bool DaySupplyPending;
            public readonly int SupplyPurchases;

            public DayRetryState(DayUpgradeController.UpgradeSnapshot upgrade,
                bool daySupplyPending, int supplyPurchases)
            {
                Upgrade = upgrade;
                DaySupplyPending = daySupplyPending;
                SupplyPurchases = supplyPurchases;
            }
        }

        private IEnumerator BeginDayNextFrame()
        {
            yield return null;
            PrepareDayAndShowBriefing(true);
        }

        private void OnDisable()
        {
            if (dayInput != null) dayInput.enabled = false;
            if (wardStatus != null) wardStatus.Failed -= OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned -= OnAnomalySpawned;
            if (anomalyRewards != null) anomalyRewards.ResolutionRewarded -= OnResolutionRewarded;
            if (anomalyRewards != null) anomalyRewards.UnsubscribeAll();
        }
    }

    /// <summary>하루 진행의 외부에서 확인 가능한 상태.</summary>
    public enum DayState { Preparing, Playing, DayClear, DayFailed, Ending }
    public enum DayUpgradeType { Mobility, FacilityDefense, ContaminationResistance }

}
