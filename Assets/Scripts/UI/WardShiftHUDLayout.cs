using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>기존 HUD Canvas에 표시 영역을 만들고 기존 presenter에 연결합니다. 게임 입력/규칙은 소유하지 않습니다.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    [DefaultExecutionOrder(10010)]
    public sealed class WardShiftHUDLayout : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset koreanFont;
        [SerializeField] private DayFlowManager dayFlow;
        [SerializeField] private MagicCaster caster;
        [SerializeField] private Sprite gaugeSprite;
        [Header("중앙 조준점")]
        [Tooltip("Crosshair Image의 가로·세로 크기만 변경합니다. 기존 6px의 1.4배는 8.4px입니다.")]
        [SerializeField, Min(1f)] private float crosshairSize = 8.4f;
        [SerializeField] private Color crosshairOutlineColor = new Color(0.08f, 0.04f, 0.16f, 0.8f);
        [Header("오염도 증가 피드백")]
        [Tooltip("장치 공격처럼 실제 직접 피격으로 오염도가 증가했을 때만 재생합니다. 지속 노출에는 재생하지 않습니다.")]
        [FormerlySerializedAs("deviceHitSfx"), SerializeField] private AudioClip contaminationIncreaseSfx;
        [SerializeField] private RectTransform hudRoot;
        [SerializeField] private TMP_Text dayStatusText;
        [SerializeField] private TMP_Text floodText;
        [SerializeField] private TMP_Text pickupText;
        [SerializeField] private Image[] magicCards = new Image[3];
        [SerializeField] private TMP_Text[] magicLabels = new TMP_Text[3];
        [SerializeField] private TMP_Text[] itemLabels = new TMP_Text[PlayerInventory.SlotCount];
        [SerializeField] private Image[] itemAccents = new Image[PlayerInventory.SlotCount];

        public Canvas Canvas => GetComponent<Canvas>();
        public TMP_Text DayStatusText => dayStatusText;
        public TMP_Text PickupText => pickupText;
        public RectTransform HUDRoot => hudRoot;
        public static readonly Color PanelColor = new Color(0.025f, 0.035f, 0.052f, 0.91f);
        public static readonly Color Muted = new Color(0.72f, 0.77f, 0.82f);
        public static readonly Color Gold = new Color(0.91f, 0.76f, 0.43f);
        public static readonly Color StabilityColor = new Color(0.29f, 0.77f, 0.93f);
        public static readonly Color ContaminationColor = new Color(0.73f, 0.4f, 0.98f);

        public static WardShiftHUDLayout Ensure(TMP_FontAsset font = null)
        {
            WardShiftHUDLayout layout = FindFirstObjectByType<WardShiftHUDLayout>(FindObjectsInactive.Include);
            if (layout == null)
            {
                MagicHUD magic = FindFirstObjectByType<MagicHUD>(FindObjectsInactive.Include);
                Canvas canvas = magic != null ? magic.GetComponentInParent<Canvas>() : null;
                if (canvas == null) return null;
                layout = canvas.gameObject.AddComponent<WardShiftHUDLayout>();
            }
            layout.Build(font);
            return layout;
        }

        private void Awake() => Build();

        /// <summary>편집기 Setup과 런타임이 같은 생성/연결 경로를 사용합니다. 반복 호출해도 중복 생성하지 않습니다.</summary>
        public void Build(TMP_FontAsset font = null)
        {
            if (font != null) koreanFont = font;
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (caster == null) caster = FindFirstObjectByType<MagicCaster>();
            if (gaugeSprite == null)
                foreach (Image image in GetComponentsInChildren<Image>(true))
                    if (image.type == Image.Type.Filled && image.sprite != null) { gaugeSprite = image.sprite; break; }
            if (hudRoot != null)
            {
                ConfigureCrosshair();
                ConfigureHitFeedback();
                foreach (Image image in hudRoot.GetComponentsInChildren<Image>(true))
                    if (image.type == Image.Type.Filled && image.sprite == null) image.sprite = gaugeSprite;
                ApplyReadableMetrics();
                return;
            }
            MagicHUD magicHUD = FindFirstObjectByType<MagicHUD>(FindObjectsInactive.Include);
            WardStatusHUD wardHUD = FindFirstObjectByType<WardStatusHUD>(FindObjectsInactive.Include);
            if (koreanFont == null && magicHUD != null)
            {
                TMP_Text oldText = magicHUD.GetComponentInChildren<TMP_Text>(true);
                if (oldText != null) koreanFont = oldText.font;
            }
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 0; // 기존 지도(60), 핑(50)보다 아래. 모달에서는 HUD 자체를 숨깁니다.
            transform.localScale = Vector3.one;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            // 구형 presenter 컴포넌트와 직렬화된 연결은 유지하고 구형 Graphic만 끕니다.
            // 조준점은 기존 오브젝트를 그대로 재사용합니다.
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
                if (graphic.gameObject.name != "Crosshair") graphic.enabled = false;
            hudRoot = Rect("HUD", transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            hudRoot.anchorMax = Vector2.one;
            hudRoot.offsetMin = hudRoot.offsetMax = Vector2.zero;
            Transform crosshair = FindDescendant(transform, "Crosshair");
            if (crosshair != null) crosshair.SetParent(hudRoot, false);
            ConfigureCrosshair();

            BuildStatus(wardHUD);
            BuildCast(magicHUD);
            BuildDay();
            BuildLoadout();
            RectTransform controls = Panel("Controls", hudRoot, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-32, 32), new Vector2(262, 200));
            Text("Keys", controls, new Vector2(16, -12), new Vector2(230, 176), 21,
                "WASD 이동 · 마우스 시점\n마법 선택: 1/2/3 · 휠", Muted);
            pickupText = Text("PickupPrompt", hudRoot, Vector2.zero, new Vector2(600, 42), 25, "", Color.white);
            Place(pickupText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 208), new Vector2(600, 42));
            ApplyReadableMetrics();
            ConfigureHitFeedback();
        }

        private void ConfigureHitFeedback()
        {
            ContaminationHitFeedback feedback = GetComponent<ContaminationHitFeedback>();
            if (feedback == null) feedback = gameObject.AddComponent<ContaminationHitFeedback>();
            feedback.Configure(this, contaminationIncreaseSfx);
        }

        private void ConfigureCrosshair()
        {
            Transform crosshair = FindDescendant(transform, "Crosshair");
            if (crosshair == null || !(crosshair is RectTransform rect)) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * Mathf.Max(1f, crosshairSize);
            Image image = crosshair.GetComponent<Image>();
            if (image == null) return;
            image.raycastTarget = false;
            Outline outline = crosshair.GetComponent<Outline>();
            if (outline == null) outline = crosshair.gameObject.AddComponent<Outline>();
            outline.effectColor = crosshairOutlineColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }

        private void ApplyReadableMetrics()
        {
            // Noto Sans KR의 실제 행 높이(약 1.45em)를 확보합니다. 글자 크기를 줄이지 않습니다.
            foreach (TMP_Text text in hudRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                RectTransform rect = text.rectTransform;
                float minHeight = text.fontSize * 1.6f;
                if (rect.sizeDelta.y < minHeight) rect.sizeDelta = new Vector2(rect.sizeDelta.x, minHeight);
            }
            SetHeight("AnomalyAlerts", 590);
            SetHeight("AnomalyAlerts/PriorityAndSummary", 64);
            SetHeight("AnomalyAlerts/DangerWarning", 70);
            SetHeight("FloodCondition", 198);
            SetHeight("FloodCondition/ActiveCondition", 174);
            SetHeight("Controls", 200);
            SetHeight("Controls/Keys", 176);
            TMP_Text controlsText = hudRoot.Find("Controls/Keys")?.GetComponent<TMP_Text>();
            if (controlsText != null)
            {
                controlsText.fontSize = 21;
                controlsText.text = "WASD 이동 · 마우스 시점\n마법 선택: 1/2/3 · 휠";
            }
            ((RectTransform)hudRoot.Find("CoreStatus/LocationAndExposure")).anchoredPosition = new Vector2(22, -84);
        }

        private void SetHeight(string path, float height)
        {
            RectTransform rect = (RectTransform)hudRoot.Find(path);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        private void BuildStatus(WardStatusHUD presenter)
        {
            RectTransform alerts = Panel("AnomalyAlerts", hudRoot, Vector2.up, Vector2.up,
                new Vector2(32, -32), new Vector2(480, 354));
            Text("Heading", alerts, new Vector2(20, -14), new Vector2(440, 30), 22,
                "시설 이상현상", Gold);
            TMP_Text detail = Text("PriorityAndSummary", alerts, new Vector2(20, -54),
                new Vector2(440, 210), 23, "활성 이상현상 없음", Color.white);
            TMP_Text warning = Text("DangerWarning", alerts, new Vector2(20, -274),
                new Vector2(440, 62), 22, "", new Color(1, 0.44f, 0.4f));
            RectTransform state = Panel("CoreStatus", hudRoot, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -32), new Vector2(700, 118));
            TMP_Text stability = Text("StabilityValue", state, new Vector2(22, -16), new Vector2(310, 38),
                25, "시설 안정도  100 / 100", Color.white);
            TMP_Text contamination = Text("ContaminationValue", state, new Vector2(372, -16), new Vector2(310, 38),
                25, "마력 오염도  0 / 100", Color.white);
            Image stabilityFill = Gauge("Stability", state, new Vector2(22, -65), new Vector2(306, 16), StabilityColor);
            Image contaminationFill = Gauge("Contamination", state, new Vector2(372, -65), new Vector2(306, 16), ContaminationColor);
            TMP_Text location = Text("LocationAndExposure", state, new Vector2(22, -88),
                new Vector2(656, 25), 19, "", Muted);
            if (presenter != null) presenter.ConfigureView(detail, warning, stability, contamination,
                stabilityFill, contaminationFill, location);
        }

        private void BuildCast(MagicHUD presenter)
        {
            RectTransform cast = Panel("CastPanel", hudRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1),
                new Vector2(0, -104), new Vector2(460, 132));
            TMP_Text title = Text("CastTitle", cast, new Vector2(18, -10), new Vector2(424, 32), 23, "", Color.white);
            Image fill = Gauge("CastProgress", cast, new Vector2(18, -52), new Vector2(350, 15), StabilityColor);
            TMP_Text percent = Text("CastPercent", cast, new Vector2(380, -43), new Vector2(68, 34), 24, "", Color.white);
            RectTransform timing = Panel("TimingTrack", cast, Vector2.up, Vector2.up,
                new Vector2(18, -76), new Vector2(350, 9));
            Image stable = Panel("StableZone", timing, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero).GetComponent<Image>();
            Image over = Panel("OverchargeZone", timing, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero).GetComponent<Image>();
            TMP_Text hint = Text("CastHint", cast, new Vector2(18, -99), new Vector2(424, 26), 19, "", Muted);
            TMP_Text aim = Text("AimOrResult", hudRoot, Vector2.zero, new Vector2(600, 38), 22, "", Color.white);
            aim.alignment = TextAlignmentOptions.Center;
            Place(aim.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1),
                new Vector2(0, -42), new Vector2(600, 38));
            if (presenter != null) presenter.ConfigureView(cast.gameObject, title, percent, fill, stable, over,
                aim, hint, timing.gameObject);
            cast.gameObject.SetActive(false);
        }

        private void BuildDay()
        {
            RectTransform day = Panel("DayResources", hudRoot, Vector2.one, Vector2.one,
                new Vector2(-32, -32), new Vector2(430, 130));
            dayStatusText = Text("DayStatus", day, new Vector2(20, -14), new Vector2(390, 106), 26, "", Color.white);
            RectTransform condition = Panel("FloodCondition", hudRoot, Vector2.one, Vector2.one,
                new Vector2(-32, -172), new Vector2(430, 130));
            floodText = Text("ActiveCondition", condition, new Vector2(18, -12), new Vector2(394, 106),
                22, "오늘의 범람 조건: 기본 조건", Gold);
        }

        private void BuildLoadout()
        {
            RectTransform loadout = Panel("MagicAndInventory", hudRoot, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 32), new Vector2(1000, 142));
            Text("MagicHeading", loadout, new Vector2(16, -9), new Vector2(420, 28), 20, "마법 선택", Muted);
            Text("ItemHeading", loadout, new Vector2(458, -9), new Vector2(520, 28), 20, "소비 아이템  ·  B 가방에서 사용", Muted);
            for (int i = 0; i < 3; i++)
            {
                RectTransform card = Panel("Magic_" + (i + 1), loadout, Vector2.up, Vector2.up,
                    new Vector2(16 + i * 142, -43), new Vector2(130, 84));
                magicCards[i] = card.GetComponent<Image>();
                magicLabels[i] = Text("Name", card, new Vector2(10, -12), new Vector2(110, 62), 24,
                    (i + 1) + "  " + MagicHUD.MagicName((MagicType)i), Color.white);
                magicLabels[i].alignment = TextAlignmentOptions.Center;
            }
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                RectTransform slot = Panel("ItemSlot_" + i, loadout, Vector2.up, Vector2.up,
                    new Vector2(458 + i * 132, -43), new Vector2(122, 84));
                itemAccents[i] = Panel("ItemAccent", slot, Vector2.up, Vector2.up,
                    new Vector2(0, 0), new Vector2(122, 3)).GetComponent<Image>();
                itemLabels[i] = Text("Contents", slot, new Vector2(8, -10), new Vector2(106, 68), 20, "빈 칸", Muted);
                itemLabels[i].alignment = TextAlignmentOptions.Center;
            }
        }

        private void LateUpdate()
        {
            if (dayFlow == null || hudRoot == null) return;
            bool visible = dayFlow.State == DayState.Playing && !dayFlow.InventoryOpen
                && !dayFlow.IsMapOpen && !dayFlow.SupplyPanelOpen;
            hudRoot.gameObject.SetActive(visible);
            FloodCondition condition = dayFlow.ActiveFloodCondition;
            bool showFloodCondition = dayFlow.State == DayState.Playing
                && dayFlow.CurrentDay >= FloodConditionRules.FirstFloodConditionDay
                && FloodConditionRules.IsAvailableCondition(condition);
            GameObject floodPanel = floodText.transform.parent.gameObject;
            floodPanel.SetActive(showFloodCondition);
            floodText.text = showFloodCondition
                ? "<color=#E8C26E>오늘의 범람 조건</color>\n<size=24><color=#94D8FF>"
                    + FloodConditionRules.GetName(condition) + "</color></size>\n<size=20><color=#EDF2F7>"
                    + FloodConditionRules.GetHighlightedRuleDescription(condition).Replace(" · ", "\n")
                    + "</color></size>"
                : string.Empty;
            for (int i = 0; i < magicCards.Length; i++)
            {
                bool selected = caster != null && (int)caster.SelectedMagic == i;
                magicCards[i].color = selected ? new Color(0.28f, 0.24f, 0.16f, 1) : new Color(0.08f, 0.10f, 0.14f, 1);
                magicLabels[i].color = selected ? Gold : Muted;
                magicLabels[i].text = (i + 1) + "  " + MagicHUD.MagicName((MagicType)i)
                    + (selected ? "\n<size=18>선택 중</size>" : "");
                Outline outline = magicCards[i].GetComponent<Outline>();
                if (outline != null) outline.effectColor = selected ? Gold : new Color(0.24f, 0.27f, 0.31f);
            }
            PlayerInventory inventory = dayFlow.Inventory;
            if (inventory == null) return;
            for (int i = 0; i < itemLabels.Length; i++)
            {
                ItemType item = inventory.GetItem(i);
                int count = inventory.GetQuantity(i);
                itemLabels[i].text = count <= 0 ? "빈 칸" : item == ItemType.PurificationPotion
                    ? "정화약\nx" + count : "비상 수리\n키트 x" + count;
                itemLabels[i].color = count > 0 ? Color.white : Muted;
                itemAccents[i].color = count <= 0 ? new Color(0.24f, 0.27f, 0.31f)
                    : item == ItemType.PurificationPotion ? ContaminationColor : StabilityColor;
            }
        }

        private static Transform FindDescendant(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, anchor, pivot, pos, size);
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rect = Rect(name, parent, anchor, pivot, pos, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = PanelColor;
            image.raycastTarget = false;
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.30f, 0.22f, 0.7f);
            outline.effectDistance = new Vector2(1, -1);
            return rect;
        }

        private TMP_Text Text(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, string value, Color color)
        {
            RectTransform rect = Rect(name, parent, Vector2.up, Vector2.up, pos, size);
            TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = koreanFont != null ? koreanFont : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.text = value;
            return text;
        }

        private Image Gauge(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            RectTransform track = Panel(name + "Track", parent, Vector2.up, Vector2.up, pos, size);
            track.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.15f, 1);
            RectTransform rect = Rect(name + "Fill", track, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image fill = rect.gameObject.AddComponent<Image>();
            // 기존 게이지의 Unity 기본 Sprite를 재사용합니다.
            fill.sprite = gaugeSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }
    }
}
