using UnityEngine;

namespace WardShift
{
    /// <summary>레이캐스트로 조준하고 E키를 눌러 획득하는 월드 아이템입니다.</summary>
    [DisallowMultipleComponent]
    public sealed class ItemPickup : MonoBehaviour
    {
        [SerializeField] private ItemType item = ItemType.PurificationPotion;
        [SerializeField, Min(1)] private int quantity = 1;

        /// <summary>아이템을 인벤토리에 넣을 수 있을 때만 월드 오브젝트를 제거합니다.</summary>
        public bool TryCollect(PlayerInventory inventory)
        {
            if (inventory == null || !isActiveAndEnabled) return false;
            if (!inventory.TryAddItem(item, Mathf.Max(1, quantity))) return false;
            Destroy(gameObject);
            return true;
        }
    }
}
