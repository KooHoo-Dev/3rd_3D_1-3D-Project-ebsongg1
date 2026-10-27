using System;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>DAY 시작 상태를 저장·복원하는 컴포넌트의 확장 지점.</summary>
    public interface IDayRetryCheckpointParticipant
    {
        object CaptureDayRetryState();
        void RestoreDayRetryState(object state);
    }

    /// <summary>인벤토리, 월드 픽업, 참여 컴포넌트의 재도전 체크포인트를 관리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayRetryCheckpoint : MonoBehaviour
    {
        private readonly List<PickupSnapshot> pickups = new List<PickupSnapshot>();
        private readonly List<ParticipantSnapshot> participants = new List<ParticipantSnapshot>();
        private PlayerInventory.DayStateSnapshot inventorySnapshot;

        public void Capture(PlayerInventory inventory)
        {
            if (inventory == null) return;
            inventorySnapshot = inventory.CaptureDayState();
            pickups.Clear();
            foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (pickup == null || pickup.gameObject.scene != gameObject.scene) continue;
                pickups.Add(new PickupSnapshot(pickup, pickup.IsCollected, pickup.gameObject.activeSelf));
            }

            participants.Clear();
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour.gameObject.scene != gameObject.scene
                    || !(behaviour is IDayRetryCheckpointParticipant participant)) continue;
                participants.Add(new ParticipantSnapshot(participant, participant.CaptureDayRetryState()));
            }
        }

        public void Restore(PlayerInventory inventory)
        {
            if (inventory != null && inventorySnapshot != null)
                inventory.RestoreDayState(inventorySnapshot);

            foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (pickup == null || pickup.gameObject.scene != gameObject.scene) continue;
                PickupSnapshot snapshot = FindPickup(pickup);
                if (snapshot != null) pickup.RestoreDayState(snapshot.IsCollected, snapshot.ActiveSelf);
                else
                {
                    pickup.gameObject.SetActive(false);
                    Destroy(pickup.gameObject);
                }
            }

            foreach (ParticipantSnapshot snapshot in participants)
                if (snapshot.Participant is UnityEngine.Object participantObject && participantObject != null)
                    snapshot.Participant.RestoreDayRetryState(snapshot.State);
        }

        public bool TryAddSupply(PlayerInventory inventory, ItemType item, out bool needsExchange)
        {
            needsExchange = false;
            if (inventory == null || inventorySnapshot == null) return false;
            if (!inventory.TryAddItemToDayState(inventorySnapshot, item, 1,
                    out PlayerInventory.DayStateSnapshot updatedSnapshot)
                || !inventory.CanAddItem(item))
            {
                needsExchange = true;
                return false;
            }
            if (!inventory.TryAddItem(item))
            {
                needsExchange = true;
                return false;
            }
            inventorySnapshot = updatedSnapshot;
            return true;
        }

        public bool TryExchangeSupply(PlayerInventory inventory, int slotIndex, ItemType item)
        {
            if (inventory == null || inventorySnapshot == null) return false;
            if (!inventory.TryReplaceItemInDayState(inventorySnapshot, slotIndex, item,
                    out PlayerInventory.DayStateSnapshot updatedSnapshot)
                || !inventory.TryReplaceItem(slotIndex, item)) return false;
            inventorySnapshot = updatedSnapshot;
            return true;
        }

        public string GetSupplySnapshotSlotLabel(PlayerInventory inventory, int slotIndex)
        {
            ItemType item = inventory != null ? inventory.GetDayStateItem(inventorySnapshot, slotIndex)
                : ItemType.None;
            int quantity = inventory != null ? inventory.GetDayStateQuantity(inventorySnapshot, slotIndex) : 0;
            return item == ItemType.None ? "빈 슬롯" : $"{GetItemDisplayName(item)} x{quantity}";
        }

        public void RefreshParticipantState(IDayRetryCheckpointParticipant participant)
        {
            for (int i = 0; i < participants.Count; i++)
            {
                if (!ReferenceEquals(participants[i].Participant, participant)) continue;
                participants[i].State = participant.CaptureDayRetryState();
                return;
            }
        }

        private PickupSnapshot FindPickup(ItemPickup pickup)
        {
            foreach (PickupSnapshot snapshot in pickups)
                if (snapshot.Pickup == pickup) return snapshot;
            return null;
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

        private sealed class PickupSnapshot
        {
            public readonly ItemPickup Pickup;
            public readonly bool IsCollected;
            public readonly bool ActiveSelf;

            public PickupSnapshot(ItemPickup pickup, bool isCollected, bool activeSelf)
            {
                Pickup = pickup;
                IsCollected = isCollected;
                ActiveSelf = activeSelf;
            }
        }

        private sealed class ParticipantSnapshot
        {
            public readonly IDayRetryCheckpointParticipant Participant;
            public object State;

            public ParticipantSnapshot(IDayRetryCheckpointParticipant participant, object state)
            {
                Participant = participant;
                State = state;
            }
        }
    }
}
