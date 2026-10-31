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
        public int SupplyPurchasesThisShop => supply != null ? supply.PurchasesThisShop : 0;
        public int MaximumSupplyPurchases => supply != null
            ? supply.MaximumPurchasesPerShop : DaySupplyController.DefaultMaximumPurchasesPerShop;
        public int RemainingSupplyPurchases => supply != null ? supply.RemainingPurchases : 0;
        public int PurificationPotionPrice => supply != null
            ? supply.PotionPrice : DaySupplyController.DefaultPotionPrice;
        public int EmergencyRepairKitPrice => supply != null
            ? supply.RepairKitPrice : DaySupplyController.DefaultRepairKitPrice;
        public bool UpgradePurchasedThisClear => upgrades != null && upgrades.PurchasedThisClear;
        public int MobilityUpgradeLevel => upgrades != null ? upgrades.MobilityLevel : startingMobilityLevel;
        public int ContaminationResistanceLevel => upgrades != null ? upgrades.ResistanceLevel : startingResistanceLevel;
        public int FacilityDefenseLevel => upgrades != null ? upgrades.FacilityDefenseLevel : startingFacilityDefenseLevel;
        public float PotionRecoveryAmount => PlayerInventory.PotionRecovery;
        public float NextDayPotionRecoveryAmount => PlayerInventory.PotionRecovery;
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

#endif

        public int GetUpgradeLevel(DayUpgradeType type)
        {
            if (upgrades != null) return upgrades.GetLevel(type);
            switch (type)
            {
                case DayUpgradeType.Mobility: return startingMobilityLevel;
                case DayUpgradeType.FacilityDefense: return startingFacilityDefenseLevel;
                case DayUpgradeType.ContaminationResistance: return startingResistanceLevel;
                default: return 0;
            }
        }

        public int GetNextUpgradePrice(DayUpgradeType type)
        {
            if (GetUpgradeLevel(type) >= DayUpgradeRules.MaximumLevel) return 0;
            EnsureUpgradePrices();
            return upgrades != null ? upgrades.GetNextPrice(type, upgradeStagePrices)
                : DayUpgradeRules.NextPrice(GetUpgradeLevel(type), upgradeStagePrices);
        }

        public string GetUpgradeEffectText(DayUpgradeType type, int level)
        {
            return upgrades != null ? upgrades.GetEffectText(type, level) : string.Empty;
        }

        public bool CanPurchaseUpgrade(DayUpgradeType type)
        {
            EnsureUpgradePrices();
            return (supply == null || !supply.PanelOpen)
                && upgrades != null && upgrades.CanPurchase(type, state, upgradeStagePrices);
        }

        public bool TryPurchaseUpgrade(DayUpgradeType type)
        {
            EnsureUpgradePrices();
            if (supply != null && supply.PanelOpen) return false;
            if (upgrades == null || !upgrades.TryPurchase(type, state, upgradeStagePrices)) return false;
            if (currentDay < FinalDay && supply != null) supply.Open(false, false);
            return true;
        }

        public bool CanPurchaseSupply(ItemType item)
        {
            return supply != null && supply.CanPurchase(item);
        }

        public SupplyPurchaseBlockReason GetSupplyPurchaseBlockReason(ItemType item)
        {
            return supply != null ? supply.GetPurchaseBlockReason(item)
                : SupplyPurchaseBlockReason.NotAvailable;
        }

        public bool TryPurchaseDaySupply(ItemType item, out SupplyPurchaseBlockReason failure)
        {
            failure = SupplyPurchaseBlockReason.NotAvailable;
            return supply != null && supply.TryPurchase(item, out failure);
        }

        /// <summary>강화를 건너뛰고 상점으로 이동합니다.</summary>
        public void OpenDaySupplyShop()
        {
            if (supply != null) supply.Open(InventoryOpen, IsMapOpen);
        }

        public bool CompleteSupplyShopAndBeginNextDay()
        {
            if (state != DayState.DayClear || supply == null || !supply.CompleteShop()) return false;
            AdvanceToNextDay();
            return true;
        }

        /// <summary>이전 호출부 호환용 건너뛰기 동작. 진행 중 DAY에는 작동하지 않습니다.</summary>
        public void DeferDaySupplyChoice()
        {
            if (supply != null && state == DayState.DayClear && supply.Pending && !supply.PanelOpen)
                supply.Open(false, false);
            CompleteSupplyShopAndBeginNextDay();
        }

        /// <summary>테스트 씬에서 근무 시간과 결정 보상 범위를 지정.</summary>
        public void ConfigureDaySettings(float durationSeconds, int minReward, int maxReward)
        {
            dayDurationSeconds = Mathf.Max(1f, durationSeconds);
            minimumAnomalyReward = Mathf.Max(0, minReward);
            maximumAnomalyReward = Mathf.Max(minimumAnomalyReward, maxReward);
        }

    }
}
