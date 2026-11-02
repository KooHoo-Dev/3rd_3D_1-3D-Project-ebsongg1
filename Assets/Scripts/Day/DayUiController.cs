using System;
using UnityEngine;

namespace WardShift
{
    /// <summary>DAY 화면의 열림 상태와 게임 일시정지를 관리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayUiController : MonoBehaviour
    {
        private DayFlowManager manager;
        private WardStatus wardStatus;
        private Action<bool> mapVisibilityChanged;
        private bool inventoryOpen;
        private bool mapOpen;

        public bool InventoryOpen => inventoryOpen;
        public bool IsMapOpen => mapOpen;

        public void Configure(DayFlowManager dayFlow, WardStatus status, Action<bool> onMapVisibilityChanged)
        {
            manager = dayFlow;
            wardStatus = status;
            mapVisibilityChanged = onMapVisibilityChanged;
        }

        public void ToggleInventory()
        {
            if (manager.State != DayState.Playing || mapOpen || manager.SupplyPanelOpen) return;
            inventoryOpen = !inventoryOpen;
            if (inventoryOpen) wardStatus.PauseGameForUI();
            else wardStatus.ResumeGameFromUI();
        }

        public void ToggleMap()
        {
            if (manager.State != DayState.Playing || inventoryOpen || manager.SupplyPanelOpen) return;
            SetMapOpen(!mapOpen, true);
        }

        public void CloseInventory() => inventoryOpen = false;

        public void SetMapOpen(bool open, bool managePause)
        {
            if (mapOpen == open) return;
            mapOpen = open;
            if (managePause && wardStatus != null)
            {
                if (open) wardStatus.PauseGameForUI();
                else wardStatus.ResumeGameFromUI();
            }
            mapVisibilityChanged?.Invoke(mapOpen);
        }
    }
}
