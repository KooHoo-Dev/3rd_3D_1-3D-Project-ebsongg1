using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>DAY 표시, 인벤토리, DAY CLEAR/FAILED 창을 기본 Unity UI로 만듬.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10007)]
    public sealed partial class Day5HUD : MonoBehaviour
    {
        [SerializeField] private DayFlowManager dayFlow;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private TMP_FontAsset koreanFont;

        [Header("강화 제목 강조 색상")]
        [SerializeField, InspectorName("이동 속도 제목")]
        private Color mobilityTitleHighlightColor = new Color32(110, 200, 255, 255);
        [SerializeField, InspectorName("시설 방호 제목")]
        private Color facilityDefenseTitleHighlightColor = new Color32(110, 230, 170, 255);
        [SerializeField, InspectorName("오염 저항 제목")]
        private Color contaminationResistanceTitleHighlightColor = new Color32(205, 160, 255, 255);

        [Header("강화 설명 효과 강조 색상")]
        [SerializeField, InspectorName("이동 속도 설명에서 색칠할 단어")]
        private string mobilityEffectHighlightWord = "이동속도";
        [SerializeField, InspectorName("이동 속도 효과 문구")]
        private Color mobilityEffectHighlightColor = new Color32(110, 200, 255, 255);
        [SerializeField, InspectorName("시설 방호 설명에서 색칠할 단어")]
        private string facilityDefenseEffectHighlightWord = "지속 안정도 피해";
        [SerializeField, InspectorName("시설 방호 효과 문구")]
        private Color facilityDefenseEffectHighlightColor = new Color32(110, 230, 170, 255);
        [SerializeField, InspectorName("오염 저항 설명에서 색칠할 단어")]
        private string contaminationResistanceEffectHighlightWord = "오염도 획득량";
        [SerializeField, InspectorName("오염 저항 효과 문구")]
        private Color contaminationResistanceEffectHighlightColor = new Color32(205, 160, 255, 255);

        [Header("구매 정보 강조 색상")]
        [SerializeField, InspectorName("마력 조각 가격")]
        private Color upgradePriceHighlightColor = new Color32(255, 210, 110, 255);
        [SerializeField, InspectorName("구매 가능 상태")]
        private Color upgradeAvailableHighlightColor = new Color32(105, 225, 165, 255);
        [SerializeField, InspectorName("구매 불가 상태")]
        private Color upgradeUnavailableHighlightColor = new Color32(255, 125, 115, 255);
        [SerializeField, InspectorName("최대 단계·구매 완료 상태")]
        private Color upgradeCompleteHighlightColor = new Color32(175, 190, 210, 255);

        private TMP_Text dayStatusText;
        private TMP_Text pickupPromptText;
        private TMP_Text inventorySummaryText;
        private TMP_Text[] slotTexts = new TMP_Text[PlayerInventory.SlotCount];
        private TMP_Text[] slotCounts = new TMP_Text[PlayerInventory.SlotCount];
        private Image[] slotIcons = new Image[PlayerInventory.SlotCount];
        private Button[] slotButtons = new Button[PlayerInventory.SlotCount];
        private readonly UpgradeCardView[] upgradeCards = new UpgradeCardView[3];
        private GameObject inventoryPanel;
        private RectTransform overlayRoot;
        private GameObject clearPanel;
        private GameObject failedPanel;
        private GameObject endingPanel;
        private GameObject supplyPanel;
        private GameObject supplyOptionsRoot;
        private Button supplyPotionButton;
        private Button supplyRepairButton;
        private Button supplyContinueButton;
        private TMP_Text clearSummaryText;
        private TMP_Text failedSummaryText;
        private TMP_Text endingSummaryText;
        private TMP_Text endingDayValueText;
        private TMP_Text endingStabilityValueText;
        private TMP_Text endingContaminationValueText;
        private TMP_Text endingManaValueText;
        private TMP_Text upgradeResultText;
        private TMP_Text anomalyResolutionFeedbackText;
        private TMP_Text supplyTitleText;
        private TMP_Text supplyMessageText;
        private TMP_Text supplyBalanceText;
        private TMP_Text supplyPurchaseCountText;
        private TMP_Text supplyPotionLabel;
        private TMP_Text supplyRepairLabel;
        private TMP_Text supplyContinueLabel;
        private Color emptyColor = new Color(0.2f, 0.23f, 0.28f, 0.94f);
        private Color potionColor = new Color(0.55f, 0.25f, 0.95f, 1f);
        private Color repairKitColor = new Color(0.15f, 0.72f, 0.58f, 1f);
        private string upgradeResult = string.Empty;
        private string supplyFeedback = string.Empty;
        private int lastDisplayedClearDay;
        private float anomalyResolutionFeedbackUntil;
        public bool HasKoreanFont => koreanFont != null;

        private void Awake()
        {
            if (dayFlow == null) dayFlow = GetComponent<DayFlowManager>();
            if (inventory == null && dayFlow != null) inventory = dayFlow.Inventory;
            if (inventory == null) inventory = FindFirstObjectByType<PlayerInventory>();
            BuildUI();
            BuildTutorialUI();
        }

        /// <summary>셋업 메뉴에서 진행 관리자와 인벤토리를 연결.</summary>
        public void Configure(DayFlowManager manager, PlayerInventory playerInventory, TMP_FontAsset uiFont = null)
        {
            if (dayFlow != null) dayFlow.AnomalyResolutionRewarded -= OnAnomalyResolutionRewarded;
            dayFlow = manager;
            inventory = playerInventory;
            if (uiFont != null) koreanFont = uiFont;
            SubscribeResolutionFeedback();
        }

        private void OnEnable()
        {
            SubscribeResolutionFeedback();
        }

        private void OnDisable()
        {
            if (dayFlow != null) dayFlow.AnomalyResolutionRewarded -= OnAnomalyResolutionRewarded;
            if (anomalyResolutionFeedbackText != null)
                anomalyResolutionFeedbackText.gameObject.SetActive(false);
        }

        private void SubscribeResolutionFeedback()
        {
            if (!isActiveAndEnabled || dayFlow == null) return;
            dayFlow.AnomalyResolutionRewarded -= OnAnomalyResolutionRewarded;
            dayFlow.AnomalyResolutionRewarded += OnAnomalyResolutionRewarded;
        }

        private void OnAnomalyResolutionRewarded(AnomalyBehaviour anomaly, int reward)
        {
            if (anomaly == null || reward <= 0 || anomalyResolutionFeedbackText == null) return;
            anomalyResolutionFeedbackText.text = $"<b>{anomaly.DisplayName}</b> 진압 완료  ·  "
                + $"<color=#E8C26E>마력 결정 +{reward}</color>";
            anomalyResolutionFeedbackUntil = Time.unscaledTime + 2.5f;
            anomalyResolutionFeedbackText.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (dayFlow == null || inventory == null) return;
            UpdateTutorial();
            FitOverlaysToScreen();
            dayStatusText.text = (dayFlow.IsCleaningUp
                    ? $"<color=#E8C26E>DAY {dayFlow.CurrentDay}</color>\n<size=20>잔여 이상현상 정리 중 ({dayFlow.CleanupAnomalyCount}개)</size>\n"
                    : $"<color=#E8C26E>DAY {dayFlow.CurrentDay}</color>   남은 {FormatTime(dayFlow.RemainingSeconds)}\n")
                + $"마력 결정  {inventory.ManaCrystals}개";
            ItemPickup pickup = dayFlow.PickupInteractor != null
                ? dayFlow.PickupInteractor.CurrentPickup : null;
            pickupPromptText.text = pickup != null && !dayFlow.InventoryOpen && !dayFlow.SupplyPanelOpen
                && !dayFlow.IsMapOpen && dayFlow.State == DayState.Playing
                ? $"[E] {GetItemName(pickup.Item)} 획득" : "";
            inventoryPanel.SetActive(dayFlow.State == DayState.Playing && dayFlow.InventoryOpen);
            if (anomalyResolutionFeedbackText != null)
                anomalyResolutionFeedbackText.gameObject.SetActive(dayFlow.State == DayState.Playing
                    && Time.unscaledTime < anomalyResolutionFeedbackUntil);
            clearPanel.SetActive(dayFlow.State == DayState.DayClear && !dayFlow.SupplyPanelOpen);
            failedPanel.SetActive(dayFlow.State == DayState.DayFailed);
            if (endingPanel != null) endingPanel.SetActive(dayFlow.State == DayState.Ending);
            if (dayFlow.State == DayState.DayClear && lastDisplayedClearDay != dayFlow.CurrentDay)
            {
                lastDisplayedClearDay = dayFlow.CurrentDay;
                upgradeResult = string.Empty;
                supplyFeedback = string.Empty;
            }
            RefreshSupplyUI();
            if (clearSummaryText != null)
                clearSummaryText.text = $"DAY {dayFlow.CurrentDay} 근무 완료\n"
                    + $"이번 근무 획득 결정 +{inventory.CrystalsEarnedToday}개  ·  현재 보유 {inventory.ManaCrystals}개\n"
                    + (dayFlow.UpgradePurchasedThisClear
                        ? "이번 근무 강화 구매 완료 · 추가 구매 불가"
                        : dayFlow.CurrentDay == 1 && !Learned(Clear)
                            ? tutorialCopy.firstClear
                            : "강화는 이번 근무에 한 종류만 구매할 수 있습니다.");
            if (failedSummaryText != null)
                failedSummaryText.text = $"{dayFlow.CurrentDay}일차\n"
                    + (dayFlow.Status != null && !string.IsNullOrEmpty(dayFlow.Status.FailureReason)
                        ? dayFlow.Status.FailureReason : "시설 관리 실패")
                    + "\nDAY 시작 시점의 자원과 픽업을 복원해 재도전합니다."
                    + (showRuptureOnFailure ? "\n" + tutorialCopy.rupture : "");
            if (endingDayValueText != null)
                endingDayValueText.text = $"{dayFlow.CurrentDay}일";
            if (endingStabilityValueText != null)
                endingStabilityValueText.text = dayFlow.Status != null
                    ? $"{dayFlow.Status.Stability:F0}%" : "-";
            if (endingContaminationValueText != null)
                endingContaminationValueText.text = dayFlow.Player != null
                    ? $"{dayFlow.Player.Value:F0}%" : "-";
            if (endingManaValueText != null)
                endingManaValueText.text = $"{inventory.ManaCrystals}개";
            RefreshInventory();
            RefreshUpgradeCards();
        }

        private void BuildUI()
        {
            EnsureInputSystemEventSystem();
            WardShiftHUDLayout layout = WardShiftHUDLayout.Ensure(koreanFont);
            if (layout == null)
            {
                Debug.LogError("WARD SHIFT: 기존 MagicHUD Canvas를 찾을 수 없습니다.", this);
                enabled = false;
                return;
            }
            dayStatusText = layout.DayStatusText;
            pickupPromptText = layout.PickupText;
            GameObject canvasObject = CreateRectTransformObject("DayOverlays", layout.transform);
            overlayRoot = canvasObject.GetComponent<RectTransform>();
            anomalyResolutionFeedbackText = CreateText("AnomalyResolutionFeedback", canvasObject.transform,
                new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(980f, 62f), 29f, TextAlignmentOptions.Center,
                new Color(0.68f, 1f, 0.91f, 1f));
            anomalyResolutionFeedbackText.gameObject.SetActive(false);

            BuildInventory(canvasObject.transform);
            BuildClearPanel(canvasObject.transform);
            failedPanel = BuildOverlay(canvasObject.transform, "DayFailedPanel", "근무 실패", out failedSummaryText,
                "같은 날 재도전", () => { if (dayFlow != null) dayFlow.RetryCurrentDay(); }, false);
            AddOverlaySecondaryButton(failedPanel, "TitleButton", "타이틀로 돌아가기",
                TitleMenuController.ReturnToTitle);
            BuildEndingPanel(canvasObject.transform);
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
            CreateActionButton("ResetTutorial", inventoryPanel.transform, new Vector2(0f, 67f),
                new Vector2(300f, 36f), "튜토리얼 초기화", ResetTutorialProgress, true,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f))
                .GetComponentInChildren<TMP_Text>().fontSize = 17f;
            inventoryPanel.SetActive(false);
        }

        private void BuildClearPanel(Transform parent)
        {
            clearPanel = CreatePanel("DayClearPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(1160f, 720f), new Color(0.035f, 0.055f, 0.09f, 0.98f));
            CreateText("Title", clearPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(-50f, 66f),
                44, TextAlignmentOptions.Center, new Color(0.65f, 0.92f, 1f)).text = "근무 완료 · 강화 선택";
            clearSummaryText = CreateText("Summary", clearPanel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -102f),
                new Vector2(-60f, 78f), 19, TextAlignmentOptions.Center, Color.white);
            DayUpgradeType[] types =
            {
                DayUpgradeType.Mobility,
                DayUpgradeType.FacilityDefense,
                DayUpgradeType.ContaminationResistance
            };
            Vector2[] positions = { new Vector2(-365f, -96f), new Vector2(0f, -96f), new Vector2(365f, -96f) };
            for (int i = 0; i < types.Length; i++)
            {
                int cardIndex = i;
                GameObject card = CreatePanel($"Upgrade_{types[i]}", clearPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    positions[i], new Vector2(330f, 322f), new Color(0.09f, 0.13f, 0.2f, 1f));
                TMP_Text title = CreateText("Name", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(-30f, 44f),
                    27, TextAlignmentOptions.Center, Color.white);
                title.richText = true;
                TMP_Text description = CreateText("Effect", card.transform, new Vector2(0f, 1f),
                    new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f),
                    new Vector2(-34f, 154f), 16.5f, TextAlignmentOptions.Top, new Color(0.82f, 0.89f, 1f));
                description.richText = true;
                Button button = CreateActionButton("Purchase", card.transform, new Vector2(0f, -110f),
                    new Vector2(270f, 58f), "구매", () => OnUpgradeClicked(types[cardIndex]));
                TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>();
                buttonLabel.fontSize = 17;
                upgradeCards[i] = new UpgradeCardView(types[i], title, description, button,
                    buttonLabel, button.GetComponent<Image>());
            }
            upgradeResultText = CreateText("UpgradeResult", clearPanel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 86f), new Vector2(-50f, 26f), 18,
                TextAlignmentOptions.Center, new Color(0.76f, 0.92f, 1f));
            CreateActionButton("OpenSupplyShop", clearPanel.transform, new Vector2(0f, -321f),
                new Vector2(360f, 58f), "강화 건너뛰고 보급 상점",
                () => { if (dayFlow != null) dayFlow.OpenDaySupplyShop(); });
            clearPanel.SetActive(false);
        }

        private void BuildSupplyPanel(Transform parent)
        {
            supplyPanel = CreatePanel("DaySupplyShopPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(760f, 600f), new Color(0.035f, 0.055f, 0.09f, 0.98f));
            supplyTitleText = CreateText("Title", supplyPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(-40f, 58f),
                38, TextAlignmentOptions.Center, new Color(0.65f, 0.92f, 1f));
            supplyTitleText.text = "보급 상점";
            supplyBalanceText = CreateText("ManaCrystalBalance", supplyPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -108f), new Vector2(-64f, 34f), 23,
                TextAlignmentOptions.Center, WardShiftHUDLayout.Gold);
            supplyPurchaseCountText = CreateText("PurchaseCount", supplyPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -148f), new Vector2(-64f, 32f), 19,
                TextAlignmentOptions.Center, new Color(0.85f, 0.9f, 1f));
            supplyMessageText = CreateText("ShopMessage", supplyPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -190f), new Vector2(-64f, 46f), 17,
                TextAlignmentOptions.Center, Color.white);

            supplyOptionsRoot = CreateRectTransformObject("SupplyOptions", supplyPanel.transform);
            supplyPotionButton = CreateActionButton("BuyPurificationPotion", supplyOptionsRoot.transform,
                new Vector2(-172f, -55f), new Vector2(320f, 220f), "정화 물약",
                () => TryPurchaseSupply(ItemType.PurificationPotion));
            supplyPotionLabel = supplyPotionButton.GetComponentInChildren<TMP_Text>();
            supplyPotionLabel.fontSize = 20f;
            supplyRepairButton = CreateActionButton("BuyEmergencyRepairKit", supplyOptionsRoot.transform,
                new Vector2(172f, -55f), new Vector2(320f, 220f), "시설 수리 키트",
                () => TryPurchaseSupply(ItemType.EmergencyRepairKit));
            supplyRepairLabel = supplyRepairButton.GetComponentInChildren<TMP_Text>();
            supplyRepairLabel.fontSize = 20f;

            supplyContinueButton = CreateActionButton("ContinueToNextDay", supplyPanel.transform,
                new Vector2(0f, 32f), new Vector2(360f, 60f), "구매하지 않고 다음 DAY",
                () => { if (dayFlow != null) dayFlow.CompleteSupplyShopAndBeginNextDay(); }, true,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            supplyContinueLabel = supplyContinueButton.GetComponentInChildren<TMP_Text>();
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
                new Vector2(-145f, 55f), new Vector2(260f, 72f), new Color(0.12f, 0.32f, 0.43f, 1f));
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(onClick);
            CreateText("ButtonLabel", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 26,
                TextAlignmentOptions.Center, Color.white).text = buttonLabel;
            panel.SetActive(active);
            return panel;
        }

        private void AddOverlaySecondaryButton(GameObject panel, string name, string label,
            UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = CreatePanel(name, panel.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(145f, 55f), new Vector2(260f, 66f), new Color(0.11f, 0.12f, 0.17f, 1f));
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(onClick);
            CreateText("ButtonLabel", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 22,
                TextAlignmentOptions.Center, new Color(0.9f, 0.9f, 0.94f, 1f)).text = label;
        }

        private void BuildEndingPanel(Transform parent)
        {
            endingPanel = CreatePanel("DayEndingPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(920f, 650f), new Color32(11, 27, 35, 252));
            Outline outline = endingPanel.AddComponent<Outline>();
            outline.effectColor = new Color32(92, 134, 137, 190);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            CreatePanel("TopAccent", endingPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(-28f, 2f),
                new Color32(180, 160, 108, 235));
            CreateText("ReportEyebrow", endingPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(-64f, 28f),
                16f, TextAlignmentOptions.Center, new Color32(150, 184, 183, 255)).text =
                "제17 마력 제어소  ·  최종 근무 기록";
            CreateText("Title", endingPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -73f), new Vector2(-64f, 64f),
                39f, TextAlignmentOptions.Center, new Color32(225, 232, 222, 255)).text = "범람기 종료 보고";
            CreatePanel("HeaderDivider", endingPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -151f), new Vector2(-72f, 1f),
                new Color32(91, 119, 119, 210));

            endingSummaryText = CreateText("ClosingReport", endingPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -169f), new Vector2(-112f, 104f), 20f,
                TextAlignmentOptions.Center, new Color32(218, 224, 216, 255));
            endingSummaryText.text = "7일간 지속된 마력 범람기가 종료되었습니다.\n"
                + "제17 마력 제어소는 전 기간 동안 운용을 유지했으며,\n"
                + "근무 기록이 정상적으로 종료되었습니다.";

            CreateText("ResultsHeading", endingPanel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -287f),
                new Vector2(-84f, 24f), 15f, TextAlignmentOptions.MidlineLeft,
                new Color32(150, 184, 183, 255)).text = "최종 근무 현황";

            endingDayValueText = CreateEndingResultRow(endingPanel.transform, "SurvivedDays",
                "생존 일수", new Vector2(0f, -320f));
            endingStabilityValueText = CreateEndingResultRow(endingPanel.transform, "FacilityStability",
                "최종 시설 안정도", new Vector2(0f, -369f));
            endingContaminationValueText = CreateEndingResultRow(endingPanel.transform, "ManaContamination",
                "최종 마력 오염도", new Vector2(0f, -418f));
            endingManaValueText = CreateEndingResultRow(endingPanel.transform, "ManaCrystals",
                "보유 마력 결정", new Vector2(0f, -467f));

            CreatePanel("FooterDivider", endingPanel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 115f), new Vector2(-72f, 1f),
                new Color32(91, 119, 119, 210));
            Button restartButton = CreateActionButton("RestartButton", endingPanel.transform,
                new Vector2(-166f, -264f), new Vector2(310f, 58f), "다시 플레이", RestartGameplay);
            restartButton.GetComponentInChildren<TMP_Text>().fontSize = 21f;
            Button titleButton = CreateActionButton("TitleButton", endingPanel.transform,
                new Vector2(166f, -264f), new Vector2(310f, 58f), "타이틀로 돌아가기",
                TitleMenuController.ReturnToTitle);
            titleButton.GetComponent<Image>().color = new Color32(29, 43, 51, 255);
            titleButton.GetComponentInChildren<TMP_Text>().fontSize = 20f;

            endingPanel.SetActive(false);
        }

        private TMP_Text CreateEndingResultRow(Transform parent, string name, string label,
            Vector2 topAnchoredPosition)
        {
            GameObject row = CreatePanel(name, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), topAnchoredPosition, new Vector2(820f, 43f),
                new Color32(19, 39, 45, 235));
            CreateText("Label", row.transform, new Vector2(0f, 0f), new Vector2(0.62f, 1f),
                new Vector2(0f, 0.5f), new Vector2(16f, 0f), Vector2.zero, 18f,
                TextAlignmentOptions.MidlineLeft, new Color32(183, 199, 194, 255)).text = label;
            return CreateText("Value", row.transform, new Vector2(0.62f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(-16f, 0f), Vector2.zero, 20f,
                TextAlignmentOptions.MidlineRight, new Color32(229, 205, 143, 255));
        }

        private static void RestartGameplay()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(TitleMenuController.GameplaySceneName, LoadSceneMode.Single);
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
                bool maxLevel = level >= DayUpgradeRules.MaximumLevel;
                bool canPurchase = dayFlow.CanPurchaseUpgrade(card.Type);
                bool purchaseComplete = maxLevel || dayFlow.UpgradePurchasedThisClear;
                string currentEffect = dayFlow.GetUpgradeEffectText(card.Type, level);
                string nextEffect = maxLevel
                    ? "최대 강화 완료" : dayFlow.GetUpgradeEffectText(card.Type, level + 1);
                string purchaseStatus = maxLevel ? "최대 강화 완료"
                    : dayFlow.UpgradePurchasedThisClear ? "이번 DAY 구매 완료"
                    : inventory.ManaCrystals < price ? $"불가 (마력 조각 부족 · {price}개 필요)"
                    : canPurchase ? "구매 가능" : "현재 구매 불가";
                Color statusColor = purchaseComplete ? upgradeCompleteHighlightColor
                    : canPurchase ? upgradeAvailableHighlightColor : upgradeUnavailableHighlightColor;
                card.Title.text = $"{Colorize(GetUpgradeTitleName(card.Type), GetUpgradeTitleColor(card.Type))} 강화 "
                    + GetUpgradeStageName(Mathf.Min(level + 1, DayUpgradeRules.MaximumLevel));
                card.Description.text = $"현재 단계: {level} / {DayUpgradeRules.MaximumLevel}\n"
                    + $"현재 효과: {ColorizeUpgradeEffect(card.Type, currentEffect)}\n"
                    + $"강화 후: {ColorizeUpgradeEffect(card.Type, nextEffect)}\n"
                    + $"필요한 마력 조각: {Colorize(maxLevel ? "없음" : price + "개", upgradePriceHighlightColor)}\n"
                    + $"구매 가능 여부: {Colorize(purchaseStatus, statusColor)}";
                card.Button.interactable = canPurchase;
                card.ButtonLabel.text = maxLevel ? "최대 강화 완료"
                    : dayFlow.UpgradePurchasedThisClear ? "오늘 구매 완료"
                    : inventory.ManaCrystals < price ? $"마력 조각 부족\n{price}개 필요"
                    : $"구매 · {price}개";
                card.ButtonImage.color = canPurchase ? new Color(0.12f, 0.42f, 0.6f, 1f)
                    : new Color(0.22f, 0.25f, 0.3f, 1f);
            }
            if (upgradeResultText != null) upgradeResultText.text = upgradeResult;
        }

        private void RefreshSupplyUI()
        {
            if (dayFlow == null || supplyPanel == null) return;
            bool pending = dayFlow.DaySupplyPending && dayFlow.State == DayState.DayClear;
            bool open = pending && dayFlow.SupplyPanelOpen;
            supplyPanel.SetActive(open);
            clearPanel.SetActive(dayFlow.State == DayState.DayClear && !open);
            if (!open) return;

            supplyBalanceText.text = $"보유 마력 조각: {inventory.ManaCrystals}개";
            supplyPurchaseCountText.text = $"이번 보급 구매: {dayFlow.SupplyPurchasesThisShop} / {dayFlow.MaximumSupplyPurchases}"
                + $"   (남은 구매 {dayFlow.RemainingSupplyPurchases}회)";
            supplyOptionsRoot.SetActive(true);
            supplyTitleText.text = $"DAY {dayFlow.CurrentDay} 보급 상점";

            RefreshSupplyOption(supplyPotionButton, supplyPotionLabel, ItemType.PurificationPotion,
                "정화 물약", $"오염도 {dayFlow.NextDayPotionRecoveryAmount:F0} 회복", dayFlow.PurificationPotionPrice);
            RefreshSupplyOption(supplyRepairButton, supplyRepairLabel, ItemType.EmergencyRepairKit,
                "시설 수리 키트", $"시설 안정도 {PlayerInventory.RepairKitRecovery:F0} 회복",
                dayFlow.EmergencyRepairKitPrice);

            supplyContinueLabel.text = dayFlow.SupplyPurchasesThisShop == 0
                ? "구매하지 않고 다음 DAY" : "다음 DAY 시작";
            if (!string.IsNullOrEmpty(supplyFeedback))
                supplyMessageText.text = supplyFeedback;
            else if (dayFlow.RemainingSupplyPurchases <= 0)
                supplyMessageText.text = "이번 DAY 구매 한도에 도달했습니다. 다음 DAY로 진행할 수 있습니다.";
            else if (dayFlow.CurrentDay == 1 && !Learned(Supply))
                supplyMessageText.text = tutorialCopy.supply;
            else supplyMessageText.text = "필요한 보급품을 구매하거나, 구매하지 않고 다음 DAY로 진행하세요.";
        }

        private void RefreshSupplyOption(Button button, TMP_Text label, ItemType item,
            string itemName, string description, int price)
        {
            SupplyPurchaseBlockReason reason = dayFlow.GetSupplyPurchaseBlockReason(item);
            label.richText = true;
            label.text = $"<b>{itemName}</b>\n{description}\n가격: {price} 마력 조각"
                + $"\n보유 수량: {inventory.GetItemCount(item)}개\n{GetSupplyBlockReasonText(reason)}";
            button.interactable = reason == SupplyPurchaseBlockReason.None;
            button.GetComponent<Image>().color = button.interactable
                ? new Color(0.12f, 0.42f, 0.6f, 1f)
                : new Color(0.22f, 0.25f, 0.3f, 1f);
        }

        private static string GetSupplyBlockReasonText(SupplyPurchaseBlockReason reason)
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

        private void OnUpgradeClicked(DayUpgradeType type)
        {
            if (dayFlow == null) return;
            bool purchased = dayFlow.TryPurchaseUpgrade(type);
            upgradeResult = purchased ? $"{GetUpgradeName(type)} 강화 구매 완료"
                : "구매할 수 없습니다. 결정 수량과 오늘 구매 여부를 확인하세요.";
            RefreshUpgradeCards();
        }

        private void TryPurchaseSupply(ItemType item)
        {
            if (dayFlow == null) return;
            bool purchased = dayFlow.TryPurchaseDaySupply(item, out SupplyPurchaseBlockReason failure);
            supplyFeedback = purchased
                ? $"{(item == ItemType.PurificationPotion ? "정화 물약" : "시설 수리 키트")} 구매 완료."
                : GetSupplyPurchaseFailureMessage(failure);
            RefreshInventory();
            RefreshSupplyUI();
        }

        private static string GetSupplyPurchaseFailureMessage(SupplyPurchaseBlockReason reason)
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
            return $"{GetUpgradeTitleName(type)} 강화";
        }

        private static string GetUpgradeTitleName(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return "이동 속도";
                case DayUpgradeType.FacilityDefense: return "시설 방호";
                case DayUpgradeType.ContaminationResistance: return "오염 저항";
                default: return "강화";
            }
        }

        private Color GetUpgradeTitleColor(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return mobilityTitleHighlightColor;
                case DayUpgradeType.FacilityDefense: return facilityDefenseTitleHighlightColor;
                case DayUpgradeType.ContaminationResistance: return contaminationResistanceTitleHighlightColor;
                default: return Color.white;
            }
        }

        private Color GetUpgradeEffectColor(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return mobilityEffectHighlightColor;
                case DayUpgradeType.FacilityDefense: return facilityDefenseEffectHighlightColor;
                case DayUpgradeType.ContaminationResistance: return contaminationResistanceEffectHighlightColor;
                default: return Color.white;
            }
        }

        private string ColorizeUpgradeEffect(DayUpgradeType type, string text)
        {
            string word = GetUpgradeEffectHighlightWord(type);
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return text;
            return text.Replace(word, Colorize(word, GetUpgradeEffectColor(type)));
        }

        private string GetUpgradeEffectHighlightWord(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return mobilityEffectHighlightWord;
                case DayUpgradeType.FacilityDefense: return facilityDefenseEffectHighlightWord;
                case DayUpgradeType.ContaminationResistance: return contaminationResistanceEffectHighlightWord;
                default: return string.Empty;
            }
        }

        private static string Colorize(string text, Color color)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>{text}</color>";
        }

        private static string GetUpgradeStageName(int level)
        {
            switch (level)
            {
                case 1: return "I";
                case 2: return "II";
                default: return "III";
            }
        }

        private sealed class UpgradeCardView
        {
            public readonly DayUpgradeType Type;
            public readonly TMP_Text Title;
            public readonly TMP_Text Description;
            public readonly Button Button;
            public readonly TMP_Text ButtonLabel;
            public readonly Image ButtonImage;

            public UpgradeCardView(DayUpgradeType type, TMP_Text title, TMP_Text description, Button button,
                TMP_Text buttonLabel, Image buttonImage)
            {
                Type = type;
                Title = title;
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
