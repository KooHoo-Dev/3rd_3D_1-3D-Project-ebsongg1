using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>기존 게임 화면 위에 DAY 운영 기록을 표시하고 근무 시작을 요청합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayBriefingController : MonoBehaviour
    {
        private const string DataResourcePath = "WardShift/DayBriefingData";

        private DayFlowManager dayFlow;
        private DayBriefingData data;
        private GameObject root;
        private TMP_Text dayLabel;
        private TMP_Text recordTypeLabel;
        private TMP_Text titleLabel;
        private TMP_Text bodyLabel;
        private GameObject cautionPanel;
        private TMP_Text cautionLabel;
        private GameObject memoShadow;
        private GameObject memoPanel;
        private TMP_Text memoLabel;
        private Button startButton;
        private bool isVisible;

        public void Configure(DayFlowManager manager)
        {
            dayFlow = manager;
            if (data == null) data = Resources.Load<DayBriefingData>(DataResourcePath);
        }

        /// <summary>새 화면이 필요할 때 한 번 생성하고, 이후 DAY에서는 같은 UI를 재사용합니다.</summary>
        public bool Show(int day)
        {
            if (isVisible || dayFlow == null) return false;
            if (data == null) data = Resources.Load<DayBriefingData>(DataResourcePath);
            DayBriefingData.DayRecord record = data != null ? data.GetRecord(day) : null;
            if (record == null)
            {
                Debug.LogError($"WARD SHIFT: DAY {day} 브리핑 기록을 찾을 수 없습니다. "
                    + $"Resources/{DataResourcePath} 에셋을 확인하세요.", this);
                return false;
            }
            if (!EnsureUi()) return false;

            if (data.KoreanFont != null)
            {
                string requiredCharacters = record.RecordType + record.Title + record.Body
                    + record.Caution + record.HandoffMemo
                    + "제17 마력 제어소 주간 담당자 인수인계 근무 시작 근무 주의사항 근무 인계 완료"
                    + "DAY 0123456789/";
                if (!data.KoreanFont.TryAddCharacters(requiredCharacters, out string missingCharacters)
                    && !string.IsNullOrEmpty(missingCharacters))
                    Debug.LogWarning($"WARD SHIFT: 브리핑 폰트에 글리프가 없습니다: {missingCharacters}", this);
            }

            dayLabel.text = $"DAY {record.Day:00}  /  07";
            recordTypeLabel.text = record.RecordType;
            titleLabel.text = record.Title;
            bodyLabel.text = record.Body;

            bool hasCaution = !string.IsNullOrWhiteSpace(record.Caution);
            cautionPanel.SetActive(hasCaution);
            if (hasCaution) cautionLabel.text = record.Caution;

            bool hasMemo = !string.IsNullOrWhiteSpace(record.HandoffMemo);
            memoLabel.text = hasMemo ? record.HandoffMemo : string.Empty;
            memoShadow.SetActive(hasMemo);
            memoPanel.SetActive(hasMemo);

            isVisible = true;
            root.SetActive(true);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(startButton.gameObject);
            return true;
        }

        private bool EnsureUi()
        {
            if (root != null) return true;

            EnsureInputSystemEventSystem();
            root = new GameObject("DayStoryBriefingCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

            BuildUi(rootRect);
            root.SetActive(false);
            return true;
        }

        private void BuildUi(RectTransform parent)
        {
            // 희미한 클릭 차단층을 사용해 책상 배경을 남기고, 뒤쪽 게임 UI로 입력이 넘어가지 않게 합니다.
            GameObject backdrop = CreateRect("Backdrop", parent, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = new Color(0.008f, 0.014f, 0.025f, 0.055f);
            backdropImage.raycastTarget = true;

            RectTransform main = CreateFramedPanel("RecordPanel", parent,
                new Vector2(0.045f, 0.09f), new Vector2(0.455f, 0.91f));
            AddBar("TopRule", main, new Color32(91, 137, 142, 220), 28f, 24f, 2f);

            TMP_FontAsset uiFont = data.KoreanFont != null
                ? data.KoreanFont : TMP_Settings.defaultFontAsset;
            TMP_FontAsset headingFont = uiFont;

            TMP_Text facility = CreateText("FacilityName", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -32f), new Vector2(-52f, 30f), 21f, uiFont,
                new Color32(145, 181, 179, 255), TextAlignmentOptions.TopLeft);
            facility.text = "제17 마력 제어소";

            dayLabel = CreateText("DayNumber", main, Vector2.one, Vector2.one,
                new Vector2(-26f, -32f), new Vector2(210f, 30f), 20f, headingFont,
                new Color32(191, 181, 155, 255), TextAlignmentOptions.TopRight);
            recordTypeLabel = CreateText("RecordType", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -82f), new Vector2(-52f, 28f), 18f, uiFont,
                new Color32(132, 177, 176, 255), TextAlignmentOptions.TopLeft);
            titleLabel = CreateText("RecordTitle", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -116f), new Vector2(-52f, 60f), 31f, uiFont,
                new Color32(240, 237, 230, 255), TextAlignmentOptions.TopLeft);
            titleLabel.enableAutoSizing = true;
            titleLabel.fontSizeMin = 26f;
            titleLabel.fontSizeMax = 31f;
            AddBar("TitleRule", main, new Color32(86, 101, 104, 180), 26f, 188f, 1f);
            AddBottomShortRule("RecordFooterRule", main,
                new Color32(86, 101, 104, 180), 104f, 128f);

            bodyLabel = CreateText("RecordBody", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -214f), new Vector2(-56f, 425f), 23f, uiFont,
                new Color32(224, 227, 223, 255), TextAlignmentOptions.TopLeft);
            bodyLabel.enableAutoSizing = true;
            bodyLabel.fontSizeMin = 21f;
            bodyLabel.fontSizeMax = 23f;
            bodyLabel.lineSpacing = 5f;
            bodyLabel.paragraphSpacing = 10f;

            cautionPanel = CreatePanel("Caution", main, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 128f), new Vector2(-40f, 132f),
                new Color32(40, 45, 44, 245));
            TMP_Text cautionHeading = CreateText("CautionHeading",
                cautionPanel.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -10f), new Vector2(-30f, 24f), 17f, uiFont,
                new Color32(202, 181, 143, 255), TextAlignmentOptions.TopLeft);
            cautionHeading.text = "근무 주의사항";
            cautionLabel = CreateText("CautionText", cautionPanel.GetComponent<RectTransform>(),
                Vector2.zero, Vector2.one, new Vector2(0f, -17f), new Vector2(-30f, -54f),
                17f, uiFont, new Color32(224, 227, 223, 255), TextAlignmentOptions.TopLeft);
            cautionLabel.enableAutoSizing = true;
            cautionLabel.fontSizeMin = 16f;
            cautionLabel.fontSizeMax = 17f;
            cautionLabel.lineSpacing = 2f;

            BuildHandoffMemo(parent, uiFont);

            startButton = CreateStartButton(main, uiFont);
        }

        private void BuildHandoffMemo(RectTransform parent, TMP_FontAsset font)
        {
            Vector2 anchorMin = new Vector2(0.70f, 0.17f);
            Vector2 anchorMax = new Vector2(0.94f, 0.75f);

            memoShadow = CreateRect("HandoffMemoShadow", parent, anchorMin, anchorMax,
                new Vector2(9f, -10f), Vector2.zero);
            Image shadowImage = memoShadow.AddComponent<Image>();
            shadowImage.color = new Color(0.025f, 0.022f, 0.018f, 0.34f);
            shadowImage.raycastTarget = false;

            memoPanel = CreateRect("HandoffMemo", parent, anchorMin, anchorMax,
                Vector2.zero, Vector2.zero);
            Image frame = memoPanel.AddComponent<Image>();
            frame.color = new Color32(160, 148, 123, 255);
            frame.raycastTarget = false;

            RectTransform paper = CreateRect("Paper", memoPanel.transform, Vector2.zero,
                Vector2.one, Vector2.zero, new Vector2(-4f, -4f)).GetComponent<RectTransform>();
            Image paperImage = paper.gameObject.AddComponent<Image>();
            paperImage.color = new Color32(217, 208, 185, 250);
            paperImage.raycastTarget = false;

            GameObject edge = CreateRect("PaperEdge", paper, Vector2.zero,
                new Vector2(0f, 1f), new Vector2(16f, 0f), new Vector2(2f, -34f));
            Image edgeImage = edge.AddComponent<Image>();
            edgeImage.color = new Color32(170, 157, 133, 180);
            edgeImage.raycastTarget = false;

            CreateText("HandoffHeading", paper, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -24f), new Vector2(-48f, 30f), 19f, font,
                new Color32(118, 108, 88, 255), TextAlignmentOptions.TopLeft).text =
                "주간 담당자 · 인수인계";
            AddBar("MemoHeaderRule", paper, new Color32(170, 157, 133, 210), 24f, 62f, 1f);

            memoLabel = CreateText("HandoffText", paper, Vector2.zero, Vector2.one,
                new Vector2(0f, -8f), new Vector2(-48f, -132f), 21f, font,
                new Color32(62, 58, 50, 255), TextAlignmentOptions.TopLeft);
            memoLabel.enableAutoSizing = true;
            memoLabel.fontSizeMin = 18f;
            memoLabel.fontSizeMax = 21f;
            memoLabel.lineSpacing = 5f;
            memoLabel.paragraphSpacing = 8f;

            AddMemoBottomRule("MemoFooterRule", paper, new Color32(170, 157, 133, 210));
            CreateText("HandoffFooter", paper, new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-24f, 18f),
                new Vector2(180f, 24f), 14f, font,
                new Color32(118, 108, 88, 255), TextAlignmentOptions.BottomRight).text =
                "근무 인계 완료";
        }

        private Button CreateStartButton(RectTransform parent, TMP_FontAsset font)
        {
            GameObject buttonObject = CreateRect("StartShiftButton", parent,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 30f), new Vector2(300f, 56f));
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color32(38, 64, 69, 255);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = new Color32(38, 64, 69, 255),
                highlightedColor = new Color32(53, 84, 88, 255),
                pressedColor = new Color32(27, 46, 49, 255),
                selectedColor = new Color32(47, 75, 79, 255),
                disabledColor = new Color32(35, 48, 54, 180),
                colorMultiplier = 1f,
                fadeDuration = 0.12f
            };
            button.onClick.AddListener(OnStartClicked);

            TMP_Text label = CreateText("Label", buttonObject.GetComponent<RectTransform>(),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 25f, font,
                new Color32(241, 244, 241, 255), TextAlignmentOptions.Center);
            label.text = "근무 시작";
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            return button;
        }

        private void OnStartClicked()
        {
            if (!isVisible || dayFlow == null) return;
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            isVisible = false;
            root.SetActive(false);
            dayFlow.StartPreparedDayFromBriefing();
        }

        private void Update()
        {
            if (!isVisible || Keyboard.current == null) return;
            if (Keyboard.current.enterKey.wasPressedThisFrame
                || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
                OnStartClicked();
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
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }

        private static RectTransform CreateFramedPanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject frame = CreateRect(name + "Frame", parent, anchorMin, anchorMax,
                Vector2.zero, Vector2.zero);
            Image frameImage = frame.AddComponent<Image>();
            frameImage.color = new Color32(55, 67, 72, 242);
            frameImage.raycastTarget = false;
            RectTransform panel = CreateRect(name, frame.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-4f, -4f)).GetComponent<RectTransform>();
            Image image = panel.gameObject.AddComponent<Image>();
            image.color = new Color32(23, 33, 38, 246);
            image.raycastTarget = true;
            return panel;
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, Vector2 size, Color color)
        {
            GameObject panel = CreateRect(name, parent, anchorMin, anchorMax, position, size);
            Image image = panel.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return panel;
        }

        private static void AddBar(string name, RectTransform parent, Color color,
            float horizontalInset, float topInset, float height)
        {
            GameObject bar = CreateRect(name, parent, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -topInset),
                new Vector2(-horizontalInset * 2f, height));
            bar.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            Image image = bar.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void AddMemoBottomRule(string name, RectTransform parent, Color color)
        {
            GameObject rule = CreateRect(name, parent, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(0f, 48f), new Vector2(-48f, 1f));
            Image image = rule.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void AddBottomShortRule(string name, RectTransform parent, Color color,
            float bottomInset, float width)
        {
            GameObject rule = CreateRect(name, parent, new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, bottomInset), new Vector2(width, 1f));
            Image image = rule.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static TMP_Text CreateText(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, Vector2 size, float fontSize, TMP_FontAsset font,
            Color color, TextAlignmentOptions alignment)
        {
            GameObject textObject = CreateRect(name, parent, anchorMin, anchorMax, position, size);
            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = font != null ? font : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            RectTransform rect = result.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x == anchorMax.x ? anchorMin.x : 0.5f,
                anchorMin.y == anchorMax.y ? anchorMin.y : 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return result;
        }
    }
}
