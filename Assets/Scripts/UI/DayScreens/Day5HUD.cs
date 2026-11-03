using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>DAY 표시와 결과·상점 화면을 조정하고 인벤토리·튜토리얼 화면을 연결합니다.</summary>
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
        
        private InventoryUIController inventoryView;
        private UpgradeUIController upgradeView;
        
        private GameObject inventoryPanel;
        private GameObject clearPanel;
        private GameObject failedPanel;
        private GameObject endingPanel;
        private GameObject supplyPanel;
        
        private RectTransform overlayRoot;
        
        private SupplyShopUIController supplyView;
        
        private TMP_Text dayStatusText;
        private TMP_Text pickupPromptText;
        private TMP_Text clearHeaderText;
        private TMP_Text clearSummaryText;
        private TMP_Text failedDayText;
        private TMP_Text failedReasonText;
        private TMP_Text failedSummaryText;
        private TMP_Text endingSummaryText;
        private TMP_Text endingDayValueText;
        private TMP_Text endingStabilityValueText;
        private TMP_Text endingContaminationValueText;
        private TMP_Text endingManaValueText;
        private TMP_Text anomalyResolutionFeedbackText;
        
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
            inventoryView?.Bind(inventory, dayFlow);
            supplyView?.Bind(dayFlow, inventory);
            upgradeView?.Bind(dayFlow, inventory);
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

        private void OnDestroy()
        {
            inventoryView?.Dispose();
            tutorial?.Dispose();
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
                ? (dayFlow.PickupInteractor.CurrentPickup) 
                : null;
            
            pickupPromptText.text = pickup != null && (dayFlow.InventoryOpen == false) && !dayFlow.SupplyPanelOpen
                && !dayFlow.IsMapOpen && dayFlow.State == DayState.Playing
                ? $"[E] {GetItemName(pickup.Item)} 획득" 
                : "";
            
            inventoryView?.SetVisible(dayFlow.State == DayState.Playing && dayFlow.InventoryOpen);
            
            if (anomalyResolutionFeedbackText != null)
                anomalyResolutionFeedbackText.gameObject.SetActive(dayFlow.State == DayState.Playing
                    && Time.unscaledTime < anomalyResolutionFeedbackUntil);
            
            clearPanel.SetActive(dayFlow.State == DayState.DayClear && !dayFlow.SupplyPanelOpen);
            failedPanel.SetActive(dayFlow.State == DayState.DayFailed);
            
            if (endingPanel != null) endingPanel.SetActive(dayFlow.State == DayState.Ending);
            if (dayFlow.State == DayState.DayClear && lastDisplayedClearDay != dayFlow.CurrentDay)
            {
                lastDisplayedClearDay = dayFlow.CurrentDay;
                upgradeView?.ClearFeedback();
                supplyView?.ClearFeedback();
            }
            
            RefreshSupplyUI();
            
            if (clearSummaryText != null)
            {
                if (clearHeaderText != null)
                    clearHeaderText.text = $"제17 마력 제어소  ·  DAY {dayFlow.CurrentDay} 성장 기록";
                
                clearSummaryText.text = $"이번 DAY 획득 <color=#E8C26E>+{inventory.CrystalsEarnedToday}</color> 마력 조각"
                    + $"   ·   현재 보유 <color=#E8C26E>{inventory.ManaCrystals}개</color>\n"
                    + (dayFlow.UpgradePurchasedThisClear
                        ? "이번 근무 강화 구매 완료 · 추가 구매 불가"
                        : dayFlow.CurrentDay == 1 && tutorial != null && !tutorial.Learned(DayTutorialController.Clear)
                            ? tutorialCopy.firstClear
                            : "강화는 이번 근무에 한 종류만 구매할 수 있습니다.");
            }
            
            if (failedDayText != null)
                failedDayText.text = $"DAY {dayFlow.CurrentDay:00} / {dayFlow.FinalDay:00}";
            if (failedReasonText != null)
                failedReasonText.text = GetFailureReasonText(dayFlow);
            if (failedSummaryText != null)
                failedSummaryText.text = "재도전하면 DAY 시작 시점의 보유 자원과 픽업 상태를 복원해 같은 날을 다시 시작합니다."
                    + (tutorial != null && tutorial.ShowRuptureOnFailure
                        ? "\n" + tutorialCopy.rupture
                        : "");
            
            if (endingDayValueText != null) endingDayValueText.text = $"{dayFlow.CurrentDay}일";
            if (endingStabilityValueText != null) endingStabilityValueText.text = dayFlow.Status != null
                    ? $"{dayFlow.Status.Stability:F0}%" 
                    : "-";
            if (endingContaminationValueText != null) endingContaminationValueText.text = dayFlow.Player != null
                    ? $"{dayFlow.Player.Value:F0}%" 
                    : "-";
            if (endingManaValueText != null) endingManaValueText.text = $"{inventory.ManaCrystals}개";
            
            upgradeView?.Refresh();
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
            failedPanel = BuildFailurePanel(canvasObject.transform, out failedDayText,
                out failedReasonText, out failedSummaryText);
            BuildEndingPanel(canvasObject.transform);
            BuildSupplyPanel(canvasObject.transform);
            clearPanel.SetActive(false);
            failedPanel.SetActive(false);
        }

        private void BuildInventory(Transform parent)
        {
            inventoryView = new InventoryUIController(parent, koreanFont, inventory, dayFlow);
            inventoryPanel = inventoryView.Panel;
            CreateActionButton("ResetTutorial", inventoryPanel.transform, new Vector2(0f, 67f),
                new Vector2(300f, 36f), "튜토리얼 초기화", ResetTutorialProgress, true,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f))
                .GetComponentInChildren<TMP_Text>(true).fontSize = 17f;
        }

        private void BuildClearPanel(Transform parent)
        {
            clearPanel = CreatePanel("DayClearPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(1160f, 720f), WardShiftHUDLayout.RecordPanelColor);
            Outline outline = clearPanel.AddComponent<Outline>();
            outline.effectColor = WardShiftHUDLayout.RecordFrameColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            AddRecordPanelOrnaments(clearPanel);
            clearHeaderText = CreateText("FacilityDayHeader", clearPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -23f), new Vector2(-60f, 24f), 16f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordHeaderColor);
            CreatePanel("HeaderRule", clearPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -41f), new Vector2(-72f, 1.5f),
                WardShiftHUDLayout.RecordDividerColor).GetComponent<Image>().raycastTarget = false;
            CreateText("Title", clearPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -49f), new Vector2(-50f, 54f),
                38, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor).text = "근무 완료 · 강화 선택";
            clearSummaryText = CreateText("Summary", clearPanel.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f),
                new Vector2(-60f, 68f), 18, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordBodyColor);
            clearSummaryText.richText = true;
            upgradeView = new UpgradeUIController(clearPanel.transform, koreanFont, dayFlow, inventory,
                new UpgradeUIController.Style
                {
                    MobilityTitle = mobilityTitleHighlightColor,
                    FacilityDefenseTitle = facilityDefenseTitleHighlightColor,
                    ContaminationResistanceTitle = contaminationResistanceTitleHighlightColor,
                    MobilityEffectWord = mobilityEffectHighlightWord,
                    MobilityEffect = mobilityEffectHighlightColor,
                    FacilityDefenseEffectWord = facilityDefenseEffectHighlightWord,
                    FacilityDefenseEffect = facilityDefenseEffectHighlightColor,
                    ContaminationResistanceEffectWord = contaminationResistanceEffectHighlightWord,
                    ContaminationResistanceEffect = contaminationResistanceEffectHighlightColor,
                    Price = upgradePriceHighlightColor,
                    Available = upgradeAvailableHighlightColor,
                    Unavailable = upgradeUnavailableHighlightColor,
                    Complete = upgradeCompleteHighlightColor
                });
            Button skipUpgradeButton = CreateActionButton("OpenSupplyShop", clearPanel.transform, new Vector2(0f, -321f),
                new Vector2(360f, 50f), "강화 건너뛰고 보급 상점",
                () => { if (dayFlow != null) dayFlow.OpenDaySupplyShop(); });
            ApplyRecordButtonStyle(skipUpgradeButton, true);
            TMP_Text skipLabel = skipUpgradeButton.GetComponentInChildren<TMP_Text>(true);
            skipLabel.fontSize = 18f;
            skipLabel.color = new Color32(192, 204, 197, 255);
            clearPanel.SetActive(false);
        }

        private static void AddRecordPanelOrnaments(GameObject panel)
        {
            AddRecordCorner(panel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -14f));
            AddRecordCorner(panel.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f));
            AddRecordCorner(panel.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 14f));
            AddRecordCorner(panel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f));
        }

        private static void AddRecordCorner(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            GameObject horizontal = CreatePanel("FrameMarkH", parent, anchor, anchor,
                pivot, position, new Vector2(18f, 1.5f), WardShiftHUDLayout.RecordFrameColor);
            GameObject vertical = CreatePanel("FrameMarkV", parent, anchor, anchor,
                pivot, position, new Vector2(1.5f, 18f), WardShiftHUDLayout.RecordFrameColor);
            horizontal.GetComponent<Image>().raycastTarget = false;
            vertical.GetComponent<Image>().raycastTarget = false;
        }

        private static void ApplyRecordButtonStyle(Button button, bool secondary)
        {
            if (button == null) return;
            ColorBlock colors = button.colors;
            colors.normalColor = secondary ? new Color32(31, 50, 54, 255) : WardShiftHUDLayout.RecordButtonColor;
            colors.highlightedColor = secondary ? new Color32(45, 70, 72, 255) : WardShiftHUDLayout.RecordButtonHoverColor;
            colors.pressedColor = WardShiftHUDLayout.RecordButtonPressedColor;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic.color = colors.normalColor;
        }

        private void BuildSupplyPanel(Transform parent)
        {
            supplyView = new SupplyShopUIController(parent, koreanFont, dayFlow, inventory);
            supplyPanel = supplyView.Panel;
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
            button.onClick.AddListener(() => AudioService.Play2D(WardShiftSoundId.UiButtonClick));
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

        private GameObject BuildFailurePanel(Transform parent, out TMP_Text dayLabel,
            out TMP_Text reasonText, out TMP_Text retryInfoText)
        {
            GameObject panel = CreatePanel("DayFailedPanel", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(860f, 520f), WardShiftHUDLayout.RecordPanelColor);
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = WardShiftHUDLayout.RecordFrameColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            AddRecordPanelOrnaments(panel);

            CreatePanel("TopAccent", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(-28f, 2f),
                new Color32(180, 160, 108, 235));
            CreateText("FacilityLabel", panel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(34f, -30f), new Vector2(310f, 26f),
                16f, TextAlignmentOptions.MidlineLeft, WardShiftHUDLayout.RecordHeaderColor).text =
                "제17 마력 제어소";
            dayLabel = CreateText("DayLabel", panel.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-34f, -30f), new Vector2(310f, 26f),
                16f, TextAlignmentOptions.MidlineRight, WardShiftHUDLayout.RecordHeaderColor);
            CreatePanel("HeaderDivider", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(-68f, 1f),
                WardShiftHUDLayout.RecordDividerColor);

            CreateText("Title", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -94f), new Vector2(-72f, 62f),
                40f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor).text = "근무 실패";
            reasonText = CreateText("FailureReason", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -163f), new Vector2(-88f, 42f), 23f,
                TextAlignmentOptions.Center, new Color32(218, 151, 126, 255));
            retryInfoText = CreateText("RetryInfo", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -232f), new Vector2(-104f, 88f), 18f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordBodyColor);
            retryInfoText.lineSpacing = 3f;

            CreatePanel("FooterDivider", panel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 126f), new Vector2(-68f, 1f),
                WardShiftHUDLayout.RecordDividerColor);
            Button retryButton = CreateActionButton("ActionButton", panel.transform,
                new Vector2(-170f, 46f), new Vector2(330f, 58f), "같은 날 재도전",
                () => { if (dayFlow != null) dayFlow.RetryCurrentDay(); }, true,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            ApplyRecordButtonStyle(retryButton, false);
            retryButton.GetComponentInChildren<TMP_Text>(true).fontSize = 18f;

            Button titleButton = CreateActionButton("TitleButton", panel.transform,
                new Vector2(170f, 46f), new Vector2(330f, 58f), "타이틀로 돌아가기",
                TitleMenuController.ReturnToTitle, true,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            ApplyRecordButtonStyle(titleButton, true);
            TMP_Text titleButtonLabel = titleButton.GetComponentInChildren<TMP_Text>(true);
            titleButtonLabel.fontSize = 18f;
            titleButtonLabel.color = new Color32(205, 212, 205, 255);

            panel.SetActive(false);
            return panel;
        }

        private static string GetFailureReasonText(DayFlowManager manager)
        {
            WardStatus status = manager != null ? manager.Status : null;
            string failureReason = status != null ? status.FailureReason : null;
            if (failureReason == "시설 안정도 0")
                return "시설 안정도가 0에 도달했습니다.";
            if (failureReason == "마력 오염도 100")
                return "플레이어 오염도가 한계에 도달했습니다.";
            if (!string.IsNullOrWhiteSpace(failureReason)) return failureReason;
            if (status != null && status.Stability <= 0f)
                return "시설 안정도가 0에 도달했습니다.";
            if (status != null && status.Player != null && status.Player.Value >= 100f)
                return "플레이어 오염도가 한계에 도달했습니다.";
            return "실패 사유를 확인할 수 없습니다.";
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

        private void RefreshSupplyUI()
        {
            if (dayFlow == null || supplyView == null) return;
            bool pending = dayFlow.DaySupplyPending && dayFlow.State == DayState.DayClear;
            bool open = pending && dayFlow.SupplyPanelOpen;
            string tutorialHint = dayFlow.CurrentDay == 1 && tutorial != null
                && !tutorial.Learned(DayTutorialController.Supply) ? tutorialCopy.supply : null;
            supplyView.SetVisible(open, tutorialHint);
            clearPanel.SetActive(dayFlow.State == DayState.DayClear && !open);
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
