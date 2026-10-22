using UnityEngine;
using UnityEngine.InputSystem;

namespace WardShift
{
    /// <summary>기존 PlayerInteractor가 선택한 가장 가까운 충돌체에서 E키 상호작용을 처리합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10001)]
    public sealed class PlayerPickupInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private DayFlowManager dayFlow;
        private readonly InputAction interactAction = new InputAction(
            "Interact", InputActionType.Button, "<Keyboard>/e");

        /// <summary>현재 조준 중인 월드 아이템입니다.</summary>
        public ItemPickup CurrentPickup { get; private set; }

        private void Awake()
        {
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
            if (inventory == null) inventory = FindFirstObjectByType<PlayerInventory>();
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
        }

        /// <summary>셋업 메뉴에서 기존 조준기와 DAY 인벤토리를 연결합니다.</summary>
        public void Configure(PlayerInteractor playerInteractor, PlayerInventory playerInventory,
            DayFlowManager manager)
        {
            interactor = playerInteractor;
            inventory = playerInventory;
            dayFlow = manager;
        }

        private void OnEnable() => interactAction.Enable();

        private void LateUpdate()
        {
            CurrentPickup = interactor != null && interactor.CurrentCollider != null
                ? interactor.CurrentCollider.GetComponentInParent<ItemPickup>() : null;
            if (CurrentPickup != null && !CurrentPickup.isActiveAndEnabled) CurrentPickup = null;
            if (CurrentPickup == null || inventory == null || dayFlow == null
                || dayFlow.State != DayState.Playing || Time.timeScale <= 0f) return;
            if (interactAction.WasPressedThisFrame()) CurrentPickup.TryCollect(inventory);
        }

        private void OnDisable() => interactAction.Disable();
        private void OnDestroy() => interactAction.Dispose();
    }
}
