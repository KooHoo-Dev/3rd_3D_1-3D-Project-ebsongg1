using UnityEngine;

namespace WardShift
{
    /// <summary>DAY CLEAR 이후 보급 상점의 구매와 단계 상태를 관리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DaySupplyController : MonoBehaviour
    {
        public const int DefaultPotionPrice = 3;
        public const int DefaultRepairKitPrice = 5;
        public const int DefaultMaximumPurchasesPerShop = 2;

        [Header("보급 상점 가격")]
        [SerializeField, Min(0), InspectorName("정화 물약 가격")]
        private int potionPrice = DefaultPotionPrice;
        [SerializeField, Min(0), InspectorName("시설 수리 키트 가격")]
        private int repairKitPrice = DefaultRepairKitPrice;
        [Header("구매 제한")]
        [SerializeField, Min(0), InspectorName("상점별 최대 구매 개수")]
        private int maximumPurchasesPerShop = DefaultMaximumPurchasesPerShop;

        private DayFlowManager manager;
        private PlayerInventory inventory;
        private bool pending;
        private bool panelOpen;
        private bool purchaseInProgress;
        private int purchasesThisShop;
        private int lastPurchaseFrame = -1;

        public bool Pending => pending;
        public bool PanelOpen => panelOpen;
        public int PurchasesThisShop => purchasesThisShop;
        public int MaximumPurchasesPerShop => Mathf.Max(0, maximumPurchasesPerShop);
        public int RemainingPurchases => Mathf.Max(0, MaximumPurchasesPerShop - purchasesThisShop);
        public int PotionPrice => Mathf.Max(0, potionPrice);
        public int RepairKitPrice => Mathf.Max(0, repairKitPrice);

        public void Configure(DayFlowManager dayFlow, PlayerInventory playerInventory)
        {
            manager = dayFlow;
            inventory = playerInventory;
        }

        /// <summary>새 DAY 시작에서는 보급 상점을 열지 않는다. DAY 1의 시작 정화약은 기존 규칙으로 유지한다.</summary>
        public void BeginDay(int day, bool isRetry)
        {
            panelOpen = false;
            if (isRetry) return;

            pending = false;
            purchasesThisShop = 0;
            lastPurchaseFrame = -1;
            if (day == 1 && inventory != null)
                inventory.TryAddItem(ItemType.PurificationPotion);
        }

        public void PrepareForDayClear(bool hasNextDay)
        {
            pending = hasNextDay;
            panelOpen = false;
            purchasesThisShop = 0;
            purchaseInProgress = false;
            lastPurchaseFrame = -1;
        }

        public void ClosePanel() => panelOpen = false;

        public bool Open(bool inventoryOpen, bool mapOpen)
        {
            if (manager == null || manager.State != DayState.DayClear || !pending
                || inventoryOpen || mapOpen || purchaseInProgress) return false;
            panelOpen = true;
            return true;
        }

        public bool CanPurchase(ItemType item) => GetPurchaseBlockReason(item) == SupplyPurchaseBlockReason.None;

        public SupplyPurchaseBlockReason GetPurchaseBlockReason(ItemType item)
        {
            if (item != ItemType.PurificationPotion && item != ItemType.EmergencyRepairKit)
                return SupplyPurchaseBlockReason.InvalidItem;
            if (manager == null || manager.State != DayState.DayClear || !pending || !panelOpen
                || purchaseInProgress || inventory == null)
                return SupplyPurchaseBlockReason.NotAvailable;
            if (RemainingPurchases <= 0) return SupplyPurchaseBlockReason.PurchaseLimitReached;
            if (lastPurchaseFrame == Time.frameCount) return SupplyPurchaseBlockReason.DuplicateInput;
            if (inventory.ManaCrystals < GetPrice(item))
                return SupplyPurchaseBlockReason.InsufficientManaCrystals;
            if (!inventory.CanAddItem(item)) return SupplyPurchaseBlockReason.InventoryFull;
            return SupplyPurchaseBlockReason.None;
        }

        public bool TryPurchase(ItemType item, out SupplyPurchaseBlockReason failure)
        {
            failure = GetPurchaseBlockReason(item);
            if (failure != SupplyPurchaseBlockReason.None) return false;

            lastPurchaseFrame = Time.frameCount;
            purchaseInProgress = true;
            try
            {
                // 인벤토리에서 재화 차감과 지급을 한 트랜잭션으로 처리한다.
                if (!inventory.TryPurchaseItem(item, GetPrice(item)))
                {
                    failure = inventory.ManaCrystals < GetPrice(item)
                        ? SupplyPurchaseBlockReason.InsufficientManaCrystals
                        : SupplyPurchaseBlockReason.InventoryFull;
                    return false;
                }

                purchasesThisShop++;
                return true;
            }
            finally
            {
                purchaseInProgress = false;
            }
        }

        public bool CompleteShop()
        {
            if (manager == null || manager.State != DayState.DayClear || !pending || !panelOpen)
                return false;
            pending = false;
            panelOpen = false;
            return true;
        }

        public void RestoreState(bool wasPending, int previousPurchases)
        {
            pending = wasPending;
            purchasesThisShop = Mathf.Clamp(previousPurchases, 0, MaximumPurchasesPerShop);
            panelOpen = false;
            purchaseInProgress = false;
            lastPurchaseFrame = -1;
        }

        private int GetPrice(ItemType item)
        {
            switch (item)
            {
                case ItemType.PurificationPotion: return PotionPrice;
                case ItemType.EmergencyRepairKit: return RepairKitPrice;
                default: return 0;
            }
        }

        private void OnValidate()
        {
            potionPrice = Mathf.Max(0, potionPrice);
            repairKitPrice = Mathf.Max(0, repairKitPrice);
            maximumPurchasesPerShop = Mathf.Max(0, maximumPurchasesPerShop);
        }
    }

    public enum SupplyPurchaseBlockReason
    {
        None,
        NotAvailable,
        InvalidItem,
        DuplicateInput,
        PurchaseLimitReached,
        InsufficientManaCrystals,
        InventoryFull
    }
}
