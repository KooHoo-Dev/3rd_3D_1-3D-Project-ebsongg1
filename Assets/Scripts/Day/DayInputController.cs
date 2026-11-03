using UnityEngine;
using UnityEngine.InputSystem;

namespace WardShift
{
    /// <summary>DAY 관련 키 입력만 읽고 DayFlowManager의 공개 동작을 호출한다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10005)]
    public sealed class DayInputController : MonoBehaviour
    {
        private readonly InputAction inventoryAction = new InputAction(
            "Toggle Inventory", InputActionType.Button, "<Keyboard>/b");
        private readonly InputAction mapAction = new InputAction(
            "Toggle Facility Map", InputActionType.Button, "<Keyboard>/tab");
        private readonly InputAction pauseAction = new InputAction(
            "Toggle Pause Menu", InputActionType.Button, "<Keyboard>/escape");
        private InputAction daySkipAction;
        private DayFlowManager manager;
        private PauseMenuController pauseMenu;
        private int suppressedDayTickFrame = -1;

        public bool SuppressDayTickThisFrame => suppressedDayTickFrame == Time.frameCount;

        public void Configure(DayFlowManager dayFlow, PauseMenuController configuredPauseMenu,
            bool allowDaySkip, Key daySkipKey)
        {
            manager = dayFlow;
            pauseMenu = configuredPauseMenu;
            if (daySkipAction != null)
            {
                daySkipAction.Disable();
                daySkipAction.Dispose();
                daySkipAction = null;
            }
            if (!allowDaySkip || daySkipKey == Key.None) return;
            string keyPath = daySkipKey.ToString().ToLowerInvariant();
            daySkipAction = new InputAction("Cheat: Advance Day", InputActionType.Button,
                $"<Keyboard>/{keyPath}");
            if (isActiveAndEnabled) daySkipAction.Enable();
        }

        private void OnEnable()
        {
            inventoryAction.Enable();
            mapAction.Enable();
            pauseAction.Enable();
            if (daySkipAction != null) daySkipAction.Enable();
        }

        private void LateUpdate()
        {
            if (manager == null || !manager.isActiveAndEnabled) return;
            if (pauseAction.WasPressedThisFrame())
            {
                suppressedDayTickFrame = Time.frameCount;
                if (pauseMenu != null && pauseMenu.IsOpen)
                {
                    pauseMenu.HandleEscape();
                    return;
                }
                // 준비·결과 화면은 해당 화면의 진행 입력을 유지하고 일시정지 메뉴를 겹쳐 열지 않습니다.
                if (manager.State != DayState.Playing) return;
                if (manager.InventoryOpen)
                {
                    manager.ToggleInventory();
                    return;
                }
                if (manager.IsMapOpen)
                {
                    manager.ToggleMap();
                    return;
                }
                if (manager.SupplyPanelOpen) return;
                if (pauseMenu != null) pauseMenu.Open();
                return;
            }

            // Pause 메뉴가 열린 동안 B/TAB/F8 입력이 다른 DAY UI를 바꾸지 않도록 차단합니다.
            if (pauseMenu != null && pauseMenu.IsOpen) return;
            if (manager.CanUseDaySkipInput && daySkipAction != null
                && daySkipAction.WasPressedThisFrame())
            {
                suppressedDayTickFrame = Time.frameCount;
                manager.CheatAdvanceDay();
                return;
            }
            if (mapAction.WasPressedThisFrame())
            {
                suppressedDayTickFrame = Time.frameCount;
                manager.ToggleMap();
                return;
            }
            if (inventoryAction.WasPressedThisFrame()) manager.ToggleInventory();
        }

        private void OnDisable()
        {
            inventoryAction.Disable();
            mapAction.Disable();
            pauseAction.Disable();
            if (daySkipAction != null) daySkipAction.Disable();
        }

        private void OnDestroy()
        {
            inventoryAction.Dispose();
            mapAction.Dispose();
            pauseAction.Dispose();
            if (daySkipAction != null) daySkipAction.Dispose();
        }
    }
}
