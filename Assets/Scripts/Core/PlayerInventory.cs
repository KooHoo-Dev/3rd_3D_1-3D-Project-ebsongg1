using System;
using UnityEngine;

namespace WardShift
{
    /// <summary>DAY 사이에 유지되는 네 칸 아이템과 마력 결정 재화를 관리.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventory : MonoBehaviour
    {
        public const int SlotCount = 4;
        public const float PotionRecovery = 30f;
        public const float RepairKitRecovery = 20f;

        /// <summary>DAY 시작 시점의 재화와 모든 소비 아이템 슬롯.</summary>
        public sealed class DayStateSnapshot
        {
            internal readonly ItemType[] Items;
            internal readonly int[] Quantities;
            internal readonly int ManaCrystals;
            internal readonly int CrystalsEarnedToday;

            internal DayStateSnapshot(ItemType[] items, int[] quantities,
                int manaCrystals, int crystalsEarnedToday)
            {
                Items = items;
                Quantities = quantities;
                ManaCrystals = manaCrystals;
                CrystalsEarnedToday = crystalsEarnedToday;
            }
        }

        [Serializable]
        private sealed class InventorySlot
        {
            [SerializeField] private ItemType item;
            [SerializeField, Min(0)] private int quantity;
            public ItemType Item { get => item; set => item = value; }
            public int Quantity { get => quantity; set => quantity = Mathf.Max(0, value); }
        }

        [SerializeField] private InventorySlot[] slots = new InventorySlot[SlotCount];
        [SerializeField, Min(0)] private int maximumStack = 99;
        [SerializeField, Min(0)] private int manaCrystals;
        [SerializeField, Min(0)] private int crystalsEarnedToday;

        /// <summary>아이템 또는 재화가 바뀌었을 때 UI를 갱신.</summary>
        public event Action Changed;
        public int ManaCrystals => manaCrystals;
        public int CrystalsEarnedToday => crystalsEarnedToday;

        private void Awake() => EnsureSlots();

        /// <summary>아이템 종류를 슬롯에 추가. 공간이 없으면 false이며 수량은 바뀌지 않음.</summary>
        public bool TryAddItem(ItemType item, int quantity = 1)
        {
            EnsureSlots();
            if (!CanAddItem(item, quantity)) return false;

            int remaining = quantity;
            int stackLimit = Mathf.Max(1, maximumStack);
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.Item != item || slot.Quantity >= stackLimit) continue;
                int added = Mathf.Min(remaining, stackLimit - slot.Quantity);
                slot.Quantity += added;
                remaining -= added;
            }
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.Item != ItemType.None || slot.Quantity != 0) continue;
                int added = Mathf.Min(remaining, stackLimit);
                slot.Item = item;
                slot.Quantity = added;
                remaining -= added;
            }

            Changed?.Invoke();
            return true;
        }

        public bool CanAddItem(ItemType item, int quantity = 1)
        {
            EnsureSlots();
            if (item == ItemType.None || quantity <= 0) return false;
            int stackLimit = Mathf.Max(1, maximumStack);
            int capacity = 0;
            foreach (InventorySlot slot in slots)
                capacity += slot.Item == item ? Mathf.Max(0, stackLimit - slot.Quantity)
                    : slot.Item == ItemType.None && slot.Quantity == 0 ? stackLimit : 0;
            return capacity >= quantity;
        }

        /// <summary>선택한 슬롯의 스택을 새 아이템 하나로 교환합니다.</summary>
        public bool TryReplaceItem(int slotIndex, ItemType item)
        {
            EnsureSlots();
            if (slotIndex < 0 || slotIndex >= slots.Length || item == ItemType.None) return false;
            slots[slotIndex].Item = item;
            slots[slotIndex].Quantity = 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>해당 칸의 소비 아이템을 사용. 효과 적용에 성공한 뒤에만 하나를 소모.</summary>
        public bool TryUseItem(int slotIndex, PlayerContamination player)
            => TryUseItem(slotIndex, player, FindFirstObjectByType<WardStatus>(), PotionRecovery);

        /// <summary>소비 아이템의 종류에 맞는 효과가 성공한 경우에만 하나를 소모합니다.</summary>
        public bool TryUseItem(int slotIndex, PlayerContamination player, WardStatus wardStatus,
            float potionRecovery)
        {
            EnsureSlots();
            if (slotIndex < 0 || slotIndex >= slots.Length) return false;
            InventorySlot slot = slots[slotIndex];
            if (slot.Quantity <= 0) return false;

            bool effectApplied;
            switch (slot.Item)
            {
                case ItemType.PurificationPotion:
                    effectApplied = player != null && player.TryReduceContamination(potionRecovery);
                    break;
                case ItemType.EmergencyRepairKit:
                    effectApplied = wardStatus != null && wardStatus.TryRepairStability(RepairKitRecovery);
                    break;
                default:
                    effectApplied = false;
                    break;
            }
            if (!effectApplied) return false;

            slot.Quantity--;
            if (slot.Quantity == 0) slot.Item = ItemType.None;
            Changed?.Invoke();
            return true;
        }

        public bool TrySpendManaCrystals(int amount)
        {
            if (amount < 0 || manaCrystals < amount) return false;
            if (amount == 0) return true;
            manaCrystals -= amount;
            Changed?.Invoke();
            return true;
        }

        /// <summary>균열 진압 보상을 누적하고 이번 DAY 획득량도 함께 기록.</summary>
        public void AddManaCrystals(int amount)
        {
            if (amount <= 0) return;
            manaCrystals = Mathf.Max(0, manaCrystals + amount);
            crystalsEarnedToday = Mathf.Max(0, crystalsEarnedToday + amount);
            Changed?.Invoke();
        }

        /// <summary>새 DAY를 시작할 때 해당 DAY 보상 합계만 초기화.</summary>
        public void BeginDay()
        {
            crystalsEarnedToday = 0;
            Changed?.Invoke();
        }

        public DayStateSnapshot CaptureDayState()
        {
            EnsureSlots();
            var items = new ItemType[slots.Length];
            var quantities = new int[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                items[i] = slots[i].Item;
                quantities[i] = slots[i].Quantity;
            }
            return new DayStateSnapshot(items, quantities, manaCrystals, crystalsEarnedToday);
        }

        public void RestoreDayState(DayStateSnapshot snapshot)
        {
            if (snapshot == null) return;
            EnsureSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Item = snapshot.Items != null && i < snapshot.Items.Length
                    ? snapshot.Items[i] : ItemType.None;
                slots[i].Quantity = snapshot.Quantities != null && i < snapshot.Quantities.Length
                    ? snapshot.Quantities[i] : 0;
                if (slots[i].Quantity <= 0) slots[i].Item = ItemType.None;
            }
            manaCrystals = Mathf.Max(0, snapshot.ManaCrystals);
            crystalsEarnedToday = Mathf.Max(0, snapshot.CrystalsEarnedToday);
            Changed?.Invoke();
        }

        /// <summary>현재 DAY 시작 스냅샷에 보급품을 반영한 복사본을 만듭니다.</summary>
        public bool TryAddItemToDayState(DayStateSnapshot snapshot, ItemType item, int quantity,
            out DayStateSnapshot updated)
        {
            updated = null;
            if (snapshot == null || item == ItemType.None || quantity <= 0
                || snapshot.Items == null || snapshot.Quantities == null) return false;

            ItemType[] items = (ItemType[])snapshot.Items.Clone();
            int[] quantities = (int[])snapshot.Quantities.Clone();
            if (!TryAddToArrays(items, quantities, item, quantity, Mathf.Max(1, maximumStack))) return false;
            updated = new DayStateSnapshot(items, quantities, snapshot.ManaCrystals,
                snapshot.CrystalsEarnedToday);
            return true;
        }

        /// <summary>선택한 스냅샷 슬롯을 보급품으로 교환한 복사본을 만듭니다.</summary>
        public bool TryReplaceItemInDayState(DayStateSnapshot snapshot, int slotIndex, ItemType item,
            out DayStateSnapshot updated)
        {
            updated = null;
            if (snapshot == null || item == ItemType.None || snapshot.Items == null
                || snapshot.Quantities == null || slotIndex < 0 || slotIndex >= snapshot.Items.Length
                || slotIndex >= snapshot.Quantities.Length) return false;

            ItemType[] items = (ItemType[])snapshot.Items.Clone();
            int[] quantities = (int[])snapshot.Quantities.Clone();
            items[slotIndex] = item;
            quantities[slotIndex] = 1;
            updated = new DayStateSnapshot(items, quantities, snapshot.ManaCrystals,
                snapshot.CrystalsEarnedToday);
            return true;
        }

        public ItemType GetDayStateItem(DayStateSnapshot snapshot, int slotIndex)
        {
            return snapshot != null && snapshot.Items != null && slotIndex >= 0
                && slotIndex < snapshot.Items.Length ? snapshot.Items[slotIndex] : ItemType.None;
        }

        public int GetDayStateQuantity(DayStateSnapshot snapshot, int slotIndex)
        {
            return snapshot != null && snapshot.Quantities != null && slotIndex >= 0
                && slotIndex < snapshot.Quantities.Length ? snapshot.Quantities[slotIndex] : 0;
        }

        /// <summary>새 실행을 시작할 때 아이템과 재화를 모두 초기화.</summary>
        public void ResetForNewRun()
        {
            EnsureSlots();
            foreach (InventorySlot slot in slots)
            {
                slot.Item = ItemType.None;
                slot.Quantity = 0;
            }
            manaCrystals = 0;
            crystalsEarnedToday = 0;
            Changed?.Invoke();
        }

        /// <summary>UI에서 슬롯 정보를 읽음.</summary>
        public ItemType GetItem(int slotIndex)
        {
            EnsureSlots();
            return slotIndex >= 0 && slotIndex < slots.Length ? slots[slotIndex].Item : ItemType.None;
        }

        /// <summary>UI에서 슬롯 수량을 읽음.</summary>
        public int GetQuantity(int slotIndex)
        {
            EnsureSlots();
            return slotIndex >= 0 && slotIndex < slots.Length ? slots[slotIndex].Quantity : 0;
        }

        private void EnsureSlots()
        {
            if (slots == null || slots.Length != SlotCount)
            {
                InventorySlot[] old = slots;
                slots = new InventorySlot[SlotCount];
                if (old != null)
                    for (int i = 0; i < Mathf.Min(old.Length, slots.Length); i++) slots[i] = old[i];
            }
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == null) slots[i] = new InventorySlot();
        }

        private static bool TryAddToArrays(ItemType[] items, int[] quantities, ItemType item,
            int quantity, int stackLimit)
        {
            int capacity = 0;
            for (int i = 0; i < items.Length && i < quantities.Length; i++)
                capacity += items[i] == item ? Mathf.Max(0, stackLimit - quantities[i])
                    : items[i] == ItemType.None && quantities[i] == 0 ? stackLimit : 0;
            if (capacity < quantity) return false;

            int remaining = quantity;
            for (int i = 0; i < items.Length && i < quantities.Length && remaining > 0; i++)
            {
                if (items[i] != item || quantities[i] >= stackLimit) continue;
                int added = Mathf.Min(remaining, stackLimit - quantities[i]);
                quantities[i] += added;
                remaining -= added;
            }
            for (int i = 0; i < items.Length && i < quantities.Length && remaining > 0; i++)
            {
                if (items[i] != ItemType.None || quantities[i] != 0) continue;
                int added = Mathf.Min(remaining, stackLimit);
                items[i] = item;
                quantities[i] = added;
                remaining -= added;
            }
            return remaining == 0;
        }
    }

    /// <summary>소비 아이템 종류.</summary>
    public enum ItemType { None, PurificationPotion, EmergencyRepairKit }
}
