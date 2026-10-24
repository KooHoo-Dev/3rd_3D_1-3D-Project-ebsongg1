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
        private GameObject inventoryPanel;
        private GameObject clearPanel;
        private GameObject failedPanel;
        private GameObject endingPanel;
        private TMP_Text clearSummaryText;
        private TMP_Text failedSummaryText;
        private TMP_Text endingSummaryText;
        private Color emptyColor = new Color(0.2f, 0.23f, 0.28f, 0.94f);
        private Color potionColor = new Color(0.55f, 0.25f, 0.95f, 1f);
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
            if (dayFlow == null) return;
            dayStatusText.text = $"근무 {dayFlow.CurrentDay}일차\n남은 시간  {FormatTime(dayFlow.RemainingSeconds)}\n"
                + $"마력 결정  {inventory.ManaCrystals}개";
            pickupPromptText.text = dayFlow.PickupInteractor != null
                && dayFlow.PickupInteractor.CurrentPickup != null && !dayFlow.InventoryOpen
                ? "[E] 정화약 획득" : "";
            inventoryPanel.SetActive(dayFlow.State == DayState.Playing && dayFlow.InventoryOpen);
            clearPanel.SetActive(dayFlow.State == DayState.DayClear);
            failedPanel.SetActive(dayFlow.State == DayState.DayFailed);
            if (endingPanel != null) endingPanel.SetActive(dayFlow.State == DayState.Ending);
            if (clearSummaryText != null)
                clearSummaryText.text = $"{dayFlow.CurrentDay}일차 근무 완료\n"
                    + $"이번 근무 획득 마력 결정: {inventory.CrystalsEarnedToday}개\n"
                    + $"보유 마력 결정: {inventory.ManaCrystals}개";
            if (failedSummaryText != null)
                failedSummaryText.text = $"{dayFlow.CurrentDay}일차\n"
                    + (dayFlow.Status != null && !string.IsNullOrEmpty(dayFlow.Status.FailureReason)
                        ? dayFlow.Status.FailureReason : "시설 관리 실패")
                    + "\nDAY 시작 시점의 자원과 픽업을 복원해 재도전합니다.";
            if (endingSummaryText != null)
                endingSummaryText.text = $"총 {dayFlow.FinalDay}일 근무를 모두 마쳤습니다.\n"
                    + $"마력 결정: {inventory.ManaCrystals}개\n시설 안정도: {dayFlow.Status.Stability:F0}";
            RefreshInventory();
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
                new Vector2(350f, 130f), 28, TextAlignmentOptions.TopRight, Color.white);
            pickupPromptText = CreateText("PickupPrompt", canvasObject.transform, new Vector2(0.5f, 0.12f),
                new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(500f, 55f), 28, TextAlignmentOptions.Center, Color.white);

            BuildInventory(canvasObject.transform);
            clearPanel = BuildOverlay(canvasObject.transform, "DayClearPanel", "근무 완료", out clearSummaryText,
                "다음 날 시작", () => { if (dayFlow != null) dayFlow.BeginNextDay(); }, false);
            failedPanel = BuildOverlay(canvasObject.transform, "DayFailedPanel", "근무 실패", out failedSummaryText,
                "같은 날 재도전", () => { if (dayFlow != null) dayFlow.RetryCurrentDay(); }, false);
            endingPanel = BuildOverlay(canvasObject.transform, "DayEndingPanel", "WARD SHIFT 완료", out endingSummaryText,
                "게임 종료", () => Application.Quit(), false);
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
                        inventory.TryUseItem(slotIndex, dayFlow.Player);
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
                .text = "아이템을 클릭하면 사용합니다 · 오염도가 있을 때 정화약이 소모됩니다";
            inventoryPanel.SetActive(false);
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
                slotTexts[i].text = potion ? "정화약\n오염도 30 회복" : "빈 슬롯";
                slotCounts[i].text = potion ? $"x{quantity}" : "";
                slotIcons[i].color = potion ? potionColor : new Color(0.25f, 0.28f, 0.33f, 0.9f);
                slotButtons[i].interactable = potion;
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
