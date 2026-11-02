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
        private readonly GameObject optionsRoot;
        private readonly Button potionButton;
        private readonly Button repairButton;
        private readonly TMP_Text titleText;
        private readonly TMP_Text messageText;
        private readonly TMP_Text balanceText;
        private readonly TMP_Text purchaseCountText;
        private readonly TMP_Text potionLabel;
        private readonly TMP_Text repairLabel;
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
                new Vector2(760f, 600f), new Color(0.035f, 0.055f, 0.09f, 0.98f));
            titleText = CreateText("Title", Panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(-40f, 58f),
                38, TextAlignmentOptions.Center, new Color(0.65f, 0.92f, 1f));
            titleText.text = "보급 상점";
            balanceText = CreateText("ManaCrystalBalance", Panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -108f), new Vector2(-64f, 34f), 23,
                TextAlignmentOptions.Center, WardShiftHUDLayout.Gold);
            purchaseCountText = CreateText("PurchaseCount", Panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -148f), new Vector2(-64f, 32f), 19,
                TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 1f));
            messageText = CreateText("ShopMessage", Panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -190f), new Vector2(-64f, 46f), 17,
                TextAlignmentOptions.Center, Color.white);

            optionsRoot = CreateRectTransformObject("SupplyOptions", Panel.transform);
            potionButton = CreateActionButton("BuyPurificationPotion", optionsRoot.transform,
                new Vector2(-172f, -55f), new Vector2(320f, 220f), "정화 물약",
                () => TryPurchase(ItemType.PurificationPotion));
            potionLabel = potionButton.GetComponentInChildren<TMP_Text>(true);
            potionLabel.fontSize = 20f;
            repairButton = CreateActionButton("BuyEmergencyRepairKit", optionsRoot.transform,
                new Vector2(172f, -55f), new Vector2(320f, 220f), "시설 수리 키트",
                () => TryPurchase(ItemType.EmergencyRepairKit));
            repairLabel = repairButton.GetComponentInChildren<TMP_Text>(true);
            repairLabel.fontSize = 20f;

            Button continueButton = CreateActionButton("ContinueToNextDay", Panel.transform,
                new Vector2(0f, 32f), new Vector2(360f, 60f), "구매하지 않고 다음 DAY",
                () => { if (dayFlow != null) dayFlow.CompleteSupplyShopAndBeginNextDay(); },
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            continueLabel = continueButton.GetComponentInChildren<TMP_Text>(true);
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
                ? $"{(item == ItemType.PurificationPotion ? "정화 물약" : "시설 수리 키트")} 구매 완료."
                : GetPurchaseFailureMessage(failure);
            Refresh();
        }

        private void Refresh()
        {
            if (dayFlow == null || inventory == null || !Panel.activeSelf) return;
            balanceText.text = $"보유 마력 조각: {inventory.ManaCrystals}개";
            purchaseCountText.text = $"이번 보급 구매: {dayFlow.SupplyPurchasesThisShop} / {dayFlow.MaximumSupplyPurchases}"
                + $"   (남은 구매 {dayFlow.RemainingSupplyPurchases}회)";
            optionsRoot.SetActive(true);
            titleText.text = $"DAY {dayFlow.CurrentDay} 보급 상점";

            RefreshOption(potionButton, potionLabel, ItemType.PurificationPotion,
                "정화 물약", $"오염도 {dayFlow.NextDayPotionRecoveryAmount:F0} 회복", dayFlow.PurificationPotionPrice);
            RefreshOption(repairButton, repairLabel, ItemType.EmergencyRepairKit,
                "시설 수리 키트", $"시설 안정도 {PlayerInventory.RepairKitRecovery:F0} 회복",
                dayFlow.EmergencyRepairKitPrice);

            continueLabel.text = dayFlow.SupplyPurchasesThisShop == 0
                ? "구매하지 않고 다음 DAY" : "다음 DAY 시작";
            if (!string.IsNullOrEmpty(feedback)) messageText.text = feedback;
            else if (dayFlow.RemainingSupplyPurchases <= 0)
                messageText.text = "이번 DAY 구매 한도에 도달했습니다. 다음 DAY로 진행할 수 있습니다.";
            else if (dayFlow.CurrentDay == 1 && !string.IsNullOrEmpty(tutorialHint))
                messageText.text = tutorialHint;
            else messageText.text = "필요한 보급품을 구매하거나, 구매하지 않고 다음 DAY로 진행하세요.";
        }

        private void RefreshOption(Button button, TMP_Text label, ItemType item,
            string itemName, string description, int price)
        {
            SupplyPurchaseBlockReason reason = dayFlow.GetSupplyPurchaseBlockReason(item);
            label.richText = true;
            label.text = $"<b>{itemName}</b>\n{description}\n가격: {price} 마력 조각"
                + $"\n보유 수량: {inventory.GetItemCount(item)}개\n{GetBlockReasonText(reason)}";
            button.interactable = reason == SupplyPurchaseBlockReason.None;
            button.GetComponent<Image>().color = button.interactable
                ? new Color(0.12f, 0.42f, 0.6f, 1f)
                : new Color(0.22f, 0.25f, 0.3f, 1f);
        }

        private static string GetBlockReasonText(SupplyPurchaseBlockReason reason)
        {
            switch (reason)
            {
                case SupplyPurchaseBlockReason.None: return "구매 가능";
                case SupplyPurchaseBlockReason.PurchaseLimitReached: return "구매 한도 도달";
                case SupplyPurchaseBlockReason.DuplicateInput: return "중복 입력 방지 중";
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

        private Button CreateActionButton(string name, Transform parent, Vector2 position,
            Vector2 size, string label, UnityAction onClick)
        {
            return CreateActionButton(name, parent, position, size, label, onClick,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        }

        private Button CreateActionButton(string name, Transform parent, Vector2 position,
            Vector2 size, string label, UnityAction onClick, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject buttonObject = CreatePanel(name, parent, anchorMin, anchorMax,
                new Vector2(0.5f, 0.5f), position, size, new Color(0.12f, 0.42f, 0.6f, 1f));
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(() => AudioService.Play2D(WardShiftSoundId.UiButtonClick));
            button.onClick.AddListener(onClick);
            CreateText("Label", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 22,
                TextAlignmentOptions.Center, Color.white).text = label;
            buttonObject.SetActive(true);
            return button;
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
