using UnityEngine;

namespace WardShift
{
    /// <summary>레이캐스트로 조준하고 E키를 눌러 획득하는 월드 아이템.</summary>
    [DisallowMultipleComponent]
    public sealed class ItemPickup : MonoBehaviour
    {
        [SerializeField] private ItemType item = ItemType.PurificationPotion;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField, HideInInspector] private bool isCollected;

        public bool IsCollected => isCollected;

        /// <summary>아이템을 인벤토리에 넣을 수 있을 때만 월드 오브젝트를 제거.</summary>
        public bool TryCollect(PlayerInventory inventory)
        {
            if (inventory == null || !isActiveAndEnabled) return false;
            if (!inventory.TryAddItem(item, Mathf.Max(1, quantity))) return false;
            isCollected = true;
            gameObject.SetActive(false);
            return true;
        }

        /// <summary>DAY 재도전 체크포인트에서 월드 픽업 상태를 복원합니다.</summary>
        public void RestoreDayState(bool collected, bool activeSelf)
        {
            isCollected = collected;
            gameObject.SetActive(activeSelf && !collected);
        }
    }
}
