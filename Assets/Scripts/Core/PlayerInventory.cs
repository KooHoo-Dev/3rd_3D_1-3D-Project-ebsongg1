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
            if (item == ItemType.None || quantity <= 0) return false;

            int remaining = quantity;
            int stackLimit = Mathf.Max(1, maximumStack);
            int capacity = 0;
            foreach (InventorySlot slot in slots)
                capacity += slot.Item == item ? Mathf.Max(0, stackLimit - slot.Quantity)
                    : slot.Item == ItemType.None && slot.Quantity == 0 ? stackLimit : 0;
            if (capacity < quantity) return false;

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

        /// <summary>해당 칸의 소비 아이템을 사용. 효과 적용에 성공한 뒤에만 하나를 소모.</summary>
        public bool TryUseItem(int slotIndex, PlayerContamination player)
        {
            EnsureSlots();
            if (player == null || slotIndex < 0 || slotIndex >= slots.Length) return false;
            InventorySlot slot = slots[slotIndex];
            if (slot.Item != ItemType.PurificationPotion || slot.Quantity <= 0
                || !player.TryReduceContamination(PotionRecovery)) return false;

            slot.Quantity--;
            if (slot.Quantity == 0) slot.Item = ItemType.None;
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
    }

    /// <summary>DAY 5에서 사용할 간단한 소비 아이템 종류.</summary>
    public enum ItemType { None, PurificationPotion }
}
