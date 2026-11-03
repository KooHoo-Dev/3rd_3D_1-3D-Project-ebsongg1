using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>플레이 중 ESC 일시정지 메뉴와 타이틀 이동 확인을 관리합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenuController : MonoBehaviour
    {
        private static readonly Color BackdropColor = new Color(0.006f, 0.012f, 0.018f, 0.58f);
        private static readonly Color PanelColor = new Color32(10, 25, 34, 238);

        private DayFlowManager dayFlow;
        private WardStatus wardStatus;
        private GameObject root;
        private GameObject menuPanel;
        private GameObject confirmationPanel;
        private bool isOpen;
        private bool isTransitioning;

        public bool IsOpen => isOpen;

        public void Configure(DayFlowManager manager, WardStatus status)
        {
            dayFlow = manager;
            wardStatus = status;
            if (root == null) BuildUI();
        }

        public void Open()
        {
            if (isOpen || isTransitioning || dayFlow == null || wardStatus == null
                || dayFlow.State != DayState.Playing || wardStatus.HasFailed) return;

            isOpen = true;
            menuPanel.SetActive(true);
            confirmationPanel.SetActive(false);
            wardStatus.PauseGameForUI();
            root.SetActive(true);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(menuPanel.transform.Find("ContinueButton").gameObject);
        }

        /// <summary>ESC를 누르면 확인창을 취소하거나 일시정지를 해제합니다.</summary>
        public void HandleEscape()
        {
            if (!isOpen) return;
            if (confirmationPanel.activeSelf)
            {
                ShowMenu();
                return;
            }
            CloseAndResume();
        }

        private void BuildUI()
        {
            EnsureInputSystemEventSystem();
            WardShiftHUDLayout layout = FindFirstObjectByType<WardShiftHUDLayout>(FindObjectsInactive.Include);
            TMP_FontAsset font = layout != null ? layout.KoreanFont : null;
            if (font == null)
            {
                TMP_Text existingText = FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
                if (existingText != null) font = existingText.font;
            }
            if (font == null) font = TMP_Settings.defaultFontAsset;

            root = new GameObject("PauseMenuCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image backdrop = CreateImage("Backdrop", root.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Vector2.zero, BackdropColor);
            backdrop.raycastTarget = true;

            menuPanel = CreatePanel("PausePanel", root.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(480f, 420f), PanelColor);
            AddPanelFrame(menuPanel);
            CreateText("Title", menuPanel.transform, font, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(-56f, 46f),
                34f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor).text = "일시정지";
            CreateImage("HeaderRule", menuPanel.transform, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), new Vector2(0f, -91f), new Vector2(-72f, 1.5f),
                WardShiftHUDLayout.RecordDividerColor);

            CreateButton("ContinueButton", menuPanel.transform, font, new Vector2(0f, 58f),
                "계속하기", CloseAndResume, false);
            CreateButton("TitleButton", menuPanel.transform, font, new Vector2(0f, -8f),
                "타이틀로 돌아가기", ShowConfirmation, true);
            CreateButton("QuitButton", menuPanel.transform, font, new Vector2(0f, -74f),
                "게임 종료", QuitGame, true);
            CreateText("EscapeHint", menuPanel.transform, font,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(240f, 24f),
                15f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordHeaderColor).text = "[ESC] 계속하기";

            confirmationPanel = CreatePanel("ReturnToTitleConfirmation", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(500f, 290f), PanelColor);
            AddPanelFrame(confirmationPanel);
            CreateText("ConfirmTitle", confirmationPanel.transform, font,
                new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
                new Vector2(0f, -38f), new Vector2(-56f, 44f), 28f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor).text = "타이틀로 돌아가기";
            TMP_Text warning = CreateText("UnsavedProgressWarning", confirmationPanel.transform, font,
                new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f),
                new Vector2(0f, -100f), new Vector2(-64f, 60f), 17f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordBodyColor);
            warning.text = "현재 DAY 진행 정보는 저장되지 않습니다.\n타이틀로 돌아가시겠습니까?";
            warning.overflowMode = TextOverflowModes.Overflow;
            CreateButton("ConfirmReturnButton", confirmationPanel.transform, font,
                new Vector2(-90f, -75f), "타이틀로 이동", ReturnToTitle, false,
                new Vector2(200f, 50f));
            CreateButton("CancelReturnButton", confirmationPanel.transform, font,
                new Vector2(115f, -75f), "취소", ShowMenu, true, new Vector2(150f, 50f));

            root.SetActive(false);
        }

        private void ShowConfirmation()
        {
            if (!isOpen || isTransitioning) return;
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            menuPanel.SetActive(false);
            confirmationPanel.SetActive(true);
            SelectButton("ConfirmReturnButton");
        }

        private void ShowMenu()
        {
            if (!isOpen) return;
            if (confirmationPanel.activeSelf)
            {
                AudioService.Play2D(WardShiftSoundId.UiButtonClick);
                confirmationPanel.SetActive(false);
                menuPanel.SetActive(true);
            }
            SelectButton("ContinueButton");
        }

        private void CloseAndResume()
        {
            if (!isOpen || isTransitioning) return;
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            isOpen = false;
            root.SetActive(false);
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
        }

        private void ReturnToTitle()
        {
            if (!isOpen || isTransitioning) return;
            isTransitioning = true;
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            isOpen = false;
            root.SetActive(false);

            // 일시정지 스냅샷을 먼저 해제해 WardStatus.OnDisable이 타이틀 커서를 다시 잠그지 않게 합니다.
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
            TitleMenuController.ReturnToTitle();
        }

        private void QuitGame()
        {
            if (!isOpen || isTransitioning) return;
            isTransitioning = true;
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            isOpen = false;
            root.SetActive(false);
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SelectButton(string name)
        {
            if (EventSystem.current == null) return;
            Transform button = (confirmationPanel.activeSelf ? confirmationPanel : menuPanel).transform.Find(name);
            if (button != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        private static void EnsureInputSystemEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                GameObject eventObject = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem = eventObject.GetComponent<EventSystem>();
                eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            else if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }

        private static Button CreateButton(string name, Transform parent, TMP_FontAsset font,
            Vector2 position, string label, UnityEngine.Events.UnityAction onClick, bool secondary,
            Vector2? size = null)
        {
            GameObject buttonObject = CreatePanel(name, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position,
                size ?? new Vector2(360f, 52f), WardShiftHUDLayout.RecordButtonColor);
            Image image = buttonObject.GetComponent<Image>();
            image.raycastTarget = true;
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(145, 143, 112, 155);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = secondary
                ? new Color32(31, 50, 54, 255) : WardShiftHUDLayout.RecordButtonColor;
            colors.highlightedColor = secondary
                ? new Color32(45, 70, 72, 255) : WardShiftHUDLayout.RecordButtonHoverColor;
            colors.pressedColor = WardShiftHUDLayout.RecordButtonPressedColor;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(onClick);

            CreateText("Label", buttonObject.transform, font, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 18f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor).text = label;
            image.color = colors.normalColor;
            return button;
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

        private static Image CreateImage(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition,
            Vector2 size, float fontSize, TextAlignmentOptions alignment, Color color)
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

        private static void AddPanelFrame(GameObject panel)
        {
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = WardShiftHUDLayout.RecordFrameColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }
    }
}
