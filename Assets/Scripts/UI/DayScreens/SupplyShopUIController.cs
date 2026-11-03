using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>DAY 보급 상점을 표시하고 구매 버튼을 기존 DAY 구매 API에 연결합니다.</summary>
    internal sealed class SupplyShopUIController
    {
        private readonly TMP_FontAsset font;
        private readonly SupplyCardView potionCard;
        private readonly SupplyCardView repairCard;
        private readonly TMP_Text facilityDayText;
        private readonly TMP_Text titleText;
        private readonly TMP_Text messageText;
        private readonly TMP_Text balanceText;
        private readonly TMP_Text purchaseCountText;
        private readonly TMP_Text continueLabel;
        private DayFlowManager dayFlow;
        private PlayerInventory inventory;
        private string feedback = string.Empty;
        private string tutorialHint;

        internal GameObject Panel { get; }

        internal SupplyShopUIController(Transform parent, TMP_FontAsset uiFont,
            DayFlowManager manager, PlayerInventory playerInventory)
        {
            font = uiFont;
            dayFlow = manager;
            inventory = playerInventory;
            Panel = CreatePanel("DaySupplyShopPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(1040f, 760f), WardShiftHUDLayout.RecordPanelColor);
            AddRecordFrame(Panel);

            facilityDayText = CreateText("FacilityDayHeader", Panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(-60f, 24f), 16f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordHeaderColor);
            CreatePanel("HeaderRule", Panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(-72f, 1.5f),
                WardShiftHUDLayout.RecordDividerColor).GetComponent<Image>().raycastTarget = false;
            titleText = CreateText("Title", Panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -51f), new Vector2(-48f, 48f),
                36f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor);
            titleText.text = "근무 보급품 선택";
            balanceText = CreateText("ManaCrystalBalance", Panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -115f), new Vector2(-72f, 30f), 21f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.Gold);
            purchaseCountText = CreateText("PurchaseCount", Panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -155f), new Vector2(-72f, 24f), 17f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordHeaderColor);
            messageText = CreateText("ShopMessage", Panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -190f), new Vector2(-84f, 46f), 16.5f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordBodyColor);
            messageText.enableAutoSizing = true;
            messageText.fontSizeMin = 15f;
            messageText.fontSizeMax = 16.5f;

            GameObject optionsRoot = CreateRectTransformObject("SupplyOptions", Panel.transform);
            potionCard = CreateSupplyCard(optionsRoot.transform, "PurificationPotionCard",
                new Vector2(-202f, -48f), "WardShift/UI/wardshift_icon_purification_potion",
                new Color32(103, 203, 190, 255), () => TryPurchase(ItemType.PurificationPotion));
            repairCard = CreateSupplyCard(optionsRoot.transform, "EmergencyRepairKitCard",
                new Vector2(202f, -48f), "WardShift/UI/wardshift_icon_emergency_repair_kit",
                new Color32(203, 171, 111, 255), () => TryPurchase(ItemType.EmergencyRepairKit));

            Button continueButton = CreateActionButton("ContinueToNextDay", Panel.transform,
                new Vector2(0f, 36f), new Vector2(360f, 52f), "나중에 선택하고 계속",
                () => { if (dayFlow != null) dayFlow.CompleteSupplyShopAndBeginNextDay(); },
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), true);
            continueLabel = continueButton.GetComponentInChildren<TMP_Text>(true);
            continueLabel.fontSize = 18f;
            Panel.SetActive(false);
        }

        internal void Bind(DayFlowManager manager, PlayerInventory playerInventory)
        {
            dayFlow = manager;
            inventory = playerInventory;
        }

        internal void ClearFeedback() => feedback = string.Empty;

        internal void SetVisible(bool open, string supplyTutorialHint)
        {
            tutorialHint = supplyTutorialHint;
            if (Panel.activeSelf != open) Panel.SetActive(open);
            // 구매 사유에는 같은 프레임의 중복 입력 상태도 포함되므로 열린 동안 갱신합니다.
            if (open) Refresh();
        }

        private void TryPurchase(ItemType item)
        {
            if (dayFlow == null) return;
            bool purchased = dayFlow.TryPurchaseDaySupply(item, out SupplyPurchaseBlockReason failure);
            feedback = purchased
                ? $"{(item == ItemType.PurificationPotion ? "정화약" : "비상 수리 키트")} 구매 완료."
                : GetPurchaseFailureMessage(failure);
            Refresh();
        }

        private void Refresh()
        {
            if (dayFlow == null || inventory == null || !Panel.activeSelf) return;
            facilityDayText.text = $"제17 마력 제어소  ·  DAY {dayFlow.CurrentDay} 보급 기록";
            balanceText.text = $"보유 마력 조각  {inventory.ManaCrystals}개";
            titleText.text = $"DAY {dayFlow.CurrentDay} 보급 상점";
            purchaseCountText.text = $"이번 보급 구매  {dayFlow.SupplyPurchasesThisShop} / {dayFlow.MaximumSupplyPurchases}"
                + $"   ·   남은 구매 {dayFlow.RemainingSupplyPurchases}회";

            RefreshOption(potionCard, ItemType.PurificationPotion, "정화약",
                $"오염도 {dayFlow.NextDayPotionRecoveryAmount:F0} 회복",
                "마력 오염도를 낮춰주는 정화약입니다.", dayFlow.PurificationPotionPrice);
            RefreshOption(repairCard, ItemType.EmergencyRepairKit, "비상 수리 키트",
                $"시설 안정도 {PlayerInventory.RepairKitRecovery:F0} 회복",
                "시설 안정도를 높여주는 도구입니다.", dayFlow.EmergencyRepairKitPrice);

            continueLabel.text = dayFlow.SupplyPurchasesThisShop == 0
                ? "나중에 선택하고 계속" : "다음 DAY 시작";
            if (!string.IsNullOrEmpty(feedback)) messageText.text = feedback;
            else if (dayFlow.RemainingSupplyPurchases <= 0)
                messageText.text = "이번 DAY 구매 한도에 도달했습니다. 다음 DAY로 진행할 수 있습니다.";
            else if (dayFlow.CurrentDay == 1 && !string.IsNullOrEmpty(tutorialHint))
                messageText.text = tutorialHint;
            else messageText.text = "필요한 보급품을 구매하거나, 나중에 선택하고 다음 DAY로 진행하세요.";
        }

        private void RefreshOption(SupplyCardView card, ItemType item, string itemName,
            string effect, string description, int price)
        {
            SupplyPurchaseBlockReason reason = dayFlow.GetSupplyPurchaseBlockReason(item);
            Color stateColor = reason == SupplyPurchaseBlockReason.None
                ? new Color32(141, 209, 184, 255)
                : reason == SupplyPurchaseBlockReason.PurchaseLimitReached
                    ? WardShiftHUDLayout.RecordHeaderColor : new Color32(211, 153, 143, 255);
            card.Title.text = itemName;
            card.Effect.text = effect;
            card.Description.text = description;
            card.Details.text = $"가격  {price} 마력 조각  ·  보유 {inventory.GetItemCount(item)}개\n"
                + $"<color=#{ColorUtility.ToHtmlStringRGBA(stateColor)}>{GetBlockReasonText(reason)}</color>";
            card.Button.interactable = reason == SupplyPurchaseBlockReason.None;
            card.ButtonLabel.text = reason == SupplyPurchaseBlockReason.None ? $"구매 · {price}개"
                : GetBlockReasonText(reason);
            SetPurchaseButtonColors(card.Button, card.Button.interactable);
        }

        private static string GetBlockReasonText(SupplyPurchaseBlockReason reason)
        {
            switch (reason)
            {
                case SupplyPurchaseBlockReason.None: return "구매 가능";
                case SupplyPurchaseBlockReason.PurchaseLimitReached: return "구매 한도 도달";
                case SupplyPurchaseBlockReason.DuplicateInput: return "구매 처리 중";
                case SupplyPurchaseBlockReason.InsufficientManaCrystals: return "마력 조각 부족";
                case SupplyPurchaseBlockReason.InventoryFull: return "인벤토리 공간 부족";
                default: return "구매 불가";
            }
        }

        private static string GetPurchaseFailureMessage(SupplyPurchaseBlockReason reason)
        {
            switch (reason)
            {
                case SupplyPurchaseBlockReason.PurchaseLimitReached: return "이번 DAY 구매 한도에 도달했습니다.";
                case SupplyPurchaseBlockReason.DuplicateInput: return "구매 요청을 처리 중입니다. 잠시 후 다시 시도하세요.";
                case SupplyPurchaseBlockReason.InsufficientManaCrystals: return "마력 조각이 부족합니다.";
                case SupplyPurchaseBlockReason.InventoryFull: return "인벤토리에 해당 아이템을 넣을 공간이 없습니다.";
                default: return "현재는 이 아이템을 구매할 수 없습니다.";
            }
        }

        private SupplyCardView CreateSupplyCard(Transform parent, string name, Vector2 position,
            string iconResourcePath, Color accent, UnityAction onClick)
        {
            GameObject card = CreatePanel(name, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position,
                new Vector2(390f, 360f), WardShiftHUDLayout.RecordCardColor);
            AddCardFrame(card, accent);
            CreateIcon("ItemIcon", card.transform, iconResourcePath);
            TMP_Text title = CreateText("ItemName", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -137f), new Vector2(-32f, 36f),
                25f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor);
            title.enableAutoSizing = true;
            title.fontSizeMin = 21f;
            title.fontSizeMax = 25f;
            TMP_Text effect = CreateText("CoreEffect", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -174f), new Vector2(-32f, 28f),
                20f, TextAlignmentOptions.Center, accent);
            TMP_Text description = CreateText("Description", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -207f), new Vector2(-48f, 46f),
                16f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordBodyColor);
            description.enableAutoSizing = true;
            description.fontSizeMin = 14f;
            description.fontSizeMax = 16f;
            TMP_Text details = CreateText("PriceAndAvailability", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -257f), new Vector2(-40f, 44f),
                15f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordHeaderColor);
            details.richText = true;

            Button button = CreateActionButton("Purchase", card.transform, new Vector2(0f, 34f),
                new Vector2(300f, 46f), "구매", onClick,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), false);
            TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>(true);
            buttonLabel.fontSize = 18f;
            return new SupplyCardView(title, effect, description, details, button, buttonLabel);
        }

        private static void AddRecordFrame(GameObject panel)
        {
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = WardShiftHUDLayout.RecordFrameColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            AddCornerMark(panel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -14f));
            AddCornerMark(panel.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f));
            AddCornerMark(panel.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 14f));
            AddCornerMark(panel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f));
        }

        private static void AddCardFrame(GameObject card, Color accent)
        {
            Outline outline = card.AddComponent<Outline>();
            outline.effectColor = WardShiftHUDLayout.RecordFrameColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            CreatePanel("TopAccent", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-28f, 1.5f),
                new Color(accent.r, accent.g, accent.b, 0.58f)).GetComponent<Image>().raycastTarget = false;
            AddCornerMark(card.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(9f, -9f));
            AddCornerMark(card.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-9f, -9f));
            AddCornerMark(card.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(9f, 9f));
            AddCornerMark(card.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-9f, 9f));
        }

        private static void AddCornerMark(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            GameObject horizontal = CreatePanel("CornerH", parent, anchor, anchor, pivot, position,
                new Vector2(11f, 1f), WardShiftHUDLayout.RecordFrameColor);
            GameObject vertical = CreatePanel("CornerV", parent, anchor, anchor, pivot, position,
                new Vector2(1f, 11f), WardShiftHUDLayout.RecordFrameColor);
            horizontal.GetComponent<Image>().raycastTarget = false;
            vertical.GetComponent<Image>().raycastTarget = false;
        }

        private static void CreateIcon(string name, Transform parent, string resourcePath)
        {
            GameObject iconObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(parent, false);
            RectTransform rect = iconObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -13f);
            rect.sizeDelta = new Vector2(106f, 106f);
            Image image = iconObject.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>(resourcePath);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = image.sprite != null;
            image.color = Color.white;
        }

        private Button CreateActionButton(string name, Transform parent, Vector2 position,
            Vector2 size, string label, UnityAction onClick, Vector2 anchorMin, Vector2 anchorMax,
            bool secondary)
        {
            GameObject buttonObject = CreatePanel(name, parent, anchorMin, anchorMax,
                new Vector2(0.5f, 0.5f), position, size, WardShiftHUDLayout.RecordButtonColor);
            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.raycastTarget = true;
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(145, 143, 112, 155);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            ColorBlock colors = button.colors;
            colors.normalColor = secondary ? new Color32(31, 50, 54, 255) : WardShiftHUDLayout.RecordButtonColor;
            colors.highlightedColor = secondary ? new Color32(45, 70, 72, 255) : WardShiftHUDLayout.RecordButtonHoverColor;
            colors.pressedColor = WardShiftHUDLayout.RecordButtonPressedColor;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() => AudioService.Play2D(WardShiftSoundId.UiButtonClick));
            button.onClick.AddListener(onClick);
            CreateText("Label", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 18f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor).text = label;
            buttonImage.color = colors.normalColor;
            return button;
        }

        private static void SetPurchaseButtonColors(Button button, bool interactable)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = interactable ? WardShiftHUDLayout.RecordButtonColor : WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.highlightedColor = interactable ? WardShiftHUDLayout.RecordButtonHoverColor : WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.pressedColor = WardShiftHUDLayout.RecordButtonPressedColor;
            colors.selectedColor = interactable ? WardShiftHUDLayout.RecordButtonHoverColor : WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.disabledColor = WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic.color = colors.normalColor;
        }

        private static GameObject CreateRectTransformObject(string name, Transform parent)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return root;
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
            Image image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
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

        private sealed class SupplyCardView
        {
            internal readonly TMP_Text Title;
            internal readonly TMP_Text Effect;
            internal readonly TMP_Text Description;
            internal readonly TMP_Text Details;
            internal readonly Button Button;
            internal readonly TMP_Text ButtonLabel;

            internal SupplyCardView(TMP_Text title, TMP_Text effect, TMP_Text description,
                TMP_Text details, Button button, TMP_Text buttonLabel)
            {
                Title = title;
                Effect = effect;
                Description = description;
                Details = details;
                Button = button;
                ButtonLabel = buttonLabel;
            }
        }
    }
}
