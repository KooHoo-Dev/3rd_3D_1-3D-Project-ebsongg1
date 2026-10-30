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
        private readonly InputAction supplyAction = new InputAction(
            "Open Day Supply", InputActionType.Button, "<Keyboard>/t");
        private InputAction daySkipAction;
        private DayFlowManager manager;
        private int suppressedDayTickFrame = -1;

        public bool SuppressDayTickThisFrame => suppressedDayTickFrame == Time.frameCount;

        public void Configure(DayFlowManager dayFlow, bool allowDaySkip, Key daySkipKey)
        {
            manager = dayFlow;
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
            supplyAction.Enable();
            if (daySkipAction != null) daySkipAction.Enable();
        }

        private void LateUpdate()
        {
            if (manager == null || !manager.isActiveAndEnabled) return;
            if (manager.CanUseDaySkipInput && daySkipAction != null
                && daySkipAction.WasPressedThisFrame())
            {
                suppressedDayTickFrame = Time.frameCount;
                manager.CheatAdvanceDay();
                return;
            }
            if (supplyAction.WasPressedThisFrame() && manager.DaySupplyPending && !manager.SupplyPanelOpen)
            {
                suppressedDayTickFrame = Time.frameCount;
                manager.OpenDaySupplyChoice();
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
            supplyAction.Disable();
            if (daySkipAction != null) daySkipAction.Disable();
        }

        private void OnDestroy()
        {
            inventoryAction.Dispose();
            mapAction.Dispose();
            supplyAction.Dispose();
            if (daySkipAction != null) daySkipAction.Dispose();
        }
    }
}
