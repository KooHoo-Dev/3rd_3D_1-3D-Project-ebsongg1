using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>PlayerInventory의 네 슬롯을 기존 DAY Canvas에 표시하고 슬롯 클릭을 연결합니다.</summary>
    internal sealed class InventoryUIController : IDisposable
    {
        private const string PotionIconResource = "WardShift/UI/wardshift_icon_purification_potion";
        private const string RepairKitIconResource = "WardShift/UI/wardshift_icon_emergency_repair_kit";

        private static readonly Color EmptySlotColor = new Color32(18, 33, 39, 228);
        private static readonly Color UnavailableSlotColor = new Color32(24, 42, 46, 255);
        private static readonly Color EmptyFrameColor = new Color32(70, 92, 88, 105);
        private static readonly Color UnavailableFrameColor = new Color32(93, 112, 104, 150);
        private static readonly Color PotionAccent = new Color32(115, 203, 190, 255);
        private static readonly Color RepairAccent = new Color32(203, 171, 111, 255);

        private readonly TMP_FontAsset font;
        private readonly TMP_Text[] slotNames = new TMP_Text[PlayerInventory.SlotCount];
        private readonly TMP_Text[] slotEffects = new TMP_Text[PlayerInventory.SlotCount];
        private readonly TMP_Text[] slotStatuses = new TMP_Text[PlayerInventory.SlotCount];
        private readonly TMP_Text[] slotCounts = new TMP_Text[PlayerInventory.SlotCount];
        private readonly Image[] slotIcons = new Image[PlayerInventory.SlotCount];
        private readonly Image[] slotIconPlates = new Image[PlayerInventory.SlotCount];
        private readonly Image[] slotFallbackMarks = new Image[PlayerInventory.SlotCount];
        private readonly Image[] slotBackgrounds = new Image[PlayerInventory.SlotCount];
        private readonly Outline[] slotOutlines = new Outline[PlayerInventory.SlotCount];
        private readonly Button[] slotButtons = new Button[PlayerInventory.SlotCount];
        private readonly TMP_Text summaryText;
        private readonly Sprite potionIcon;
        private readonly Sprite repairKitIcon;
        private PlayerInventory inventory;
        private DayFlowManager dayFlow;
        private bool needsRefresh = true;

        internal GameObject Panel { get; }

        internal InventoryUIController(Transform parent, TMP_FontAsset uiFont,
            PlayerInventory playerInventory, DayFlowManager manager)
        {
            font = uiFont;
            potionIcon = Resources.Load<Sprite>(PotionIconResource);
            repairKitIcon = Resources.Load<Sprite>(RepairKitIconResource);

            Panel = CreatePanel("InventoryPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(900f, 640f), WardShiftHUDLayout.RecordPanelColor, true);
            CreatePanel("HeaderShade", Panel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f),
                new Vector2(-2f, 166f), new Color32(14, 34, 40, 245), false);
            AddRecordFrame(Panel);
            CreateText("FacilityHeader", Panel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -25f),
                new Vector2(-140f, 24f), 16f, TextAlignmentOptions.Center,
                WardShiftHUDLayout.RecordHeaderColor).text = "제17 마력 제어소";
            CreateText("CloseHint", Panel.transform, new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -23f),
                new Vector2(112f, 26f), 16f, TextAlignmentOptions.Right,
                WardShiftHUDLayout.RecordHeaderColor).text = "[B] 닫기";
            CreateText("InventoryTitle", Panel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -53f),
                new Vector2(-180f, 48f), 36f, TextAlignmentOptions.Center,
                WardShiftHUDLayout.RecordTitleColor).text = "휴대 보급품";
            CreatePanel("HeaderRule", Panel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -94f),
                new Vector2(-68f, 1.5f), WardShiftHUDLayout.RecordDividerColor, false);

            GameObject balance = CreatePanel("ManaCrystalBalance", Panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -111f), new Vector2(360f, 38f),
                new Color32(17, 42, 48, 245), false);
            Outline balanceOutline = balance.AddComponent<Outline>();
            balanceOutline.effectColor = new Color32(139, 126, 92, 125);
            balanceOutline.effectDistance = new Vector2(1f, -1f);
            balanceOutline.useGraphicAlpha = true;
            summaryText = CreateText("InventorySummary", balance.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.zero, 20f, TextAlignmentOptions.Center, WardShiftHUDLayout.Gold);

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                int slotIndex = i;
                int column = i % 2;
                int row = i / 2;
                Vector2 position = new Vector2(column == 0 ? -193f : 193f,
                    row == 0 ? 34f : -140f);
                GameObject slot = CreatePanel($"InventorySlot_{i + 1}", Panel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), position, new Vector2(370f, 152f),
                    Color.white, true);
                slotBackgrounds[i] = slot.GetComponent<Image>();
                slotOutlines[i] = slot.AddComponent<Outline>();
                slotOutlines[i].effectColor = WardShiftHUDLayout.RecordFrameColor;
                slotOutlines[i].effectDistance = new Vector2(1f, -1f);
                slotOutlines[i].useGraphicAlpha = true;

                Button button = slot.AddComponent<Button>();
                button.targetGraphic = slotBackgrounds[i];
                button.transition = Selectable.Transition.ColorTint;
                button.onClick.AddListener(() => UseSlot(slotIndex));
                slotButtons[i] = button;
                ConfigureButtonColors(button, false, false);

                GameObject iconPlate = CreatePanel("ItemIconPlate", slot.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(-137f, 0f),
                    new Vector2(80f, 80f), new Color32(11, 31, 38, 255), false);
                slotIconPlates[i] = iconPlate.GetComponent<Image>();
                Outline iconFrame = iconPlate.AddComponent<Outline>();
                iconFrame.effectColor = new Color32(139, 126, 92, 120);
                iconFrame.effectDistance = new Vector2(1f, -1f);
                iconFrame.useGraphicAlpha = true;

                GameObject iconObject = new GameObject("ItemIcon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(iconPlate.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = new Vector2(5f, 5f);
                iconRect.offsetMax = new Vector2(-5f, -5f);
                slotIcons[i] = iconObject.GetComponent<Image>();
                slotIcons[i].preserveAspect = true;
                slotIcons[i].raycastTarget = false;

                GameObject fallbackObject = new GameObject("MissingIconFallback",
                    typeof(RectTransform), typeof(Image));
                fallbackObject.transform.SetParent(iconPlate.transform, false);
                RectTransform fallbackRect = fallbackObject.GetComponent<RectTransform>();
                fallbackRect.anchorMin = fallbackRect.anchorMax = new Vector2(0.5f, 0.5f);
                fallbackRect.pivot = new Vector2(0.5f, 0.5f);
                fallbackRect.anchoredPosition = Vector2.zero;
                fallbackRect.sizeDelta = new Vector2(14f, 14f);
                fallbackRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                slotFallbackMarks[i] = fallbackObject.GetComponent<Image>();
                slotFallbackMarks[i].color = WardShiftHUDLayout.Gold;
                slotFallbackMarks[i].raycastTarget = false;

                slotNames[i] = CreateText("ItemName", slot.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(106f, -17f), new Vector2(184f, 31f), 23f,
                    TextAlignmentOptions.Left, WardShiftHUDLayout.RecordTitleColor);
                slotNames[i].enableAutoSizing = true;
                slotNames[i].fontSizeMin = 18f;
                slotNames[i].fontSizeMax = 23f;

                slotEffects[i] = CreateText("ItemEffect", slot.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(106f, -51f), new Vector2(228f, 25f), 18f,
                    TextAlignmentOptions.Left, PotionAccent);
                slotEffects[i].enableAutoSizing = true;
                slotEffects[i].fontSizeMin = 15f;
                slotEffects[i].fontSizeMax = 18f;

                slotStatuses[i] = CreateText("ItemAvailability", slot.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(106f, -83f), new Vector2(230f, 23f), 15f,
                    TextAlignmentOptions.Left, WardShiftHUDLayout.RecordHeaderColor);
                slotStatuses[i].enableAutoSizing = true;
                slotStatuses[i].fontSizeMin = 12f;
                slotStatuses[i].fontSizeMax = 15f;

                CreatePanel("QuantityPlate", slot.transform, new Vector2(1f, 1f),
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -13f),
                    new Vector2(66f, 29f), new Color32(15, 39, 45, 245), false);
                slotCounts[i] = CreateText("ItemQuantity", slot.transform,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-14f, -13f), new Vector2(66f, 29f), 17f,
                    TextAlignmentOptions.Center, WardShiftHUDLayout.Gold);
            }

            CreateText("InventoryHint", Panel.transform, new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f),
                new Vector2(-56f, 34f), 17f, TextAlignmentOptions.Center,
                WardShiftHUDLayout.RecordHeaderColor).text =
                "아이템 카드를 클릭해 사용  ·  효과가 적용될 때만 1개 소모";

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

            for (int i = 0; i < slotNames.Length; i++)
            {
                ItemType item = inventory.GetItem(i);
                int quantity = inventory.GetQuantity(i);
                bool potion = item == ItemType.PurificationPotion && quantity > 0;
                bool repairKit = item == ItemType.EmergencyRepairKit && quantity > 0;
                bool hasItem = potion || repairKit;
                Sprite icon = potion ? potionIcon : repairKit ? repairKitIcon : null;

                slotNames[i].text = potion ? "정화약" : repairKit ? "비상 수리 키트" : "빈 슬롯";
                slotNames[i].color = hasItem ? WardShiftHUDLayout.RecordTitleColor
                    : new Color32(139, 155, 150, 190);
                slotEffects[i].text = potion
                    ? $"오염도 {dayFlow.PotionRecoveryAmount:F0} 회복"
                    : repairKit ? $"시설 안정도 {PlayerInventory.RepairKitRecovery:F0} 회복" : string.Empty;
                Color accent = potion ? PotionAccent : repairKit ? RepairAccent : WardShiftHUDLayout.RecordHeaderColor;
                slotEffects[i].color = accent;
                slotCounts[i].text = hasItem ? $"x{quantity}" : string.Empty;
                slotIcons[i].sprite = icon;
                slotIcons[i].enabled = hasItem && icon != null;
                slotFallbackMarks[i].gameObject.SetActive(hasItem && icon == null);
                slotIconPlates[i].color = hasItem
                    ? new Color32(11, 31, 38, 255) : new Color32(13, 28, 34, 170);
                slotBackgrounds[i].color = Color.white;

                if (!hasItem)
                {
                    slotStatuses[i].text = "빈 슬롯";
                    slotStatuses[i].color = new Color32(132, 149, 145, 155);
                }
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
                bool potion = hasItem && item == ItemType.PurificationPotion;
                bool repairKit = hasItem && item == ItemType.EmergencyRepairKit;
                bool recognizedItem = potion || repairKit;
                bool usable = (potion && dayFlow.Player != null && dayFlow.Player.Value > 0f)
                    || (repairKit && dayFlow.Status != null && dayFlow.Status.Stability < 100f);

                ConfigureButtonColors(slotButtons[i], recognizedItem, usable);
                slotOutlines[i].effectColor = !recognizedItem ? EmptyFrameColor
                    : usable ? WardShiftHUDLayout.RecordFrameColor : UnavailableFrameColor;
                slotIcons[i].color = usable ? Color.white : new Color32(177, 193, 188, 180);

                if (recognizedItem)
                {
                    slotStatuses[i].text = usable ? "사용 가능 · 클릭하여 사용" : "현재 사용 불가";
                    slotStatuses[i].color = usable ? WardShiftHUDLayout.RecordHeaderColor
                        : new Color32(168, 174, 164, 190);
                }
                else if (slotStatuses[i].text != "빈 슬롯")
                {
                    slotStatuses[i].text = "빈 슬롯";
                    slotStatuses[i].color = new Color32(132, 149, 145, 155);
                }
            }
        }

        private static void ConfigureButtonColors(Button button, bool hasItem, bool usable)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = WardShiftHUDLayout.RecordCardColor;
            colors.highlightedColor = WardShiftHUDLayout.RecordButtonHoverColor;
            colors.pressedColor = WardShiftHUDLayout.RecordButtonPressedColor;
            colors.selectedColor = WardShiftHUDLayout.RecordButtonHoverColor;
            colors.disabledColor = hasItem ? UnavailableSlotColor : EmptySlotColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.interactable = usable;
        }

        public void Dispose()
        {
            if (inventory != null) inventory.Changed -= OnInventoryChanged;
            inventory = null;
            dayFlow = null;
        }

        private void AddRecordFrame(GameObject panel)
        {
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = WardShiftHUDLayout.RecordFrameColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            AddCornerMark(panel.transform, new Vector2(0f, 1f), new Vector2(14f, -14f));
            AddCornerMark(panel.transform, new Vector2(1f, 1f), new Vector2(-14f, -14f));
            AddCornerMark(panel.transform, new Vector2(0f, 0f), new Vector2(14f, 14f));
            AddCornerMark(panel.transform, new Vector2(1f, 0f), new Vector2(-14f, 14f));
        }

        private void AddCornerMark(Transform parent, Vector2 anchor, Vector2 offset)
        {
            Vector2 pivot = new Vector2(anchor.x, anchor.y);
            Color color = new Color32(165, 149, 106, 210);
            CreatePanel("FrameCornerHorizontal", parent, anchor, anchor, pivot,
                offset, new Vector2(18f, 1.5f), color, false);
            CreatePanel("FrameCornerVertical", parent, anchor, anchor, pivot,
                offset, new Vector2(1.5f, 18f), color, false);
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            Color color, bool raycastTarget)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
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
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }
    }
}
