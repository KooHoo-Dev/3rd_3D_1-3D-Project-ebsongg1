using System;
using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace WardShift
{
    /// <summary>
    /// DAY 재도전 체크포인트에 상태를 추가할 수 있는 확장 지점.
    /// 강화 시스템은 이 인터페이스를 구현해 DAY 시작 상태를 저장·복원할 수 있음.
    /// </summary>
    public interface IDayRetryCheckpointParticipant
    {
        object CaptureDayRetryState();
        void RestoreDayRetryState(object state);
    }

    /// <summary>DAY 시작, 근무 시간, 진압 보상, CLEAR/FAILED와 다음 DAY 전환을 관리.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10006)]
    public sealed class DayFlowManager : MonoBehaviour, IDayRetryCheckpointParticipant
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

        [Header("실행 상태 (읽기 전용)")]
        [SerializeField, Min(1)] private int currentDay = 1;
        [SerializeField] private DayState state = DayState.Preparing;
        [SerializeField] private float remainingSeconds;
        [SerializeField, Range(0, 2)] private int mobilityUpgradeLevel;
        [SerializeField, Range(0, 2)] private int contaminationResistanceLevel;
        [SerializeField, Range(0, 2)] private int fieldPurificationLevel;
        [SerializeField] private FloodCondition activeFloodCondition;
        [SerializeField] private FloodCondition nextDayFloodCondition;

        private readonly InputAction inventoryAction = new InputAction(
            "Toggle Inventory", InputActionType.Button, "<Keyboard>/b");
        private readonly InputAction mapAction = new InputAction(
            "Toggle Facility Map", InputActionType.Button, "<Keyboard>/m");
        private readonly InputAction supplyAction = new InputAction(
            "Open Day Supply", InputActionType.Button, "<Keyboard>/t");
        private InputAction daySkipAction;
        private readonly HashSet<int> rewardedAnomalyIds = new HashSet<int>();
        private readonly List<AnomalyBehaviour> subscribedAnomalies = new List<AnomalyBehaviour>();
        private readonly List<PickupCheckpoint> pickupCheckpoint = new List<PickupCheckpoint>();
        private readonly List<ParticipantCheckpoint> participantCheckpoint = new List<ParticipantCheckpoint>();
        private PlayerInventory.DayStateSnapshot inventoryCheckpoint;
        private int dayRandomSeed;
        private int plannedNextDayNumber;
        private int plannedNextDayRandomSeed;
        private FloodCondition previousDayFloodCondition;
#if UNITY_EDITOR
        private bool useFloodConditionTestOverride;
        private FloodCondition floodConditionTestOverride;
#endif
        private bool inventoryOpen;
        private bool mapOpen;
        private bool daySupplyPending;
        private bool daySupplyClaimed;
        private bool supplyPanelOpen;
        private ItemType supplyItemAwaitingExchange;
        private bool upgradePurchasedThisClear;
        private FirstPersonController firstPersonController;
        private ThirdPersonController thirdPersonController;
        private float firstPersonBaseMoveSpeed;
        private float firstPersonBaseSprintSpeed;
        private float thirdPersonBaseMoveSpeed;
        private float thirdPersonBaseSprintSpeed;
        private bool firstPersonSpeedCaptured;
        private bool thirdPersonSpeedCaptured;

        public int CurrentDay => currentDay;
        public DayState State => state;
        public float RemainingSeconds => remainingSeconds;
        public float DayDurationSeconds => dayDurationSeconds;
        public float SpawnStopBeforeEndSeconds => spawnStopBeforeEndSeconds;
        public int FinalDay => Mathf.Max(1, finalDay);
        public int DayRandomSeed => dayRandomSeed;
        public bool InventoryOpen => inventoryOpen;
        public bool IsMapOpen => mapOpen;
        public bool DaySupplyPending => daySupplyPending;
        public bool SupplyPanelOpen => supplyPanelOpen;
        public ItemType SupplyItemAwaitingExchange => supplyItemAwaitingExchange;
        public bool UpgradePurchasedThisClear => upgradePurchasedThisClear;
        public int MobilityUpgradeLevel => mobilityUpgradeLevel;
        public int ContaminationResistanceLevel => contaminationResistanceLevel;
        public int FieldPurificationLevel => fieldPurificationLevel;
        public FloodCondition ActiveFloodCondition => activeFloodCondition;
        public FloodCondition NextDayFloodCondition => nextDayFloodCondition;
        public float SafeZoneRecoveryMultiplier => FloodConditionRules.SafeZoneRecoveryMultiplier(activeFloodCondition);
        public float PotionRecoveryAmount => FloodConditionRules.PotionRecoveryAmount(
            PotionRecoveryForLevel(fieldPurificationLevel), activeFloodCondition);
        public FacilityZoneTracker ZoneTracker => zoneTracker;
        public PlayerInventory Inventory => inventory;
        public WardStatus Status => wardStatus;
        public PlayerContamination Player => wardStatus != null ? wardStatus.Player : null;
        public PlayerPickupInteractor PickupInteractor => pickupInteractor;
        public event Action<bool> MapVisibilityChanged;
        public bool IsConfigured => wardStatus != null && anomalySpawner != null && inventory != null
            && magicCaster != null && playerRoot != null && startPoint != null && pickupInteractor != null;

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

        private void Awake()
        {
            if (wardStatus == null) wardStatus = FindFirstObjectByType<WardStatus>();
            if (anomalySpawner == null) anomalySpawner = FindFirstObjectByType<AnomalySpawner>();
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (magicCaster == null) magicCaster = FindFirstObjectByType<MagicCaster>();
            if (playerRoot == null && wardStatus != null && wardStatus.Player != null)
                playerRoot = wardStatus.Player.transform;
            if (pickupInteractor == null) pickupInteractor = FindFirstObjectByType<PlayerPickupInteractor>();
            if (zoneTracker == null) zoneTracker = FindFirstObjectByType<FacilityZoneTracker>();
            CapturePlayerMovementBaseSpeeds();
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            if (CanUseDaySkip && daySkipCheatKey != Key.None)
            {
                string keyPath = daySkipCheatKey.ToString().ToLowerInvariant();
                daySkipAction = new InputAction("Cheat: Advance Day", InputActionType.Button,
                    $"<Keyboard>/{keyPath}");
            }
        }

        private void OnEnable()
        {
            inventoryAction.Enable();
            mapAction.Enable();
            supplyAction.Enable();
            if (daySkipAction != null) daySkipAction.Enable();
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
            currentDay = Mathf.Max(1, currentDay);
            if (wardStatus.HasFailed) OnDayFailed(wardStatus.FailureReason);
            else BeginDay();
        }

        private void LateUpdate()
        {
            if (CanUseDaySkip && daySkipAction != null && daySkipAction.WasPressedThisFrame())
            {
                CheatAdvanceDay();
                return;
            }
            if (supplyAction.WasPressedThisFrame() && daySupplyPending && !supplyPanelOpen)
            {
                OpenDaySupplyChoice();
                return;
            }
            if (mapAction.WasPressedThisFrame())
            {
                ToggleMap();
                return;
            }
            if (inventoryAction.WasPressedThisFrame()) ToggleInventory();
            if (state != DayState.Playing || Time.timeScale <= 0f) return;
            if (wardStatus != null && wardStatus.HasFailed)
            {
                OnDayFailed(wardStatus.FailureReason);
                return;
            }

            if (remainingSeconds > 0f)
            {
                remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.deltaTime);
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
                ClearAllAnomalies();
                if (HasSurvivedDay()) CompleteDay();
                else if (wardStatus == null || !wardStatus.HasFailed)
                    OnDayFailed("근무 종료 시 생존 조건을 만족하지 못했습니다.");
            }
        }

        /// <summary>DAY CLEAR 화면의 다음 날 버튼이 호출. 중복 클릭은 상태 검사로 무시.</summary>
        public void BeginNextDay()
        {
            if (state != DayState.DayClear) return;
            currentDay = Mathf.Min(currentDay + 1, FinalDay);
            dayRandomSeed = plannedNextDayNumber == currentDay
                ? plannedNextDayRandomSeed : CreateDaySeed();
            plannedNextDayNumber = 0;
            plannedNextDayRandomSeed = 0;
            nextDayFloodCondition = FloodCondition.None;
            state = DayState.Preparing;
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
                ClearAllAnomalies();
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
            UnsubscribeFromAnomalies();
            ClearAllAnomalies();
            state = DayState.Preparing;
            SetMapOpen(false, false);
            RestoreDayCheckpoint();
            if (pickupInteractor != null) pickupInteractor.ClearCurrentPickup();
            StartCoroutine(BeginDayNextFrame());
        }

        /// <summary>기존 UI/호출부 호환용 이름. 동작은 현재 DAY 재도전입니다.</summary>
        public void RestartFromDayOne() => RetryCurrentDay();

        /// <summary>인벤토리 창을 열거나 닫음.</summary>
        public void ToggleInventory()
        {
            if (state != DayState.Playing || mapOpen || supplyPanelOpen) return;
            inventoryOpen = !inventoryOpen;
            if (inventoryOpen) wardStatus.PauseGameForUI();
            else wardStatus.ResumeGameFromUI();
        }

        /// <summary>지도 열기 입력. 인벤토리와 DAY 종료 화면이 열려 있을 때는 무시합니다.</summary>
        public void ToggleMap()
        {
            if (state != DayState.Playing || inventoryOpen || supplyPanelOpen) return;
            SetMapOpen(!mapOpen, true);
        }

        /// <summary>셋업 도구가 현재 씬의 기존 컴포넌트를 연결.</summary>
        public void Configure(WardStatus status, AnomalySpawner spawner, PlayerInventory playerInventory,
            MagicCaster caster, Transform player, Transform spawn, PlayerPickupInteractor pickup)
        {
            wardStatus = status;
            anomalySpawner = spawner;
            inventory = playerInventory;
            magicCaster = caster;
            playerRoot = player;
            startPoint = spawn;
            pickupInteractor = pickup;
        }

        public void ConfigureZoneTracker(FacilityZoneTracker tracker)
        {
            zoneTracker = tracker;
        }

        public int GetUpgradeLevel(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return mobilityUpgradeLevel;
                case DayUpgradeType.ContaminationResistance: return contaminationResistanceLevel;
                case DayUpgradeType.FieldPurification: return fieldPurificationLevel;
                default: return 0;
            }
        }

        public int GetNextUpgradePrice(DayUpgradeType type)
        {
            if (GetUpgradeLevel(type) >= 2) return 0;
            EnsureUpgradePrices();
            return Mathf.Max(0, upgradeStagePrices[GetUpgradeLevel(type)]);
        }

        public string GetUpgradeEffectText(DayUpgradeType type, int level)
        {
            level = Mathf.Clamp(level, 0, 2);
            switch (type)
            {
                case DayUpgradeType.Mobility:
                    return level == 0 ? "기본 이동속도" : $"이동속도 +{level * 10}%";
                case DayUpgradeType.ContaminationResistance:
                    return level == 0 ? "기본 오염 획득량" : $"오염도 획득량 -{(level == 1 ? 20 : 35)}%";
                case DayUpgradeType.FieldPurification:
                {
                    FloodCondition condition = state == DayState.DayClear
                        ? nextDayFloodCondition : activeFloodCondition;
                    float amount = FloodConditionRules.PotionRecoveryAmount(
                        PotionRecoveryForLevel(level), condition);
                    return $"정화약 {amount:F0} 회복";
                }
                default:
                    return string.Empty;
            }
        }

        public bool CanPurchaseUpgrade(DayUpgradeType type)
        {
            return state == DayState.DayClear && !upgradePurchasedThisClear
                && GetUpgradeLevel(type) < 2 && inventory != null
                && inventory.ManaCrystals >= GetNextUpgradePrice(type);
        }

        public bool TryPurchaseUpgrade(DayUpgradeType type)
        {
            if (!CanPurchaseUpgrade(type)) return false;
            int price = GetNextUpgradePrice(type);
            if (!inventory.TrySpendManaCrystals(price)) return false;

            SetUpgradeLevel(type, GetUpgradeLevel(type) + 1);
            upgradePurchasedThisClear = true;
            ApplyUpgradeEffects();
            return true;
        }

        public void OpenDaySupplyChoice()
        {
            if (state != DayState.Playing || !daySupplyPending || inventoryOpen || mapOpen) return;
            supplyPanelOpen = true;
            if (wardStatus != null) wardStatus.PauseGameForUI();
        }

        /// <summary>보급 선택을 미루고 진행합니다. DAY 동안 T 키로 다시 열 수 있습니다.</summary>
        public void DeferDaySupplyChoice()
        {
            if (!supplyPanelOpen) return;
            supplyItemAwaitingExchange = ItemType.None;
            supplyPanelOpen = false;
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
        }

        /// <summary>보급을 인벤토리와 DAY 시작 재도전 스냅샷에 함께 지급합니다.</summary>
        public bool TryChooseDaySupply(ItemType item, out bool needsExchange)
        {
            needsExchange = false;
            if (state != DayState.Playing || !supplyPanelOpen || !daySupplyPending
                || (item != ItemType.PurificationPotion && item != ItemType.EmergencyRepairKit)
                || inventory == null || inventoryCheckpoint == null) return false;

            if (!inventory.TryAddItemToDayState(inventoryCheckpoint, item, 1,
                    out PlayerInventory.DayStateSnapshot updatedSnapshot)
                || !inventory.CanAddItem(item))
            {
                supplyItemAwaitingExchange = item;
                needsExchange = true;
                return false;
            }

            if (!inventory.TryAddItem(item))
            {
                supplyItemAwaitingExchange = item;
                needsExchange = true;
                return false;
            }
            inventoryCheckpoint = updatedSnapshot;
            CompleteDaySupplyClaim();
            return true;
        }

        /// <summary>가득 찬 인벤토리에서 고른 슬롯을 보급품으로 교환합니다.</summary>
        public bool TryExchangeDaySupply(int slotIndex)
        {
            if (state != DayState.Playing || !supplyPanelOpen || !daySupplyPending
                || supplyItemAwaitingExchange == ItemType.None || inventory == null
                || inventoryCheckpoint == null) return false;

            if (!inventory.TryReplaceItemInDayState(inventoryCheckpoint, slotIndex,
                    supplyItemAwaitingExchange, out PlayerInventory.DayStateSnapshot updatedSnapshot)
                || !inventory.TryReplaceItem(slotIndex, supplyItemAwaitingExchange)) return false;
            inventoryCheckpoint = updatedSnapshot;
            CompleteDaySupplyClaim();
            return true;
        }

        public string GetSupplySnapshotSlotLabel(int slotIndex)
        {
            ItemType item = inventory != null ? inventory.GetDayStateItem(inventoryCheckpoint, slotIndex)
                : ItemType.None;
            int quantity = inventory != null ? inventory.GetDayStateQuantity(inventoryCheckpoint, slotIndex) : 0;
            return item == ItemType.None ? "빈 슬롯" : $"{GetItemDisplayName(item)} x{quantity}";
        }

        /// <summary>테스트 씬에서 근무 시간과 결정 보상 범위를 지정.</summary>
        public void ConfigureDaySettings(float durationSeconds, int minReward, int maxReward)
        {
            dayDurationSeconds = Mathf.Max(1f, durationSeconds);
            minimumAnomalyReward = Mathf.Max(0, minReward);
            maximumAnomalyReward = Mathf.Max(minimumAnomalyReward, maxReward);
        }

#if UNITY_EDITOR
        /// <summary>에디터 전용 검사에서 이후 DAY 조건을 고정합니다. 일반 플레이 경로에는 저장되지 않습니다.</summary>
        public void ConfigureFloodConditionTestOverride(bool enabled, FloodCondition condition)
        {
            useFloodConditionTestOverride = enabled;
            floodConditionTestOverride = condition;
            if (state == DayState.DayClear) PrepareNextDayCondition();
        }
#endif

        private void BeginDay(bool isRetry = false)
        {
            inventoryOpen = false;
            supplyPanelOpen = false;
            supplyItemAwaitingExchange = ItemType.None;
            SetMapOpen(false, false);
            rewardedAnomalyIds.Clear();
            UnsubscribeFromAnomalies();
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawningEnabled(false);
                anomalySpawner.SetSpawnWindowOpen(false);
            }
            ClearAllAnomalies();
            ResetSceneManaDevicesForNewDay();
            TeleportPlayerToStart();
            if (dayRandomSeed == 0) dayRandomSeed = CreateDaySeed();
            if (!isRetry)
                activeFloodCondition = SelectFloodCondition(currentDay, dayRandomSeed, previousDayFloodCondition);
            inventory.BeginDay();
            if (wardStatus != null) wardStatus.ResetForNewDay();
            if (!isRetry)
            {
                upgradePurchasedThisClear = false;
                daySupplyClaimed = false;
                daySupplyPending = currentDay > 1;
                if (currentDay == 1)
                {
                    daySupplyClaimed = inventory.TryAddItem(ItemType.PurificationPotion);
                    daySupplyPending = !daySupplyClaimed;
                }
            }
            ApplyUpgradeEffects();
            remainingSeconds = Mathf.Max(1f, dayDurationSeconds);
            state = DayState.Playing;
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
                CaptureDayCheckpoint();
                anomalySpawner.SetSpawningEnabled(true);
            }
            else CaptureDayCheckpoint();

            if (daySupplyPending) OpenDaySupplyChoice();
        }

        private void CompleteDay()
        {
            if (state != DayState.Playing) return;
            state = currentDay >= FinalDay ? DayState.Ending : DayState.DayClear;
            previousDayFloodCondition = activeFloodCondition;
            activeFloodCondition = FloodCondition.None;
            ApplyUpgradeEffects();
            PrepareNextDayCondition();
            upgradePurchasedThisClear = false;
            daySupplyPending = false;
            supplyPanelOpen = false;
            supplyItemAwaitingExchange = ItemType.None;
            SetMapOpen(false, false);
            remainingSeconds = 0f;
            inventoryOpen = false;
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
            state = DayState.DayFailed;
            SetMapOpen(false, false);
            inventoryOpen = false;
            supplyPanelOpen = false;
            supplyItemAwaitingExchange = ItemType.None;
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            if (anomalySpawner != null) anomalySpawner.SetSpawnWindowOpen(false);
            if (magicCaster != null) magicCaster.SetInputBlocked(true);
            if (wardStatus != null) wardStatus.PauseGameForUI();
        }

        private void SetMapOpen(bool open, bool managePause)
        {
            if (mapOpen == open) return;
            mapOpen = open;
            if (managePause && wardStatus != null)
            {
                if (open) wardStatus.PauseGameForUI();
                else wardStatus.ResumeGameFromUI();
            }
            MapVisibilityChanged?.Invoke(mapOpen);
        }

        private void OnAnomalySpawned(AnomalyBehaviour anomaly)
        {
            if (anomaly == null || subscribedAnomalies.Contains(anomaly)) return;
            anomaly.ResolutionCompleted += OnAnomalyResolved;
            anomaly.Ended += OnAnomalyEnded;
            subscribedAnomalies.Add(anomaly);
        }

        private void OnAnomalyEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            if (anomaly == null) return;
            anomaly.ResolutionCompleted -= OnAnomalyResolved;
            anomaly.Ended -= OnAnomalyEnded;
            subscribedAnomalies.Remove(anomaly);
        }

        private void OnAnomalyResolved(AnomalyBehaviour anomaly)
        {
            if (anomaly == null || state != DayState.Playing) return;
            int id = anomaly.GetInstanceID();
            if (!rewardedAnomalyIds.Add(id)) return;
            int reward = anomaly.ResolutionReward;
            if (reward < 0)
            {
                int min = Mathf.Max(0, minimumAnomalyReward);
                int max = Mathf.Max(min, maximumAnomalyReward);
                reward = UnityEngine.Random.Range(min, max + 1);
            }
            else reward = FloodConditionRules.ResolutionReward(reward, activeFloodCondition);
            inventory.AddManaCrystals(Mathf.Max(0, reward));
            anomaly.ResolutionCompleted -= OnAnomalyResolved;
        }

        private bool HasSurvivedDay()
        {
            return wardStatus != null && !wardStatus.HasFailed && wardStatus.Stability > 0f
                && wardStatus.Player != null && wardStatus.Player.Value < 100f;
        }

        private void CaptureDayCheckpoint()
        {
            if (inventory == null) return;
            inventoryCheckpoint = inventory.CaptureDayState();
            pickupCheckpoint.Clear();
            foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (pickup == null || pickup.gameObject.scene != gameObject.scene) continue;
                pickupCheckpoint.Add(new PickupCheckpoint(pickup, pickup.IsCollected,
                    pickup.gameObject.activeSelf));
            }

            participantCheckpoint.Clear();
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour.gameObject.scene != gameObject.scene
                    || !(behaviour is IDayRetryCheckpointParticipant participant)) continue;
                participantCheckpoint.Add(new ParticipantCheckpoint(participant, participant.CaptureDayRetryState()));
            }
        }

        private void RestoreDayCheckpoint()
        {
            if (inventory != null && inventoryCheckpoint != null)
                inventory.RestoreDayState(inventoryCheckpoint);

            ItemPickup[] currentPickups = FindObjectsByType<ItemPickup>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ItemPickup pickup in currentPickups)
            {
                if (pickup == null || pickup.gameObject.scene != gameObject.scene) continue;
                PickupCheckpoint snapshot = FindPickupCheckpoint(pickup);
                if (snapshot != null) pickup.RestoreDayState(snapshot.IsCollected, snapshot.ActiveSelf);
                else
                {
                    pickup.gameObject.SetActive(false);
                    Destroy(pickup.gameObject);
                }
            }

            foreach (ParticipantCheckpoint snapshot in participantCheckpoint)
                if (snapshot.Participant is UnityEngine.Object participantObject && participantObject != null)
                    snapshot.Participant.RestoreDayRetryState(snapshot.State);
        }

        public object CaptureDayRetryState()
        {
            return new DayRetryState(mobilityUpgradeLevel, contaminationResistanceLevel,
                fieldPurificationLevel, activeFloodCondition, previousDayFloodCondition,
                upgradePurchasedThisClear, daySupplyPending, daySupplyClaimed);
        }

        public void RestoreDayRetryState(object stateSnapshot)
        {
            if (!(stateSnapshot is DayRetryState snapshot)) return;
            mobilityUpgradeLevel = Mathf.Clamp(snapshot.MobilityUpgradeLevel, 0, 2);
            contaminationResistanceLevel = Mathf.Clamp(snapshot.ContaminationResistanceLevel, 0, 2);
            fieldPurificationLevel = Mathf.Clamp(snapshot.FieldPurificationLevel, 0, 2);
            activeFloodCondition = snapshot.ActiveFloodCondition;
            previousDayFloodCondition = snapshot.PreviousDayFloodCondition;
            upgradePurchasedThisClear = snapshot.UpgradePurchasedThisClear;
            daySupplyPending = snapshot.DaySupplyPending;
            daySupplyClaimed = snapshot.DaySupplyClaimed;
            supplyPanelOpen = false;
            supplyItemAwaitingExchange = ItemType.None;
            ApplyUpgradeEffects();
        }

        private void CompleteDaySupplyClaim()
        {
            daySupplyPending = false;
            daySupplyClaimed = true;
            supplyPanelOpen = false;
            supplyItemAwaitingExchange = ItemType.None;
            RefreshSelfRetryCheckpoint();
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
        }

        private void RefreshSelfRetryCheckpoint()
        {
            for (int i = 0; i < participantCheckpoint.Count; i++)
            {
                if (!ReferenceEquals(participantCheckpoint[i].Participant, this)) continue;
                participantCheckpoint[i].State = CaptureDayRetryState();
                return;
            }
        }

        private void SetUpgradeLevel(DayUpgradeType type, int level)
        {
            level = Mathf.Clamp(level, 0, 2);
            switch (type)
            {
                case DayUpgradeType.Mobility: mobilityUpgradeLevel = level; break;
                case DayUpgradeType.ContaminationResistance: contaminationResistanceLevel = level; break;
                case DayUpgradeType.FieldPurification: fieldPurificationLevel = level; break;
            }
        }

        private void ApplyUpgradeEffects()
        {
            ApplyMovementUpgrade();
            if (Player != null)
            {
                float reduction = contaminationResistanceLevel == 2 ? 0.35f
                    : contaminationResistanceLevel == 1 ? 0.2f : 0f;
                Player.SetContaminationGainMultiplier(1f - reduction);
                Player.SetSafeZoneRecoveryMultiplier(SafeZoneRecoveryMultiplier);
            }
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
            nextDayFloodCondition = FloodCondition.None;
            plannedNextDayNumber = 0;
            plannedNextDayRandomSeed = 0;
            if (currentDay >= FinalDay) return;

            plannedNextDayNumber = currentDay + 1;
            plannedNextDayRandomSeed = CreateDaySeed();
            nextDayFloodCondition = SelectFloodCondition(plannedNextDayNumber,
                plannedNextDayRandomSeed, previousDayFloodCondition);
        }

        private void CapturePlayerMovementBaseSpeeds()
        {
            if (playerRoot == null) return;
            if (firstPersonController == null)
                firstPersonController = playerRoot.GetComponentInChildren<FirstPersonController>(true);
            if (thirdPersonController == null)
                thirdPersonController = playerRoot.GetComponentInChildren<ThirdPersonController>(true);
            if (firstPersonController != null && !firstPersonSpeedCaptured)
            {
                firstPersonBaseMoveSpeed = firstPersonController.MoveSpeed;
                firstPersonBaseSprintSpeed = firstPersonController.SprintSpeed;
                firstPersonSpeedCaptured = true;
            }
            if (thirdPersonController != null && !thirdPersonSpeedCaptured)
            {
                thirdPersonBaseMoveSpeed = thirdPersonController.MoveSpeed;
                thirdPersonBaseSprintSpeed = thirdPersonController.SprintSpeed;
                thirdPersonSpeedCaptured = true;
            }
        }

        private void ApplyMovementUpgrade()
        {
            CapturePlayerMovementBaseSpeeds();
            float multiplier = 1f + (mobilityUpgradeLevel == 2 ? 0.2f
                : mobilityUpgradeLevel == 1 ? 0.1f : 0f);
            if (firstPersonController != null && firstPersonSpeedCaptured)
            {
                firstPersonController.MoveSpeed = firstPersonBaseMoveSpeed * multiplier;
                firstPersonController.SprintSpeed = firstPersonBaseSprintSpeed * multiplier;
            }
            if (thirdPersonController != null && thirdPersonSpeedCaptured)
            {
                thirdPersonController.MoveSpeed = thirdPersonBaseMoveSpeed * multiplier;
                thirdPersonController.SprintSpeed = thirdPersonBaseSprintSpeed * multiplier;
            }
        }

        private void EnsureUpgradePrices()
        {
            if (upgradeStagePrices == null || upgradeStagePrices.Length != 2)
                upgradeStagePrices = new[] { 4, 8 };
        }

        private static float PotionRecoveryForLevel(int level)
        {
            return level >= 2 ? 60f : level == 1 ? 45f : PlayerInventory.PotionRecovery;
        }

        private static string GetItemDisplayName(ItemType item)
        {
            switch (item)
            {
                case ItemType.PurificationPotion: return "정화약";
                case ItemType.EmergencyRepairKit: return "비상 수리 키트";
                default: return "빈 슬롯";
            }
        }

        private void OnValidate()
        {
            EnsureUpgradePrices();
            mobilityUpgradeLevel = Mathf.Clamp(mobilityUpgradeLevel, 0, 2);
            contaminationResistanceLevel = Mathf.Clamp(contaminationResistanceLevel, 0, 2);
            fieldPurificationLevel = Mathf.Clamp(fieldPurificationLevel, 0, 2);
        }

        private PickupCheckpoint FindPickupCheckpoint(ItemPickup pickup)
        {
            foreach (PickupCheckpoint snapshot in pickupCheckpoint)
                if (snapshot.Pickup == pickup) return snapshot;
            return null;
        }

        private static int CreateDaySeed() => UnityEngine.Random.Range(1, int.MaxValue);

        private sealed class PickupCheckpoint
        {
            public readonly ItemPickup Pickup;
            public readonly bool IsCollected;
            public readonly bool ActiveSelf;
            public PickupCheckpoint(ItemPickup pickup, bool isCollected, bool activeSelf)
            {
                Pickup = pickup;
                IsCollected = isCollected;
                ActiveSelf = activeSelf;
            }
        }

        private sealed class ParticipantCheckpoint
        {
            public readonly IDayRetryCheckpointParticipant Participant;
            public object State;
            public ParticipantCheckpoint(IDayRetryCheckpointParticipant participant, object state)
            {
                Participant = participant;
                State = state;
            }
        }

        private sealed class DayRetryState
        {
            public readonly int MobilityUpgradeLevel;
            public readonly int ContaminationResistanceLevel;
            public readonly int FieldPurificationLevel;
            public readonly FloodCondition ActiveFloodCondition;
            public readonly FloodCondition PreviousDayFloodCondition;
            public readonly bool UpgradePurchasedThisClear;
            public readonly bool DaySupplyPending;
            public readonly bool DaySupplyClaimed;

            public DayRetryState(int mobilityUpgradeLevel, int contaminationResistanceLevel,
                int fieldPurificationLevel, FloodCondition activeFloodCondition,
                FloodCondition previousDayFloodCondition, bool upgradePurchasedThisClear,
                bool daySupplyPending, bool daySupplyClaimed)
            {
                MobilityUpgradeLevel = mobilityUpgradeLevel;
                ContaminationResistanceLevel = contaminationResistanceLevel;
                FieldPurificationLevel = fieldPurificationLevel;
                ActiveFloodCondition = activeFloodCondition;
                PreviousDayFloodCondition = previousDayFloodCondition;
                UpgradePurchasedThisClear = upgradePurchasedThisClear;
                DaySupplyPending = daySupplyPending;
                DaySupplyClaimed = daySupplyClaimed;
            }
        }

        private void ClearAllAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in FindObjectsByType<AnomalyBehaviour>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (anomaly != null) Destroy(anomaly.gameObject);
            if (anomalySpawner != null) anomalySpawner.ClearActiveAnomalies();
        }

        private static void ResetSceneManaDevicesForNewDay()
        {
            ManaDeviceVisuals[] devices = FindObjectsByType<ManaDeviceVisuals>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ManaDeviceVisuals device in devices)
                if (device != null) device.ResetForNewDay();
        }

        private void TeleportPlayerToStart()
        {
            if (playerRoot == null || startPoint == null) return;
            CharacterController controller = playerRoot.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            playerRoot.SetPositionAndRotation(startPoint.position, startPoint.rotation);
            if (wasEnabled) controller.enabled = true;
        }

        private IEnumerator BeginDayNextFrame()
        {
            yield return null;
            BeginDay(true);
        }

        private void UnsubscribeFromAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in subscribedAnomalies)
            {
                if (anomaly == null) continue;
                anomaly.ResolutionCompleted -= OnAnomalyResolved;
                anomaly.Ended -= OnAnomalyEnded;
            }
            subscribedAnomalies.Clear();
        }

        private void OnDisable()
        {
            inventoryAction.Disable();
            mapAction.Disable();
            supplyAction.Disable();
            if (daySkipAction != null) daySkipAction.Disable();
            if (wardStatus != null) wardStatus.Failed -= OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned -= OnAnomalySpawned;
            UnsubscribeFromAnomalies();
        }

        private void OnDestroy()
        {
            inventoryAction.Dispose();
            mapAction.Dispose();
            supplyAction.Dispose();
            if (daySkipAction != null) daySkipAction.Dispose();
        }
    }

    /// <summary>하루 진행의 외부에서 확인 가능한 상태.</summary>
    public enum DayState { Preparing, Playing, DayClear, DayFailed, Ending }
    public enum DayUpgradeType { Mobility, ContaminationResistance, FieldPurification }

#if UNITY_EDITOR
    /// <summary>에디터 Play Mode 검사에서 진압 이벤트와 보상 중복 방지를 확인하는 임시 이상현상.</summary>
    [AddComponentMenu("")]
    public sealed class FloodRewardProbeAnomaly : AnomalyBehaviour
    {
        private int stage = 1;
        private bool ruptureDuringAdvance;

        public override AnomalySpawnPoint.SurfaceType SpawnSurface => AnomalySpawnPoint.SurfaceType.Wall;
        public override bool IsHazardActive => false;
        public override int DangerStage => stage;
        public override int ResolutionReward => stage;

        public override float AdvanceHazard(float deltaTime)
        {
            if (!ruptureDuringAdvance) return 0f;
            ruptureDuringAdvance = false;
            NotifyRuptured();
            Destroy(gameObject);
            return 0f;
        }

        public void SetStage(int value) => stage = Mathf.Clamp(value, 1, 3);
        public void ResolveForTest() => NotifyResolved();
        public void RuptureForTest() => NotifyRuptured();
        public void RuptureDuringAdvanceForTest() => ruptureDuringAdvance = true;
    }
#endif
}
