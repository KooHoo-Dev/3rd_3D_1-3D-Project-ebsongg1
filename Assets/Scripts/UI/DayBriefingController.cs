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
        private GameObject sidePanel;
        private TMP_Text sidePanelLabel;
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

            dayLabel.text = $"DAY {record.Day:00}  /  07";
            recordTypeLabel.text = record.RecordType;
            titleLabel.text = record.Title;
            bodyLabel.text = record.Body;

            bool hasCaution = !string.IsNullOrWhiteSpace(record.Caution);
            cautionPanel.SetActive(hasCaution);
            if (hasCaution) cautionLabel.text = record.Caution;

            bool hasSidePanel = !string.IsNullOrWhiteSpace(record.SidePanelText);
            sidePanel.SetActive(hasSidePanel);
            if (hasSidePanel) sidePanelLabel.text = record.SidePanelText;

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
            backdropImage.color = new Color(0.008f, 0.014f, 0.025f, 0.12f);
            backdropImage.raycastTarget = true;

            RectTransform main = CreateFramedPanel("RecordPanel", parent,
                new Vector2(0.055f, 0.105f), new Vector2(0.49f, 0.895f));
            AddBar("TopRule", main, new Color32(93, 153, 165, 230), 28f, 26f, 3f);

            TMP_FontAsset uiFont = data.KoreanFont != null
                ? data.KoreanFont : TMP_Settings.defaultFontAsset;
            TMP_FontAsset headingFont = data.EnglishFont != null ? data.EnglishFont : uiFont;

            TMP_Text facility = CreateText("FacilityName", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -34f), new Vector2(-52f, 30f), 22f, uiFont,
                new Color32(143, 205, 211, 255), TextAlignmentOptions.TopLeft);
            facility.text = "WARD SHIFT   ·   제17 마력 제어소";

            dayLabel = CreateText("DayNumber", main, Vector2.one, Vector2.one,
                new Vector2(-26f, -34f), new Vector2(210f, 30f), 21f, headingFont,
                new Color32(218, 202, 163, 255), TextAlignmentOptions.TopRight);
            recordTypeLabel = CreateText("RecordType", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -91f), new Vector2(-52f, 28f), 21f, uiFont,
                new Color32(120, 200, 203, 255), TextAlignmentOptions.TopLeft);
            titleLabel = CreateText("RecordTitle", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -121f), new Vector2(-52f, 60f), 36f, uiFont,
                new Color32(239, 239, 232, 255), TextAlignmentOptions.TopLeft);
            AddBar("TitleRule", main, new Color32(115, 130, 137, 170), 26f, 198f, 1f);

            bodyLabel = CreateText("RecordBody", main, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -225f), new Vector2(-56f, 390f), 27f, uiFont,
                new Color32(218, 222, 224, 255), TextAlignmentOptions.TopLeft);
            bodyLabel.lineSpacing = 7f;
            bodyLabel.paragraphSpacing = 9f;

            cautionPanel = CreatePanel("Caution", main, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 112f), new Vector2(-40f, 100f),
                new Color(0.14f, 0.055f, 0.16f, 0.72f));
            AddBar("CautionAccent", cautionPanel.GetComponent<RectTransform>(),
                new Color32(166, 115, 177, 245), 0f, 0f, 3f);
            cautionLabel = CreateText("CautionText", cautionPanel.GetComponent<RectTransform>(),
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-34f, -20f),
                21f, uiFont, new Color32(227, 217, 230, 255), TextAlignmentOptions.TopLeft);
            cautionLabel.lineSpacing = 3f;

            sidePanel = CreatePanel("DailyWork", parent, new Vector2(0.735f, 0.32f),
                new Vector2(0.955f, 0.66f), Vector2.zero, Vector2.zero,
                new Color(0.025f, 0.05f, 0.072f, 0.93f));
            RectTransform sideRect = sidePanel.GetComponent<RectTransform>();
            AddBar("SideRule", sideRect, new Color32(83, 145, 158, 220), 18f, 20f, 2f);
            CreateText("DailyWorkHeading", sideRect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -34f), new Vector2(-40f, 32f), 23f, uiFont,
                new Color32(146, 207, 208, 255), TextAlignmentOptions.TopLeft).text = "오늘의 업무";
            sidePanelLabel = CreateText("DailyWorkText", sideRect, Vector2.zero, new Vector2(1f, 1f),
                new Vector2(0f, -29f), new Vector2(-40f, -96f), 20f, uiFont,
                new Color32(218, 223, 224, 255), TextAlignmentOptions.TopLeft);
            sidePanelLabel.lineSpacing = 3f;

            startButton = CreateStartButton(main, uiFont);
        }

        private Button CreateStartButton(RectTransform parent, TMP_FontAsset font)
        {
            GameObject buttonObject = CreateRect("StartShiftButton", parent,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 35f), new Vector2(320f, 64f));
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color32(35, 69, 83, 255);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = new Color32(35, 69, 83, 255),
                highlightedColor = new Color32(52, 101, 115, 255),
                pressedColor = new Color32(25, 49, 61, 255),
                selectedColor = new Color32(45, 89, 102, 255),
                disabledColor = new Color32(35, 48, 54, 180),
                colorMultiplier = 1f,
                fadeDuration = 0.12f
            };
            button.onClick.AddListener(OnStartClicked);

            TMP_Text label = CreateText("Label", buttonObject.GetComponent<RectTransform>(),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 25f, font,
                new Color32(241, 244, 241, 255), TextAlignmentOptions.Center);
            label.text = "근무 시작   ·   ENTER";
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            return button;
        }

        private void OnStartClicked()
        {
            if (!isVisible || dayFlow == null) return;
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
            frameImage.color = new Color32(97, 108, 114, 242);
            frameImage.raycastTarget = false;
            RectTransform panel = CreateRect(name, frame.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-4f, -4f)).GetComponent<RectTransform>();
            Image image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0.018f, 0.035f, 0.056f, 0.94f);
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
            text.overflowMode = TextOverflowModes.Ellipsis;
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
