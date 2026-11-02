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
            Vector2[] positions = { new Vector2(-365f, -96f), new Vector2(0f, -96f), new Vector2(365f, -96f) };
            for (int i = 0; i < types.Length; i++)
            {
                DayUpgradeType type = types[i];
                GameObject card = CreatePanel($"Upgrade_{type}", parent,
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
                    new Vector2(270f, 58f), "구매", () => OnUpgradeClicked(type));
                TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>();
                buttonLabel.fontSize = 17;
                cards[i] = new UpgradeCardView(type, title, description, button,
                    buttonLabel, button.GetComponent<Image>());
            }
            resultText = CreateText("UpgradeResult", parent,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 86f), new Vector2(-50f, 26f), 18,
                TextAlignmentOptions.Center, new Color(0.76f, 0.92f, 1f));
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
                string purchaseStatus = maxLevel ? "최대 강화 완료"
                    : dayFlow.UpgradePurchasedThisClear ? "이번 DAY 구매 완료"
                    : inventory.ManaCrystals < price ? $"불가 (마력 조각 부족 · {price}개 필요)"
                    : canPurchase ? "구매 가능" : "현재 구매 불가";
                Color statusColor = purchaseComplete ? style.Complete
                    : canPurchase ? style.Available : style.Unavailable;
                card.Title.text = $"{Colorize(GetUpgradeTitleName(card.Type), GetUpgradeTitleColor(card.Type))} 강화 "
                    + GetUpgradeStageName(Mathf.Min(level + 1, DayUpgradeRules.MaximumLevel));
                card.Description.text = $"현재 단계: {level} / {DayUpgradeRules.MaximumLevel}\n"
                    + $"현재 효과: {ColorizeUpgradeEffect(card.Type, currentEffect)}\n"
                    + $"강화 후: {ColorizeUpgradeEffect(card.Type, nextEffect)}\n"
                    + $"필요한 마력 조각: {Colorize(maxLevel ? "없음" : price + "개", style.Price)}\n"
                    + $"구매 가능 여부: {Colorize(purchaseStatus, statusColor)}";
                card.Button.interactable = canPurchase;
                card.ButtonLabel.text = maxLevel ? "최대 강화 완료"
                    : dayFlow.UpgradePurchasedThisClear ? "오늘 구매 완료"
                    : inventory.ManaCrystals < price ? $"마력 조각 부족\n{price}개 필요"
                    : $"구매 · {price}개";
                card.ButtonImage.color = canPurchase ? new Color(0.12f, 0.42f, 0.6f, 1f)
                    : new Color(0.22f, 0.25f, 0.3f, 1f);
            }
            resultText.text = feedback;
        }

        private void OnUpgradeClicked(DayUpgradeType type)
        {
            if (dayFlow == null) return;
            bool purchased = dayFlow.TryPurchaseUpgrade(type);
            feedback = purchased ? $"{GetUpgradeTitleName(type)} 강화 강화 구매 완료"
                : "구매할 수 없습니다. 결정 수량과 오늘 구매 여부를 확인하세요.";
            Refresh();
        }

        private Color GetUpgradeTitleColor(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return style.MobilityTitle;
                case DayUpgradeType.FacilityDefense: return style.FacilityDefenseTitle;
                case DayUpgradeType.ContaminationResistance: return style.ContaminationResistanceTitle;
                default: return Color.white;
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

        private Button CreateActionButton(string name, Transform parent, Vector2 position,
            Vector2 size, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject buttonObject = CreatePanel(name, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size,
                new Color(0.12f, 0.42f, 0.6f, 1f));
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(() => AudioService.Play2D(WardShiftSoundId.UiButtonClick));
            button.onClick.AddListener(onClick);
            CreateText("Label", buttonObject.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 22,
                TextAlignmentOptions.Center, Color.white).text = label;
            return button;
        }

        private sealed class UpgradeCardView
        {
            internal readonly DayUpgradeType Type;
            internal readonly TMP_Text Title;
            internal readonly TMP_Text Description;
            internal readonly Button Button;
            internal readonly TMP_Text ButtonLabel;
            internal readonly Image ButtonImage;

            internal UpgradeCardView(DayUpgradeType type, TMP_Text title, TMP_Text description,
                Button button, TMP_Text buttonLabel, Image buttonImage)
            {
                Type = type;
                Title = title;
                Description = description;
                Button = button;
                ButtonLabel = buttonLabel;
                ButtonImage = buttonImage;
            }
        }
    }
}
