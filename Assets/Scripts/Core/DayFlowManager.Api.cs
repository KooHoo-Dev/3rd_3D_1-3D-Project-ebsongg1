using System;
using UnityEngine;

namespace WardShift
{
    /// <summary>기존 HUD와 셋업 스크립트에 제공하는 DayFlowManager 호환 API.</summary>
    public sealed partial class DayFlowManager
    {
        public int CurrentDay => currentDay;
        public DayState State => state;
        public float RemainingSeconds => remainingSeconds;
        public bool IsCleaningUp => Session.IsCleaningUp;
        public int CleanupAnomalyCount => IsCleaningUp && anomalySpawner != null
            ? anomalySpawner.ActiveAnomalyCount : 0;
        public float DayDurationSeconds => dayDurationSeconds;
        public float SpawnStopBeforeEndSeconds => spawnStopBeforeEndSeconds;
        public int FinalDay => Mathf.Max(1, finalDay);
        public int DayRandomSeed => dayRandomSeed;
        public bool InventoryOpen => dayUi != null && dayUi.InventoryOpen;
        public bool IsMapOpen => dayUi != null && dayUi.IsMapOpen;
        public bool DaySupplyPending => supply != null && supply.Pending;
        public bool SupplyPanelOpen => supply != null && supply.PanelOpen;
        public ItemType SupplyItemAwaitingExchange => supply != null ? supply.AwaitingExchange : ItemType.None;
        public bool UpgradePurchasedThisClear => upgrades != null && upgrades.PurchasedThisClear;
        public int MobilityUpgradeLevel => upgrades != null ? upgrades.MobilityLevel : startingMobilityLevel;
        public int ContaminationResistanceLevel => upgrades != null ? upgrades.ResistanceLevel : startingResistanceLevel;
        public int FieldPurificationLevel => upgrades != null ? upgrades.PurificationLevel : startingPurificationLevel;
        public FloodCondition ActiveFloodCondition => activeFloodCondition;
        public FloodCondition NextDayFloodCondition => nextDayFloodCondition;
        public float SafeZoneRecoveryMultiplier => FloodConditionRules.SafeZoneRecoveryMultiplier(activeFloodCondition);
        public float PotionRecoveryAmount => upgrades != null
            ? upgrades.PotionRecoveryAmount(activeFloodCondition)
            : FloodConditionRules.PotionRecoveryAmount(
                DayUpgradeRules.PotionRecovery(startingPurificationLevel, PlayerInventory.PotionRecovery), activeFloodCondition);
        public FacilityZoneTracker ZoneTracker => zoneTracker;
        public PlayerInventory Inventory => inventory;
        public WardStatus Status => wardStatus;
        public PlayerContamination Player => wardStatus != null ? wardStatus.Player : null;
        public PlayerPickupInteractor PickupInteractor => pickupInteractor;
        public event Action<bool> MapVisibilityChanged;
        public event Action<AnomalyBehaviour, int> AnomalyResolutionRewarded;
        public bool IsConfigured => wardStatus != null && anomalySpawner != null && inventory != null
            && magicCaster != null && playerRoot != null && startPoint != null && pickupInteractor != null;
        internal int MinimumAnomalyReward => minimumAnomalyReward;
        internal int MaximumAnomalyReward => maximumAnomalyReward;

        internal void RaiseAnomalyResolutionRewarded(AnomalyBehaviour anomaly, int reward)
        {
            AnomalyResolutionRewarded?.Invoke(anomaly, reward);
        }

        /// <summary>인벤토리 창을 열거나 닫음.</summary>
        public void ToggleInventory()
        {
            if (dayUi != null) dayUi.ToggleInventory();
        }

        /// <summary>지도 열기 입력. 인벤토리와 DAY 종료 화면이 열려 있을 때는 무시합니다.</summary>
        public void ToggleMap()
        {
            if (dayUi != null) dayUi.ToggleMap();
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

#if UNITY_EDITOR
        /// <summary>Play Mode 검사에서만 세션 상태를 지정한다.</summary>
        public void SetDayAndStateForTest(int day, DayState newState) =>
            session.SetDayAndStateForTest(day, newState);

        public void SetRemainingSecondsForTest(float seconds) =>
            session.SetRemainingSecondsForTest(seconds);

        public void SetActiveFloodConditionForTest(FloodCondition condition) =>
            session.SetActiveFloodConditionForTest(condition);
#endif

        public int GetUpgradeLevel(DayUpgradeType type)
        {
            if (upgrades != null) return upgrades.GetLevel(type);
            switch (type)
            {
                case DayUpgradeType.Mobility: return startingMobilityLevel;
                case DayUpgradeType.ContaminationResistance: return startingResistanceLevel;
                case DayUpgradeType.FieldPurification: return startingPurificationLevel;
                default: return 0;
            }
        }

        public int GetNextUpgradePrice(DayUpgradeType type)
        {
            if (GetUpgradeLevel(type) >= 2) return 0;
            EnsureUpgradePrices();
            return upgrades != null ? upgrades.GetNextPrice(type, upgradeStagePrices)
                : DayUpgradeRules.NextPrice(GetUpgradeLevel(type), upgradeStagePrices);
        }

        public string GetUpgradeEffectText(DayUpgradeType type, int level)
        {
            FloodCondition condition = state == DayState.DayClear
                ? nextDayFloodCondition : activeFloodCondition;
            return DayUpgradeController.GetEffectText(type, level, condition);
        }

        public bool CanPurchaseUpgrade(DayUpgradeType type)
        {
            EnsureUpgradePrices();
            return upgrades != null && upgrades.CanPurchase(type, state, upgradeStagePrices);
        }

        public bool TryPurchaseUpgrade(DayUpgradeType type)
        {
            EnsureUpgradePrices();
            if (upgrades == null || !upgrades.TryPurchase(type, state, upgradeStagePrices)) return false;
            ApplyUpgradeEffects();
            return true;
        }

        public void OpenDaySupplyChoice()
        {
            if (supply != null) supply.Open(InventoryOpen, IsMapOpen);
        }

        /// <summary>보급 선택을 미루고 진행합니다. DAY 동안 T 키로 다시 열 수 있습니다.</summary>
        public void DeferDaySupplyChoice()
        {
            if (supply != null) supply.Defer();
        }

        /// <summary>보급을 인벤토리와 DAY 시작 재도전 스냅샷에 함께 지급합니다.</summary>
        public bool TryChooseDaySupply(ItemType item, out bool needsExchange)
        {
            needsExchange = false;
            return supply != null && supply.TryChoose(item, out needsExchange);
        }

        /// <summary>가득 찬 인벤토리에서 고른 슬롯을 보급품으로 교환합니다.</summary>
        public bool TryExchangeDaySupply(int slotIndex)
        {
            return supply != null && supply.TryExchange(slotIndex);
        }

        public string GetSupplySnapshotSlotLabel(int slotIndex)
        {
            return checkpoint != null ? checkpoint.GetSupplySnapshotSlotLabel(inventory, slotIndex) : "빈 슬롯";
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

    }
}
