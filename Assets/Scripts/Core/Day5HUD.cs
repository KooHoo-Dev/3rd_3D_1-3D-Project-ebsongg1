using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>DAY 표시, 인벤토리, DAY CLEAR/FAILED 창을 기본 Unity UI로 만듬.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10007)]
    public sealed class Day5HUD : MonoBehaviour
    {
        [SerializeField] private DayFlowManager dayFlow;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private TMP_FontAsset koreanFont;
        private TMP_Text dayStatusText;
        private TMP_Text pickupPromptText;
        private TMP_Text inventorySummaryText;
        private TMP_Text[] slotTexts = new TMP_Text[PlayerInventory.SlotCount];
        private TMP_Text[] slotCounts = new TMP_Text[PlayerInventory.SlotCount];
        private Image[] slotIcons = new Image[PlayerInventory.SlotCount];
        private Button[] slotButtons = new Button[PlayerInventory.SlotCount];
        private readonly UpgradeCardView[] upgradeCards = new UpgradeCardView[3];
        private GameObject inventoryPanel;
        private GameObject clearPanel;
        private GameObject failedPanel;
        private GameObject endingPanel;
        private GameObject supplyPanel;
        private GameObject supplyOptionsRoot;
        private GameObject supplyExchangeRoot;
        private Button supplyReopenButton;
        private Button[] supplyExchangeButtons = new Button[PlayerInventory.SlotCount];
        private TMP_Text clearSummaryText;
        private TMP_Text clearConditionText;
        private TMP_Text failedSummaryText;
        private TMP_Text endingSummaryText;
        private TMP_Text upgradeResultText;
        private TMP_Text supplyMessageText;
        private TMP_Text supplyPotionLabel;
        private TMP_Text supplyRepairLabel;
        private Color emptyColor = new Color(0.2f, 0.23f, 0.28f, 0.94f);
        private Color potionColor = new Color(0.55f, 0.25f, 0.95f, 1f);
        private Color repairKitColor = new Color(0.15f, 0.72f, 0.58f, 1f);
        private string upgradeResult = string.Empty;
        private int lastDisplayedClearDay;
        public bool HasKoreanFont => koreanFont != null;

        private void Awake()
        {
            if (dayFlow == null) dayFlow = GetComponent<DayFlowManager>();
            if (inventory == null && dayFlow != null) inventory = dayFlow.Inventory;
            if (inventory == null) inventory = FindFirstObjectByType<PlayerInventory>();
            BuildUI();
        }

        /// <summary>셋업 메뉴에서 진행 관리자와 인벤토리를 연결.</summary>
        public void Configure(DayFlowManager manager, PlayerInventory playerInventory, TMP_FontAsset uiFont = null)
        {
            dayFlow = manager;
            inventory = playerInventory;
            if (uiFont != null) koreanFont = uiFont;
        }

        private void Update()
        {
            if (dayFlow == null || inventory == null) return;
            dayStatusText.text = $"근무 {dayFlow.CurrentDay}일차\n남은 시간  {FormatTime(dayFlow.RemainingSeconds)}\n"
                + $"마력 결정  {inventory.ManaCrystals}개\n[B] 인벤토리  [M] 지도"
                + (dayFlow.DaySupplyPending ? "  [T] 보급" : "");
            ItemPickup pickup = dayFlow.PickupInteractor != null
                ? dayFlow.PickupInteractor.CurrentPickup : null;
            pickupPromptText.text = pickup != null && !dayFlow.InventoryOpen && !dayFlow.SupplyPanelOpen
                ? $"[E] {GetItemName(pickup.Item)} 획득" : "";
            inventoryPanel.SetActive(dayFlow.State == DayState.Playing && dayFlow.InventoryOpen);
            clearPanel.SetActive(dayFlow.State == DayState.DayClear);
            failedPanel.SetActive(dayFlow.State == DayState.DayFailed);
            if (endingPanel != null) endingPanel.SetActive(dayFlow.State == DayState.Ending);
            RefreshSupplyUI();
            if (dayFlow.State == DayState.DayClear && lastDisplayedClearDay != dayFlow.CurrentDay)
            {
                lastDisplayedClearDay = dayFlow.CurrentDay;
                upgradeResult = string.Empty;
            }
            if (clearSummaryText != null)
                clearSummaryText.text = $"{dayFlow.CurrentDay}일차 근무 완료\n"
                    + $"이번 근무 획득 마력 결정: {inventory.CrystalsEarnedToday}개\n"
                    + $"보유 마력 결정: {inventory.ManaCrystals}개";
            if (clearConditionText != null)
            {
                clearConditionText.text = dayFlow.CurrentDay < dayFlow.FinalDay
                    ? $"다음 DAY {dayFlow.CurrentDay + 1} 범람 조건\n"
                        + FloodConditionRules.GetDisplayText(dayFlow.NextDayFloodCondition)
                    : "마지막 DAY를 완료했습니다.";
            }
            if (failedSummaryText != null)
                failedSummaryText.text = $"{dayFlow.CurrentDay}일차\n"
                    + (dayFlow.Status != null && !string.IsNullOrEmpty(dayFlow.Status.FailureReason)
                        ? dayFlow.Status.FailureReason : "시설 관리 실패")
                    + "\nDAY 시작 시점의 자원과 픽업을 복원해 재도전합니다.";
            if (endingSummaryText != null)
                endingSummaryText.text = $"총 {dayFlow.FinalDay}일 근무를 모두 마쳤습니다.\n"
                    + $"마력 결정: {inventory.ManaCrystals}개\n시설 안정도: {dayFlow.Status.Stability:F0}";
            RefreshInventory();
            RefreshUpgradeCards();
        }

        private void BuildUI()
        {
            EnsureInputSystemEventSystem();
            GameObject canvasObject = new GameObject("WardShift_Day5_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            dayStatusText = CreateText("DayStatus", canvasObject.transform, new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f),
                new Vector2(430f, 165f), 25, TextAlignmentOptions.TopRight, Color.white);
            pickupPromptText = CreateText("PickupPrompt", canvasObject.transform, new Vector2(0.5f, 0.12f),
                new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(500f, 55f), 28, TextAlignmentOptions.Center, Color.white);

            BuildInventory(canvasObject.transform);
            BuildClearPanel(canvasObject.transform);
            failedPanel = BuildOverlay(canvasObject.transform, "DayFailedPanel", "근무 실패", out failedSummaryText,
                "같은 날 재도전", () => { if (dayFlow != null) dayFlow.RetryCurrentDay(); }, false);
            endingPanel = BuildOverlay(canvasObject.transform, "DayEndingPanel", "WARD SHIFT 완료", out endingSummaryText,
                "게임 종료", () => Application.Quit(), false);
            BuildSupplyPanel(canvasObject.transform);
            clearPanel.SetActive(false);
            failedPanel.SetActive(false);
        }

        private void BuildInventory(Transform parent)
        {
            inventoryPanel = CreatePanel("InventoryPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(780f, 570f), new Color(0.055f, 0.075f, 0.12f, 0.97f));
            CreateText("InventoryTitle", inventoryPanel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f),
                new Vector2(-48f, 75f), 38, TextAlignmentOptions.Center, Color.white).text = "인벤토리  ·  [B] 닫기";
            inventorySummaryText = CreateText("InventorySummary", inventoryPanel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f),
                new Vector2(-48f, 45f), 24, TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 1f));

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                int slotIndex = i;
                int column = i % 2;
                int row = i / 2;
                Vector2 position = new Vector2(column == 0 ? -180f : 180f, row == 0 ? 70f : -110f);
                GameObject slot = CreatePanel($"InventorySlot_{i + 1}", inventoryPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    position, new Vector2(330f, 135f), emptyColor);
                Button button = slot.AddComponent<Button>();
                button.targetGraphic = slot.GetComponent<Image>();
                button.onClick.AddListener(() =>
                {
                    if (inventory != null && dayFlow != null)
                        inventory.TryUseItem(slotIndex, dayFlow.Player, dayFlow.Status,
                            dayFlow.PotionRecoveryAmount);
                });
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
            CreateText("InventoryHint", inventoryPanel.transform, new Vector2(0f, 0f),
                new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f),
                new Vector2(-40f, 35f), 19, TextAlignmentOptions.Center, new Color(0.75f, 0.8f, 0.9f))
                .text = "아이템을 클릭하면 사용합니다 · 효과가 적용될 때만 소모됩니다";
            inventoryPanel.SetActive(false);
        }

        private void BuildClearPanel(Transform parent)
        {
            clearPanel = CreatePanel("DayClearPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(1160f, 720f), new Color(0.035f, 0.055f, 0.09f, 0.98f));
            CreateText("Title", clearPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(-50f, 76f),
                46, TextAlignmentOptions.Center, new Color(0.65f, 0.92f, 1f)).text = "근무 완료 · 강화 선택";
            clearSummaryText = CreateText("Summary", clearPanel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f),
                new Vector2(-60f, 82f), 20, TextAlignmentOptions.Center, Color.white);
            clearConditionText = CreateText("NextFloodCondition", clearPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -204f), new Vector2(-70f, 104f), 17,
                TextAlignmentOptions.Center, new Color(0.75f, 0.9f, 1f));

            DayUpgradeType[] types =
            {
                DayUpgradeType.Mobility,
                DayUpgradeType.ContaminationResistance,
                DayUpgradeType.FieldPurification
            };
            string[] titles = { "기동 조율", "오염 내성", "현장 정화" };
            Vector2[] positions = { new Vector2(-365f, -12f), new Vector2(0f, -12f), new Vector2(365f, -12f) };
            for (int i = 0; i < types.Length; i++)
            {
                int cardIndex = i;
                GameObject card = CreatePanel($"Upgrade_{types[i]}", clearPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    positions[i], new Vector2(330f, 310f), new Color(0.09f, 0.13f, 0.2f, 1f));
                CreateText("Name", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(-30f, 52f),
                    30, TextAlignmentOptions.Center, Color.white).text = titles[i];
                TMP_Text description = CreateText("Effect", card.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -28f),
                    new Vector2(296f, 145f), 22, TextAlignmentOptions.Center, new Color(0.82f, 0.89f, 1f));
                Button button = CreateActionButton("Purchase", card.transform, new Vector2(0f, -108f),
                    new Vector2(270f, 62f), "구매", () => OnUpgradeClicked(types[cardIndex]));
                TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>();
                upgradeCards[i] = new UpgradeCardView(types[i], description, button,
                    buttonLabel, button.GetComponent<Image>());
            }
            upgradeResultText = CreateText("UpgradeResult", clearPanel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 128f), new Vector2(-50f, 38f), 20,
                TextAlignmentOptions.Center, new Color(0.76f, 0.92f, 1f));
            CreateActionButton("NextDay", clearPanel.transform, new Vector2(0f, -320f),
                new Vector2(300f, 62f), "다음 날 시작",
                () => { if (dayFlow != null) dayFlow.BeginNextDay(); });
            clearPanel.SetActive(false);
        }

        private void BuildSupplyPanel(Transform parent)
        {
            supplyReopenButton = CreateActionButton("OpenDaySupply", parent,
                new Vector2(0f, 46f), new Vector2(250f, 56f), "[T] 보급 선택",
                () => { if (dayFlow != null) dayFlow.OpenDaySupplyChoice(); }, true,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

            supplyPanel = CreatePanel("DaySupplyPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(720f, 540f), new Color(0.035f, 0.055f, 0.09f, 0.98f));
            CreateText("Title", supplyPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(-40f, 70f),
                38, TextAlignmentOptions.Center, new Color(0.65f, 0.92f, 1f)).text = "DAY 시작 보급";
            supplyMessageText = CreateText("Message", supplyPanel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -105f),
                new Vector2(-55f, 120f), 16, TextAlignmentOptions.Center, Color.white);

            supplyOptionsRoot = CreateRectTransformObject("SupplyOptions", supplyPanel.transform);
            supplyOptionsRoot.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -70f);
            Button potionButton = CreateActionButton("ChoosePotion", supplyOptionsRoot.transform, new Vector2(-150f, 8f),
                new Vector2(275f, 130f), "정화약\n오염도 30 회복",
                () => TryChooseSupply(ItemType.PurificationPotion));
            supplyPotionLabel = potionButton.GetComponentInChildren<TMP_Text>();
            Button repairButton = CreateActionButton("ChooseRepairKit", supplyOptionsRoot.transform, new Vector2(150f, 8f),
                new Vector2(275f, 130f), "비상 수리 키트\n안정도 20 회복",
                () => TryChooseSupply(ItemType.EmergencyRepairKit));
            supplyRepairLabel = repairButton.GetComponentInChildren<TMP_Text>();

            supplyExchangeRoot = CreateRectTransformObject("SupplyExchangeOptions", supplyPanel.transform);
            supplyExchangeRoot.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);
            for (int i = 0; i < supplyExchangeButtons.Length; i++)
            {
                int slotIndex = i;
                int column = i % 2;
                int row = i / 2;
                Vector2 position = new Vector2(column == 0 ? -150f : 150f, row == 0 ? 30f : -78f);
                supplyExchangeButtons[i] = CreateActionButton($"ExchangeSlot_{i + 1}",
                    supplyExchangeRoot.transform, position, new Vector2(285f, 92f), "교환 슬롯",
                    () => TryExchangeSupply(slotIndex));
                supplyExchangeButtons[i].GetComponentInChildren<TMP_Text>().fontSize = 17f;
            }
            CreateActionButton("DeferSupply", supplyPanel.transform, new Vector2(0f, 30f),
                new Vector2(300f, 58f), "나중에 선택하고 계속",
                () => { if (dayFlow != null) dayFlow.DeferDaySupplyChoice(); }, true,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            supplyPanel.SetActive(false);
        }

        private Button CreateActionButton(string name, Transform parent, Vector2 position,
            Vector2 size, string label, UnityEngine.Events.UnityAction onClick)
        {
            return CreateActionButton(name, parent, position, size, label, onClick, true,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        }

        private Button CreateActionButton(string name, Transform parent, Vector2 position,
            Vector2 size, string label, UnityEngine.Events.UnityAction onClick, bool active,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject buttonObject = CreatePanel(name, parent, anchorMin, anchorMax,
                new Vector2(0.5f, 0.5f), position, size, new Color(0.12f, 0.42f, 0.6f, 1f));
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(onClick);
            CreateText("Label", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 22,
                TextAlignmentOptions.Center, Color.white).text = label;
            buttonObject.SetActive(active);
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

        private GameObject BuildOverlay(Transform parent, string name, string title, out TMP_Text summary,
            string buttonLabel, UnityEngine.Events.UnityAction onClick, bool active)
        {
            GameObject panel = CreatePanel(name, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(700f, 430f), new Color(0.035f, 0.055f, 0.09f, 0.98f));
            CreateText("Title", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(-40f, 90f),
                48, TextAlignmentOptions.Center, new Color(0.65f, 0.92f, 1f)).text = title;
            summary = CreateText("Summary", panel.transform, new Vector2(0f, 0.35f),
                new Vector2(1f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(-55f, 180f), 29, TextAlignmentOptions.Center, Color.white);
            GameObject buttonObject = CreatePanel("ActionButton", panel.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 55f), new Vector2(290f, 72f), new Color(0.12f, 0.42f, 0.6f, 1f));
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(onClick);
            CreateText("ButtonLabel", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 26,
                TextAlignmentOptions.Center, Color.white).text = buttonLabel;
            panel.SetActive(active);
            return panel;
        }

        private void RefreshInventory()
        {
            if (inventory == null || slotTexts == null) return;
            if (inventorySummaryText != null)
                inventorySummaryText.text = $"마력 결정  {inventory.ManaCrystals}개";
            for (int i = 0; i < slotTexts.Length; i++)
            {
                ItemType item = inventory.GetItem(i);
                int quantity = inventory.GetQuantity(i);
                bool potion = item == ItemType.PurificationPotion && quantity > 0;
                bool repairKit = item == ItemType.EmergencyRepairKit && quantity > 0;
                slotTexts[i].text = potion ? $"정화약\n오염도 {dayFlow.PotionRecoveryAmount:F0} 회복"
                    : repairKit ? "비상 수리 키트\n안정도 20 회복" : "빈 슬롯";
                slotCounts[i].text = potion || repairKit ? $"x{quantity}" : "";
                slotIcons[i].color = potion ? potionColor : repairKit ? repairKitColor
                    : new Color(0.25f, 0.28f, 0.33f, 0.9f);
                slotButtons[i].interactable = (potion && dayFlow.Player != null && dayFlow.Player.Value > 0f)
                    || (repairKit && dayFlow.Status != null && dayFlow.Status.Stability < 100f);
            }
        }

        private void RefreshUpgradeCards()
        {
            if (dayFlow == null || inventory == null || upgradeCards == null) return;
            for (int i = 0; i < upgradeCards.Length; i++)
            {
                UpgradeCardView card = upgradeCards[i];
                if (card == null) continue;
                int level = dayFlow.GetUpgradeLevel(card.Type);
                int price = dayFlow.GetNextUpgradePrice(card.Type);
                card.Description.text = $"단계 {level}/2\n현재 효과: {dayFlow.GetUpgradeEffectText(card.Type, level)}"
                    + (level < 2 ? $"\n다음 효과: {dayFlow.GetUpgradeEffectText(card.Type, level + 1)}" : "");
                bool canPurchase = dayFlow.CanPurchaseUpgrade(card.Type);
                card.Button.interactable = canPurchase;
                card.ButtonLabel.text = level >= 2 ? "최대 단계"
                    : dayFlow.UpgradePurchasedThisClear ? "오늘 구매 완료"
                    : inventory.ManaCrystals < price ? $"결정 부족 · {price}개 필요"
                    : $"구매 · {price} 결정";
                card.ButtonImage.color = canPurchase ? new Color(0.12f, 0.42f, 0.6f, 1f)
                    : new Color(0.22f, 0.25f, 0.3f, 1f);
            }
            if (upgradeResultText != null) upgradeResultText.text = upgradeResult;
        }

        private void RefreshSupplyUI()
        {
            if (dayFlow == null || supplyPanel == null) return;
            bool pending = dayFlow.DaySupplyPending && dayFlow.State == DayState.Playing;
            bool open = pending && dayFlow.SupplyPanelOpen;
            supplyPanel.SetActive(open);
            if (supplyReopenButton != null)
                supplyReopenButton.gameObject.SetActive(pending && !open);
            bool exchanging = dayFlow.SupplyItemAwaitingExchange != ItemType.None;
            supplyOptionsRoot.SetActive(open && !exchanging);
            supplyExchangeRoot.SetActive(open && exchanging);
            if (supplyPotionLabel != null)
                supplyPotionLabel.text = $"정화약\n오염도 {dayFlow.PotionRecoveryAmount:F0} 회복";
            if (supplyRepairLabel != null) supplyRepairLabel.text = "비상 수리 키트\n안정도 20 회복";
            if (supplyMessageText != null)
            {
                string conditionText = dayFlow.ActiveFloodCondition == FloodCondition.None
                    ? $"오늘의 범람 조건: {FloodConditionRules.GetName(dayFlow.ActiveFloodCondition)} · 범람 조건 없음"
                    : $"오늘의 범람 조건: {FloodConditionRules.GetName(dayFlow.ActiveFloodCondition)}\n"
                        + FloodConditionRules.GetRuleDescription(dayFlow.ActiveFloodCondition) + "\n"
                        + $"대응 힌트: {FloodConditionRules.GetResponseHint(dayFlow.ActiveFloodCondition)}";
                supplyMessageText.text = conditionText + "\n\n" + (exchanging
                    ? $"인벤토리 공간이 없어 {GetItemName(dayFlow.SupplyItemAwaitingExchange)}을(를) 바로 넣을 수 없습니다.\n교환할 슬롯을 고르세요. DAY 시작 기록의 해당 슬롯도 함께 교체됩니다."
                    : "정화약 또는 비상 수리 키트 중 하나를 선택하세요.\n가득 찬 경우 슬롯을 교환하거나, 선택을 미뤄도 근무를 계속할 수 있습니다.");
            }
            for (int i = 0; i < supplyExchangeButtons.Length; i++)
            {
                ItemType current = inventory != null ? inventory.GetItem(i) : ItemType.None;
                int count = inventory != null ? inventory.GetQuantity(i) : 0;
                string currentLabel = current == ItemType.None ? "빈 슬롯" : $"{GetItemName(current)} x{count}";
                supplyExchangeButtons[i].GetComponentInChildren<TMP_Text>().text =
                    $"교환 슬롯 {i + 1}\n현재: {currentLabel}\nDAY 시작: {dayFlow.GetSupplySnapshotSlotLabel(i)}";
            }
        }

        private void OnUpgradeClicked(DayUpgradeType type)
        {
            if (dayFlow == null) return;
            bool purchased = dayFlow.TryPurchaseUpgrade(type);
            upgradeResult = purchased ? $"{GetUpgradeName(type)} 강화 구매 완료"
                : "구매할 수 없습니다. 결정 수량과 오늘 구매 여부를 확인하세요.";
            RefreshUpgradeCards();
        }

        private void TryChooseSupply(ItemType item)
        {
            if (dayFlow == null) return;
            if (dayFlow.TryChooseDaySupply(item, out _))
            {
                RefreshInventory();
                RefreshSupplyUI();
            }
            else RefreshSupplyUI();
        }

        private void TryExchangeSupply(int slotIndex)
        {
            if (dayFlow == null) return;
            if (dayFlow.TryExchangeDaySupply(slotIndex))
            {
                RefreshInventory();
                RefreshSupplyUI();
            }
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
            text.font = koreanFont != null ? koreanFont : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        private static string GetItemName(ItemType item)
        {
            switch (item)
            {
                case ItemType.PurificationPotion: return "정화약";
                case ItemType.EmergencyRepairKit: return "비상 수리 키트";
                default: return "아이템";
            }
        }

        private static string GetUpgradeName(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return "기동 조율";
                case DayUpgradeType.ContaminationResistance: return "오염 내성";
                case DayUpgradeType.FieldPurification: return "현장 정화";
                default: return "강화";
            }
        }

        private sealed class UpgradeCardView
        {
            public readonly DayUpgradeType Type;
            public readonly TMP_Text Description;
            public readonly Button Button;
            public readonly TMP_Text ButtonLabel;
            public readonly Image ButtonImage;

            public UpgradeCardView(DayUpgradeType type, TMP_Text description, Button button,
                TMP_Text buttonLabel, Image buttonImage)
            {
                Type = type;
                Description = description;
                Button = button;
                ButtonLabel = buttonLabel;
                ButtonImage = buttonImage;
            }
        }

        private static void EnsureInputSystemEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                GameObject eventObject = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem = eventObject.GetComponent<EventSystem>();
                eventObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            else if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                InputSystemUIInputModule inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                inputModule.AssignDefaultActions();
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
