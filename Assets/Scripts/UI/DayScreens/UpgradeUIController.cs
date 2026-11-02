using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>DAY 클리어 화면의 강화 카드 표시와 구매 입력을 담당합니다.</summary>
    internal sealed class UpgradeUIController
    {
        internal struct Style
        {
            internal Color MobilityTitle;
            internal Color FacilityDefenseTitle;
            internal Color ContaminationResistanceTitle;
            internal string MobilityEffectWord;
            internal Color MobilityEffect;
            internal string FacilityDefenseEffectWord;
            internal Color FacilityDefenseEffect;
            internal string ContaminationResistanceEffectWord;
            internal Color ContaminationResistanceEffect;
            internal Color Price;
            internal Color Available;
            internal Color Unavailable;
            internal Color Complete;
        }

        private readonly TMP_FontAsset font;
        private readonly Style style;
        private readonly UpgradeCardView[] cards = new UpgradeCardView[3];
        private readonly TMP_Text resultText;
        private DayFlowManager dayFlow;
        private PlayerInventory inventory;
        private string feedback = string.Empty;

        internal UpgradeUIController(Transform parent, TMP_FontAsset uiFont, DayFlowManager manager,
            PlayerInventory playerInventory, Style visualStyle)
        {
            font = uiFont;
            dayFlow = manager;
            inventory = playerInventory;
            style = visualStyle;

            DayUpgradeType[] types =
            {
                DayUpgradeType.Mobility,
                DayUpgradeType.FacilityDefense,
                DayUpgradeType.ContaminationResistance
            };
            Vector2[] positions =
            {
                new Vector2(-370f, -80f),
                new Vector2(0f, -80f),
                new Vector2(370f, -80f)
            };
            for (int i = 0; i < types.Length; i++)
            {
                DayUpgradeType type = types[i];
                GameObject card = CreatePanel($"Upgrade_{type}", parent,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    positions[i], new Vector2(340f, 360f), WardShiftHUDLayout.RecordCardColor);
                AddCardFrame(card, GetUpgradeTitleColor(type));

                CreateIcon("UpgradeIcon", card.transform,
                    $"WardShift/UI/wardshift_icon_{GetIconSuffix(type)}");
                TMP_Text title = CreateText("Name", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(-28f, 36f),
                    24f, TextAlignmentOptions.Center, GetUpgradeTitleColor(type));
                title.enableAutoSizing = true;
                title.fontSizeMin = 20f;
                title.fontSizeMax = 24f;

                TMP_Text stage = CreateText("Stage", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0f, -157f), new Vector2(-32f, 24f),
                    17f, TextAlignmentOptions.Center, WardShiftHUDLayout.RecordHeaderColor);
                CreatePanel("EffectDivider", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0f, -183f), new Vector2(-38f, 1f),
                    WardShiftHUDLayout.RecordDividerColor).GetComponent<Image>().raycastTarget = false;
                TMP_Text effects = CreateText("Effect", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0f, -194f), new Vector2(-40f, 72f),
                    17f, TextAlignmentOptions.TopLeft, WardShiftHUDLayout.RecordBodyColor);
                effects.richText = true;
                effects.lineSpacing = 2f;

                TMP_Text price = CreateText("Price", card.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(0f, 0f), new Vector2(18f, 66f), new Vector2(145f, 24f),
                    14f, TextAlignmentOptions.MidlineLeft, style.Price);
                TMP_Text status = CreateText("PurchaseStatus", card.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(1f, 0f), new Vector2(-18f, 66f), new Vector2(145f, 24f),
                    14f, TextAlignmentOptions.MidlineRight, style.Available);

                Button button = CreateActionButton("Purchase", card.transform,
                    new Vector2(0f, -142f), new Vector2(292f, 48f), "강화 구매",
                    () => OnUpgradeClicked(type));
                TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>(true);
                buttonLabel.fontSize = 18f;
                cards[i] = new UpgradeCardView(type, title, stage, effects,
                    price, status, button, buttonLabel);
            }

            resultText = CreateText("UpgradeResult", parent,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 88f), new Vector2(-50f, 24f), 17f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordHeaderColor);
        }

        internal void Bind(DayFlowManager manager, PlayerInventory playerInventory)
        {
            dayFlow = manager;
            inventory = playerInventory;
        }

        internal void ClearFeedback()
        {
            feedback = string.Empty;
            resultText.text = feedback;
        }

        internal void Refresh()
        {
            if (dayFlow == null || inventory == null) return;
            for (int i = 0; i < cards.Length; i++)
            {
                UpgradeCardView card = cards[i];
                int level = dayFlow.GetUpgradeLevel(card.Type);
                int price = dayFlow.GetNextUpgradePrice(card.Type);
                bool maxLevel = level >= DayUpgradeRules.MaximumLevel;
                bool canPurchase = dayFlow.CanPurchaseUpgrade(card.Type);
                bool purchaseComplete = maxLevel || dayFlow.UpgradePurchasedThisClear;
                string currentEffect = dayFlow.GetUpgradeEffectText(card.Type, level);
                string nextEffect = maxLevel
                    ? "최대 강화 완료" : dayFlow.GetUpgradeEffectText(card.Type, level + 1);
                string purchaseStatus = maxLevel ? "최대 단계"
                    : dayFlow.UpgradePurchasedThisClear ? "구매 완료"
                    : inventory.ManaCrystals < price ? "조각 부족"
                    : canPurchase ? "구매 가능" : "구매 불가";
                Color statusColor = purchaseComplete ? style.Complete
                    : canPurchase ? style.Available : style.Unavailable;

                card.Title.text = $"{GetUpgradeTitleName(card.Type)} 강화  {GetUpgradeStageName(Mathf.Min(level + 1, DayUpgradeRules.MaximumLevel))}";
                card.Stage.text = $"현재 단계  {level} / {DayUpgradeRules.MaximumLevel}";
                card.Effects.text = $"현재 효과  {ColorizeUpgradeEffect(card.Type, currentEffect)}\n"
                    + $"강화 후  {ColorizeUpgradeEffect(card.Type, nextEffect)}";
                card.Price.text = maxLevel ? "비용 없음" : $"마력 조각 {price}개";
                card.Status.text = Colorize(purchaseStatus, statusColor);

                card.Button.interactable = canPurchase;
                card.ButtonLabel.text = maxLevel ? "최대 강화 완료"
                    : dayFlow.UpgradePurchasedThisClear ? "이번 DAY 구매 완료"
                    : inventory.ManaCrystals < price ? $"마력 조각 부족 · {price}개 필요"
                    : canPurchase ? "강화 구매" : "현재 구매 불가";
                SetPurchaseButtonColors(card.Button, canPurchase);
            }
            resultText.text = feedback;
        }

        private void OnUpgradeClicked(DayUpgradeType type)
        {
            if (dayFlow == null) return;
            bool purchased = dayFlow.TryPurchaseUpgrade(type);
            feedback = purchased ? $"{GetUpgradeTitleName(type)} 강화 구매 완료"
                : "구매할 수 없습니다. 마력 조각 수량과 이번 DAY 구매 여부를 확인하세요.";
            Refresh();
        }

        private Color GetUpgradeTitleColor(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return style.MobilityTitle;
                case DayUpgradeType.FacilityDefense: return style.FacilityDefenseTitle;
                case DayUpgradeType.ContaminationResistance: return style.ContaminationResistanceTitle;
                default: return WardShiftHUDLayout.RecordTitleColor;
            }
        }

        private string ColorizeUpgradeEffect(DayUpgradeType type, string text)
        {
            string word;
            Color color;
            switch (type)
            {
                case DayUpgradeType.Mobility:
                    word = style.MobilityEffectWord;
                    color = style.MobilityEffect;
                    break;
                case DayUpgradeType.FacilityDefense:
                    word = style.FacilityDefenseEffectWord;
                    color = style.FacilityDefenseEffect;
                    break;
                case DayUpgradeType.ContaminationResistance:
                    word = style.ContaminationResistanceEffectWord;
                    color = style.ContaminationResistanceEffect;
                    break;
                default: return text;
            }
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return text;
            return text.Replace(word, Colorize(word, color));
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

        private static string GetIconSuffix(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return "mobility";
                case DayUpgradeType.FacilityDefense: return "facility_defense";
                case DayUpgradeType.ContaminationResistance: return "contamination_resistance";
                default: return "mobility";
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

        private static void AddCardFrame(GameObject card, Color accent)
        {
            Outline outline = card.AddComponent<Outline>();
            outline.effectColor = WardShiftHUDLayout.RecordFrameColor;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            CreatePanel("TopAccent", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-24f, 1.5f),
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

        private Button CreateActionButton(string name, Transform parent, Vector2 position,
            Vector2 size, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = CreatePanel(name, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size,
                WardShiftHUDLayout.RecordButtonColor);
            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.raycastTarget = true;
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(145, 143, 112, 155);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = WardShiftHUDLayout.RecordButtonColor;
            colors.highlightedColor = WardShiftHUDLayout.RecordButtonHoverColor;
            colors.pressedColor = WardShiftHUDLayout.RecordButtonPressedColor;
            colors.selectedColor = WardShiftHUDLayout.RecordButtonHoverColor;
            colors.disabledColor = WardShiftHUDLayout.RecordButtonDisabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(() => AudioService.Play2D(WardShiftSoundId.UiButtonClick));
            button.onClick.AddListener(onClick);
            CreateText("Label", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 18f,
                TextAlignmentOptions.Center, WardShiftHUDLayout.RecordTitleColor).text = label;
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

        private sealed class UpgradeCardView
        {
            internal readonly DayUpgradeType Type;
            internal readonly TMP_Text Title;
            internal readonly TMP_Text Stage;
            internal readonly TMP_Text Effects;
            internal readonly TMP_Text Price;
            internal readonly TMP_Text Status;
            internal readonly Button Button;
            internal readonly TMP_Text ButtonLabel;

            internal UpgradeCardView(DayUpgradeType type, TMP_Text title, TMP_Text stage,
                TMP_Text effects, TMP_Text price, TMP_Text status, Button button, TMP_Text buttonLabel)
            {
                Type = type;
                Title = title;
                Stage = stage;
                Effects = effects;
                Price = price;
                Status = status;
                Button = button;
                ButtonLabel = buttonLabel;
            }
        }
    }
}
