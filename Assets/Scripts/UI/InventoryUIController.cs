using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>PlayerInventory의 네 슬롯을 기존 DAY Canvas에 표시하고 슬롯 클릭을 연결합니다.</summary>
    internal sealed class InventoryUIController : IDisposable
    {
        private readonly TMP_FontAsset font;
        private readonly TMP_Text[] slotTexts = new TMP_Text[PlayerInventory.SlotCount];
        private readonly TMP_Text[] slotCounts = new TMP_Text[PlayerInventory.SlotCount];
        private readonly Image[] slotIcons = new Image[PlayerInventory.SlotCount];
        private readonly Button[] slotButtons = new Button[PlayerInventory.SlotCount];
        private readonly TMP_Text summaryText;
        private PlayerInventory inventory;
        private DayFlowManager dayFlow;
        private bool needsRefresh = true;

        internal GameObject Panel { get; }

        internal InventoryUIController(Transform parent, TMP_FontAsset uiFont,
            PlayerInventory playerInventory, DayFlowManager manager)
        {
            font = uiFont;
            Panel = CreatePanel("InventoryPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(780f, 570f), new Color(0.055f, 0.075f, 0.12f, 0.97f));
            CreateText("InventoryTitle", Panel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f),
                new Vector2(-48f, 75f), 38, TextAlignmentOptions.Center, Color.white).text = "인벤토리  ·  [B] 닫기";
            summaryText = CreateText("InventorySummary", Panel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f),
                new Vector2(-48f, 45f), 24, TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 1f));

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                int slotIndex = i;
                int column = i % 2;
                int row = i / 2;
                Vector2 position = new Vector2(column == 0 ? -180f : 180f, row == 0 ? 70f : -110f);
                GameObject slot = CreatePanel($"InventorySlot_{i + 1}", Panel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    position, new Vector2(330f, 135f), new Color(0.2f, 0.23f, 0.28f, 0.94f));
                Button button = slot.AddComponent<Button>();
                button.targetGraphic = slot.GetComponent<Image>();
                button.onClick.AddListener(() => UseSlot(slotIndex));
                slotButtons[i] = button;

                GameObject iconObject = new GameObject("ItemIcon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(slot.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(18f, 0f);
                iconRect.sizeDelta = new Vector2(62f, 62f);
                slotIcons[i] = iconObject.GetComponent<Image>();

                slotTexts[i] = CreateText("ItemName", slot.transform, new Vector2(0f, 0f),
                    new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(94f, 0f),
                    new Vector2(-120f, 0f), 25, TextAlignmentOptions.MidlineLeft, Color.white);
                slotCounts[i] = CreateText("ItemQuantity", slot.transform, new Vector2(1f, 0f),
                    new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-18f, 0f),
                    new Vector2(75f, 0f), 25, TextAlignmentOptions.MidlineRight, Color.white);
            }
            CreateText("InventoryHint", Panel.transform, new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f),
                new Vector2(-40f, 35f), 19, TextAlignmentOptions.Center, new Color(0.75f, 0.8f, 0.9f))
                .text = "아이템을 클릭하면 사용합니다 · 효과가 적용될 때만 소모됩니다";
            Panel.SetActive(false);
            Bind(playerInventory, manager);
        }

        internal void Bind(PlayerInventory playerInventory, DayFlowManager manager)
        {
            if (inventory != null) inventory.Changed -= OnInventoryChanged;
            inventory = playerInventory;
            dayFlow = manager;
            if (inventory != null) inventory.Changed += OnInventoryChanged;
            needsRefresh = true;
            if (Panel.activeSelf) Refresh();
        }

        internal void SetVisible(bool visible)
        {
            if (Panel.activeSelf != visible) Panel.SetActive(visible);
            if (!visible) return;
            if (needsRefresh) Refresh();
            else RefreshAvailability();
        }

        private void OnInventoryChanged()
        {
            needsRefresh = true;
            if (Panel.activeSelf) Refresh();
        }

        private void UseSlot(int slotIndex)
        {
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            if (inventory != null && dayFlow != null
                && inventory.TryUseItem(slotIndex, dayFlow.Player, dayFlow.Status,
                    dayFlow.PotionRecoveryAmount))
                AudioService.Play2D(WardShiftSoundId.PlayerItemUse);
        }

        private void Refresh()
        {
            if (inventory == null || dayFlow == null) return;
            needsRefresh = false;
            summaryText.text = $"마력 결정  {inventory.ManaCrystals}개";
            for (int i = 0; i < slotTexts.Length; i++)
            {
                ItemType item = inventory.GetItem(i);
                int quantity = inventory.GetQuantity(i);
                bool potion = item == ItemType.PurificationPotion && quantity > 0;
                bool repairKit = item == ItemType.EmergencyRepairKit && quantity > 0;
                slotTexts[i].text = potion ? $"정화약\n오염도 {dayFlow.PotionRecoveryAmount:F0} 회복"
                    : repairKit ? "비상 수리 키트\n안정도 20 회복" : "빈 슬롯";
                slotCounts[i].text = potion || repairKit ? $"x{quantity}" : "";
                slotIcons[i].color = potion ? new Color(0.55f, 0.25f, 0.95f, 1f)
                    : repairKit ? new Color(0.15f, 0.72f, 0.58f, 1f)
                    : new Color(0.25f, 0.28f, 0.33f, 0.9f);
            }
            RefreshAvailability();
        }

        private void RefreshAvailability()
        {
            if (inventory == null || dayFlow == null) return;
            for (int i = 0; i < slotButtons.Length; i++)
            {
                ItemType item = inventory.GetItem(i);
                bool hasItem = inventory.GetQuantity(i) > 0;
                slotButtons[i].interactable = (hasItem && item == ItemType.PurificationPotion
                    && dayFlow.Player != null && dayFlow.Player.Value > 0f)
                    || (hasItem && item == ItemType.EmergencyRepairKit
                        && dayFlow.Status != null && dayFlow.Status.Stability < 100f);
            }
        }

        public void Dispose()
        {
            if (inventory != null) inventory.Changed -= OnInventoryChanged;
            inventory = null;
            dayFlow = null;
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private TMP_Text CreateText(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            float fontSize, TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.font = font != null ? font : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }
    }
}
