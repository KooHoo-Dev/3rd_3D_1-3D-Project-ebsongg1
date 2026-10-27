using UnityEngine;

namespace WardShift
{
    /// <summary>DAY 보급 선택과 재도전 스냅샷 지급을 함께 관리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DaySupplyController : MonoBehaviour
    {
        private DayFlowManager manager;
        private WardStatus wardStatus;
        private PlayerInventory inventory;
        private DayRetryCheckpoint checkpoint;
        private bool pending;
        private bool claimed;
        private bool panelOpen;
        private ItemType awaitingExchange;

        public bool Pending => pending;
        public bool Claimed => claimed;
        public bool PanelOpen => panelOpen;
        public ItemType AwaitingExchange => awaitingExchange;

        public void Configure(DayFlowManager dayFlow, WardStatus status,
            PlayerInventory playerInventory, DayRetryCheckpoint retryCheckpoint)
        {
            manager = dayFlow;
            wardStatus = status;
            inventory = playerInventory;
            checkpoint = retryCheckpoint;
        }

        public void BeginDay(int day, bool isRetry)
        {
            panelOpen = false;
            awaitingExchange = ItemType.None;
            if (isRetry) return;
            claimed = false;
            pending = day > 1;
            if (day != 1) return;
            claimed = inventory.TryAddItem(ItemType.PurificationPotion);
            pending = !claimed;
        }

        public void ClearAfterDay()
        {
            pending = false;
            ClosePanel();
        }

        public void ClosePanel()
        {
            panelOpen = false;
            awaitingExchange = ItemType.None;
        }

        public void RestoreState(bool wasPending, bool wasClaimed)
        {
            pending = wasPending;
            claimed = wasClaimed;
            ClosePanel();
        }

        public void Open(bool inventoryOpen, bool mapOpen)
        {
            if (manager.State != DayState.Playing || !pending || inventoryOpen || mapOpen) return;
            panelOpen = true;
            if (wardStatus != null) wardStatus.PauseGameForUI();
        }

        public void Defer()
        {
            if (!panelOpen) return;
            ClosePanel();
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
        }

        public bool TryChoose(ItemType item, out bool needsExchange)
        {
            needsExchange = false;
            if (manager.State != DayState.Playing || !panelOpen || !pending
                || (item != ItemType.PurificationPotion && item != ItemType.EmergencyRepairKit)
                || inventory == null || checkpoint == null) return false;

            if (!checkpoint.TryAddSupply(inventory, item, out needsExchange))
            {
                if (needsExchange) awaitingExchange = item;
                return false;
            }
            CompleteClaim();
            return true;
        }

        public bool TryExchange(int slotIndex)
        {
            if (manager.State != DayState.Playing || !panelOpen || !pending
                || awaitingExchange == ItemType.None || inventory == null || checkpoint == null)
                return false;
            if (!checkpoint.TryExchangeSupply(inventory, slotIndex, awaitingExchange)) return false;
            CompleteClaim();
            return true;
        }

        private void CompleteClaim()
        {
            pending = false;
            claimed = true;
            ClosePanel();
            checkpoint.RefreshParticipantState(manager);
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
        }
    }
}
