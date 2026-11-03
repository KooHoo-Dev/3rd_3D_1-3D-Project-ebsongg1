using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10005)]
    public sealed class WardStatusHUD : MonoBehaviour
    {
        private const int MaximumVisibleAlerts = 3;
        private const float AlertCardHeight = 118f;
        private const float AlertCardSpacing = 7f;
        private const float AlertListTop = 62f;
        private const float MaximumAlertPanelHeight = 590f;
        private const float StabilityDamageFlashDuration = 0.22f;
        private const float StabilityDamageFlashInterval = 0.42f;
        private const float StabilityThresholdWarningDuration = 0.46f;
        private const float StabilityThresholdWarningCooldown = 1.2f;
        private static readonly Color StabilityDamageFlashColor = new Color(1f, 0.34f, 0.18f);
        private static readonly Color StabilityDangerColor = new Color(1f, 0.30f, 0.25f);
        private static readonly Color StabilityCautionColor = new Color(1f, 0.70f, 0.25f);

        [SerializeField] private WardStatus status;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text warningText;
        [SerializeField] private Image stabilityFill;
        [SerializeField] private Image contaminationFill;
        [SerializeField] private GameObject failurePanel;
        [SerializeField] private TMP_Text failureText;
        [SerializeField] private TMP_Text stabilityText;
        [SerializeField] private TMP_Text contaminationText;
        [SerializeField] private TMP_Text locationText;

        private readonly List<AnomalyBehaviour> priorities = new List<AnomalyBehaviour>();
        private readonly Dictionary<int, long> spawnOrder = new Dictionary<int, long>();
        private readonly HashSet<int> seenAnomalies = new HashSet<int>();
        private readonly HashSet<int> visibleAlerts = new HashSet<int>();
        private readonly Dictionary<int, AlertCard> activeCards = new Dictionary<int, AlertCard>();
        private readonly Stack<AlertCard> pooledCards = new Stack<AlertCard>();
        private readonly List<int> staleCardIds = new List<int>();
        private readonly List<int> staleAnomalyIds = new List<int>();

        private DayFlowManager dayFlow;
        private FacilityZoneTracker zoneTracker;
        private ZoneIdentity[] zones;
        private RectTransform anomalyCardList;
        private TMP_Text overflowText;
        private GameObject anomalyCardTemplate;
        private long nextSpawnOrder;
        private float directHitUntil;
        private float directHitAmount;
        private float previousStability = float.NaN;
        private float previousContamination = float.NaN;
        private float contaminationEmphasisUntil;
        private bool emphasizedFirstContaminationGain;
        private bool wasFacilityStabilityDanger;
        private CanvasGroup stabilityThresholdWarning;
        private float stabilityDamageFlashUntil;
        private float nextStabilityDamageFlashAt;
        private float stabilityThresholdWarningStartedAt;
        private float stabilityThresholdWarningUntil;
        private float nextStabilityThresholdWarningAt;
        public FacilityZoneTracker ZoneTracker => zoneTracker;

        public void ShowContaminationIncrease(float amount)
        {
            directHitAmount = amount;
            directHitUntil = Time.unscaledTime + 0.65f;
        }

        private static string FormatContaminationIncrease(float amount)
        {
            if (amount < 0.01f) return string.Format("  +{0:F3}", amount);
            if (amount < 0.1f) return string.Format("  +{0:F2}", amount);
            return amount < 1f
                ? string.Format("  +{0:F1}", amount)
                : string.Format("  +{0:F0}", amount);
        }

        private void Awake()
        {
            dayFlow = FindFirstObjectByType<DayFlowManager>();
            zoneTracker = dayFlow != null ? dayFlow.ZoneTracker : FindFirstObjectByType<FacilityZoneTracker>();
            zones = FindObjectsByType<ZoneIdentity>(FindObjectsSortMode.None);
            EnsureAlertHierarchy();
        }

        private void OnEnable()
        {
            if (status != null) status.StabilityChanged += OnStabilityChanged;
            previousStability = status != null ? status.Stability : float.NaN;
            ResetStabilityFeedback();
        }

        private void OnDisable()
        {
            if (status != null) status.StabilityChanged -= OnStabilityChanged;
            ResetStabilityFeedback();
            ReleaseAllCards();
        }

        private void OnStabilityChanged(float stability)
        {
            float now = Time.unscaledTime;
            float previous = previousStability;
            if (!float.IsNaN(previous) && stability < previous - 0.001f
                && CanShowStabilityFeedback())
            {
                if (now >= nextStabilityDamageFlashAt)
                {
                    stabilityDamageFlashUntil = now + StabilityDamageFlashDuration;
                    nextStabilityDamageFlashAt = now + StabilityDamageFlashInterval;
                }

                if (CrossedStabilityWarningThreshold(previous, stability)
                    && now >= nextStabilityThresholdWarningAt)
                {
                    stabilityThresholdWarningStartedAt = now;
                    stabilityThresholdWarningUntil = now + StabilityThresholdWarningDuration;
                    nextStabilityThresholdWarningAt = now + StabilityThresholdWarningCooldown;
                }
            }
            if (stabilityFill != null) stabilityFill.fillAmount = stability / 100f;
            previousStability = stability;
        }

        public void ConfigureStabilityThresholdWarning(CanvasGroup warning)
        {
            stabilityThresholdWarning = warning;
            if (stabilityThresholdWarning == null) return;
            stabilityThresholdWarning.interactable = false;
            stabilityThresholdWarning.blocksRaycasts = false;
            stabilityThresholdWarning.alpha = 0f;
        }

        private bool CanShowStabilityFeedback()
        {
            if (status == null || status.HasFailed) return false;
            return dayFlow == null || (dayFlow.State == DayState.Playing
                && !dayFlow.InventoryOpen && !dayFlow.IsMapOpen && !dayFlow.SupplyPanelOpen);
        }

        private static bool CrossedStabilityWarningThreshold(float previous, float current)
        {
            for (int threshold = 90; threshold >= 10; threshold -= 10)
            {
                if ((previous > threshold && current <= threshold)
                    || (Mathf.Approximately(previous, threshold) && current < threshold))
                    return true;
            }
            return false;
        }

        private void ResetStabilityFeedback()
        {
            stabilityDamageFlashUntil = 0f;
            nextStabilityDamageFlashAt = 0f;
            stabilityThresholdWarningStartedAt = 0f;
            stabilityThresholdWarningUntil = 0f;
            nextStabilityThresholdWarningAt = 0f;
            if (stabilityThresholdWarning != null) stabilityThresholdWarning.alpha = 0f;
        }

        private void UpdateStabilityThresholdWarning(float now, bool visible)
        {
            if (stabilityThresholdWarning == null) return;
            if (!visible)
            {
                ResetStabilityFeedback();
                return;
            }

            if (stabilityThresholdWarningUntil <= now)
            {
                stabilityThresholdWarning.alpha = 0f;
                return;
            }

            float fadeIn = Mathf.Clamp01((now - stabilityThresholdWarningStartedAt) / 0.055f);
            float fadeOut = Mathf.Clamp01((stabilityThresholdWarningUntil - now) / 0.30f);
            stabilityThresholdWarning.alpha = Mathf.Min(fadeIn, fadeOut);
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            bool feedbackVisible = CanShowStabilityFeedback();
            UpdateStabilityThresholdWarning(now, feedbackVisible);
            if (status == null || status.Player == null) return;
            EnsureAlertHierarchy();

            PlayerContamination player = status.Player;
            if (!emphasizedFirstContaminationGain && !float.IsNaN(previousContamination)
                && player.Value > previousContamination + 0.001f)
            {
                emphasizedFirstContaminationGain = true;
                contaminationEmphasisUntil = now + 1f;
            }
            previousContamination = player.Value;
            bool gameplayActive = dayFlow == null || dayFlow.State == DayState.Playing;
            bool facilityStabilityDanger = gameplayActive && status.Stability <= 20f;
            if (facilityStabilityDanger && !wasFacilityStabilityDanger)
                AudioService.Play2D(WardShiftSoundId.FacilityStabilityDanger);
            wasFacilityStabilityDanger = facilityStabilityDanger;
            if (!gameplayActive) directHitUntil = 0f;
            priorities.Clear();
            seenAnomalies.Clear();
            IReadOnlyList<AnomalyBehaviour> activeAnomalies = AnomalyBehaviour.ActiveAnomalies;
            for (int i = 0; i < activeAnomalies.Count; i++)
            {
                AnomalyBehaviour anomaly = activeAnomalies[i];
                if (anomaly == null) continue;

                int instanceId = anomaly.GetInstanceID();
                seenAnomalies.Add(instanceId);
                if (!spawnOrder.ContainsKey(instanceId)) spawnOrder.Add(instanceId, ++nextSpawnOrder);
                if (!gameplayActive || !anomaly.IsHazardActive) continue;

                priorities.Add(anomaly);
            }

            staleAnomalyIds.Clear();
            foreach (KeyValuePair<int, long> entry in spawnOrder)
                if (!seenAnomalies.Contains(entry.Key)) staleAnomalyIds.Add(entry.Key);
            for (int i = 0; i < staleAnomalyIds.Count; i++) spawnOrder.Remove(staleAnomalyIds[i]);

            priorities.Sort(CompareUrgency);
            RefreshAnomalyCards(gameplayActive);

            if (stabilityText != null) stabilityText.text = string.Format("시설 안정도  {0:F0} / 100", status.Stability);
            bool stabilityDamageFlash = feedbackVisible && now < stabilityDamageFlashUntil;
            Color stabilityBaseColor = status.Stability <= 30f ? StabilityDangerColor
                : status.Stability <= 50f ? StabilityCautionColor : Color.white;
            Color stabilityDisplayColor = stabilityDamageFlash ? StabilityDamageFlashColor : stabilityBaseColor;
            if (stabilityText != null) stabilityText.color = stabilityDisplayColor;
            bool directHitVisible = now < directHitUntil;
            if (contaminationText != null)
            {
                contaminationText.text = string.Format("마력 오염도  {0:F0} / 100", player.Value)
                    + (directHitVisible ? FormatContaminationIncrease(directHitAmount) : "");
                contaminationText.color = directHitVisible || now < contaminationEmphasisUntil
                    ? new Color(1f, 0.55f, 0.8f)
                    : Color.white;
            }
            if (locationText != null)
            {
                ZoneIdentity zone = zoneTracker != null ? zoneTracker.CurrentZone : null;
                locationText.text = (zone != null ? zone.DisplayName + "  ·  " : "")
                    + string.Format("노출 +{0:F1}/초", player.ExposurePerSecond);
            }
            if (stabilityFill != null) stabilityFill.fillAmount = status.Stability / 100f;
            if (contaminationFill != null) contaminationFill.fillAmount = player.Value / 100f;
            if (stabilityFill != null) stabilityFill.color = stabilityDamageFlash
                ? StabilityDamageFlashColor
                : status.Stability <= 30f ? StabilityDangerColor
                : status.Stability <= 50f ? StabilityCautionColor : WardShiftHUDLayout.StabilityColor;
            if (contaminationFill != null) contaminationFill.color = directHitVisible
                || Time.unscaledTime < contaminationEmphasisUntil
                ? new Color(1f, 0.45f, 0.8f)
                : player.Value >= 80f ? new Color(1, 0.3f, 0.25f)
                : WardShiftHUDLayout.ContaminationColor;

            if (warningText != null)
            {
                warningText.color = new Color(1f, 0.44f, 0.4f);
                string globalWarning = player.Value >= 80f && status.Stability <= 30f ? "오염도 / 시설 안정도 모두 위험"
                    : player.Value >= 80f ? "오염도 위험 · 오염원에서 벗어나세요"
                    : status.Stability <= 30f ? "시설 안정도 위험 · 긴급 대응 필요"
                    : player.ExposurePerSecond > 0f ? "위험 영역 근처: 오염도 증가 중" : "";
                // 단계 상승과 장치 공격은 별도의 상단 경고 배너가 우선 관리합니다.
                warningText.text = status.HasFailed ? "근무 실패" : globalWarning;
            }

            FitAlertPanel(gameplayActive);
            if (failurePanel != null && failurePanel != gameObject)
                failurePanel.SetActive(status.HasFailed && dayFlow == null);
            if (failureText != null && status.HasFailed)
                failureText.text = dayFlow != null
                    ? string.Format("근무 실패\n{0}일차\n{1}\n플레이 모드를 종료한 뒤 다시 시작하세요.", dayFlow.CurrentDay, status.FailureReason)
                    : string.Format("게임 실패\n{0}\n플레이 모드를 종료한 뒤 다시 시작하세요.", status.FailureReason);
        }

        private void EnsureAlertHierarchy()
        {
            if (statusText == null || statusText.transform.parent == null) return;
            Transform panel = statusText.transform.parent;
            Transform listTransform = panel.Find("AnomalyCards");
            if (listTransform == null)
            {
                GameObject listObject = new GameObject("AnomalyCards", typeof(RectTransform), typeof(VerticalLayoutGroup));
                listObject.transform.SetParent(panel, false);
                anomalyCardList = listObject.GetComponent<RectTransform>();
                anomalyCardList.anchorMin = new Vector2(0f, 1f);
                anomalyCardList.anchorMax = new Vector2(1f, 1f);
                anomalyCardList.pivot = new Vector2(0f, 1f);
                anomalyCardList.anchoredPosition = new Vector2(20f, -AlertListTop);
                anomalyCardList.sizeDelta = new Vector2(-40f, 0f);
            }
            else anomalyCardList = listTransform.GetComponent<RectTransform>();

            VerticalLayoutGroup layoutGroup = anomalyCardList.GetComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(0, 0, 0, 0);
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.spacing = AlertCardSpacing;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;

            Transform overflowTransform = panel.Find("OverflowCount");
            if (overflowTransform == null)
            {
                GameObject textObject = new GameObject("OverflowCount", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(panel, false);
                overflowText = textObject.GetComponent<TMP_Text>();
                RectTransform rect = overflowText.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(-40f, 26f);
            }
            else overflowText = overflowTransform.GetComponent<TMP_Text>();
            overflowText.font = statusText.font;
            overflowText.fontSize = 17f;
            overflowText.color = WardShiftHUDLayout.Muted;
            overflowText.alignment = TextAlignmentOptions.Left;
            overflowText.textWrappingMode = TextWrappingModes.NoWrap;
            overflowText.overflowMode = TextOverflowModes.Ellipsis;
            overflowText.enableAutoSizing = false;
            overflowText.raycastTarget = false;

            Transform templateTransform = anomalyCardList.Find("AnomalyCardTemplate");
            if (templateTransform == null) anomalyCardTemplate = CreateAlertCardTemplate();
            else anomalyCardTemplate = templateTransform.gameObject;
            anomalyCardTemplate.SetActive(false);
        }

        private GameObject CreateAlertCardTemplate()
        {
            GameObject card = new GameObject("AnomalyCardTemplate", typeof(RectTransform),
                typeof(Image), typeof(LayoutElement));
            card.transform.SetParent(anomalyCardList, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(0f, AlertCardHeight);

            Image background = card.GetComponent<Image>();
            background.color = new Color(0.035f, 0.047f, 0.065f, 0.96f);
            background.raycastTarget = false;
            LayoutElement layoutElement = card.GetComponent<LayoutElement>();
            layoutElement.minHeight = AlertCardHeight;
            layoutElement.preferredHeight = AlertCardHeight;
            layoutElement.flexibleHeight = 0f;

            GameObject accentObject = new GameObject("SeverityAccent", typeof(RectTransform), typeof(Image));
            accentObject.transform.SetParent(card.transform, false);
            RectTransform accentRect = accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = new Vector2(8f, 0f);
            accentRect.sizeDelta = new Vector2(4f, -16f);
            Image accent = accentObject.GetComponent<Image>();
            accent.color = StageColor(1);
            accent.raycastTarget = false;

            GameObject detailsObject = new GameObject("Details", typeof(RectTransform), typeof(TextMeshProUGUI));
            detailsObject.transform.SetParent(card.transform, false);
            RectTransform detailsRect = detailsObject.GetComponent<RectTransform>();
            detailsRect.anchorMin = Vector2.zero;
            detailsRect.anchorMax = Vector2.one;
            // Increase the inner top padding so the first line sits clearly inside the card.
            detailsRect.offsetMin = new Vector2(20f, 10f);
            detailsRect.offsetMax = new Vector2(-10f, -15f);
            TMP_Text details = detailsObject.GetComponent<TMP_Text>();
            details.font = statusText.font;
            details.fontSize = 18f;
            details.color = Color.white;
            details.alignment = TextAlignmentOptions.TopLeft;
            details.textWrappingMode = TextWrappingModes.Normal;
            details.overflowMode = TextOverflowModes.Ellipsis;
            details.enableAutoSizing = false;
            details.raycastTarget = false;
            details.richText = true;

            card.SetActive(false);
            return card;
        }

        private void RefreshAnomalyCards(bool gameplayActive)
        {
            if (statusText == null || anomalyCardList == null || anomalyCardTemplate == null) return;

            int visibleCount = gameplayActive ? Mathf.Min(MaximumVisibleAlerts, priorities.Count) : 0;
            visibleAlerts.Clear();
            statusText.rectTransform.anchorMin = new Vector2(0f, 1f);
            statusText.rectTransform.anchorMax = new Vector2(0f, 1f);
            statusText.rectTransform.pivot = new Vector2(0f, 1f);
            statusText.rectTransform.anchoredPosition = new Vector2(20f, -AlertListTop);
            statusText.rectTransform.sizeDelta = new Vector2(440f, 64f);
            statusText.alignment = TextAlignmentOptions.TopLeft;
            statusText.enableAutoSizing = false;
            statusText.textWrappingMode = TextWrappingModes.Normal;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
            statusText.gameObject.SetActive(visibleCount == 0 && gameplayActive);
            if (visibleCount == 0 && gameplayActive)
                statusText.text = "활성 이상현상 없음\n<size=18><color=#B8C4D1>시설을 순찰하세요 · M 지도</color></size>";

            anomalyCardList.gameObject.SetActive(visibleCount > 0);
            for (int i = 0; i < visibleCount; i++)
            {
                AnomalyBehaviour anomaly = priorities[i];
                int instanceId = anomaly.GetInstanceID();
                visibleAlerts.Add(instanceId);
                if (!activeCards.TryGetValue(instanceId, out AlertCard card))
                {
                    card = AcquireCard(instanceId);
                    activeCards.Add(instanceId, card);
                }

                card.Root.name = "AnomalyCard_" + instanceId;
                card.Root.SetActive(true);
                RefreshCard(card, anomaly);
                int expectedIndex = i + 1; // 첫 자식은 재사용하는 공통 카드 템플릿입니다.
                if (card.Root.transform.GetSiblingIndex() != expectedIndex)
                    card.Root.transform.SetSiblingIndex(expectedIndex);
            }

            staleCardIds.Clear();
            foreach (KeyValuePair<int, AlertCard> entry in activeCards)
                if (!visibleAlerts.Contains(entry.Key)) staleCardIds.Add(entry.Key);
            for (int i = 0; i < staleCardIds.Count; i++)
            {
                AlertCard card = activeCards[staleCardIds[i]];
                activeCards.Remove(staleCardIds[i]);
                card.Anomaly = null;
                card.Root.SetActive(false);
                pooledCards.Push(card);
            }

            bool hasOverflow = visibleCount > 0 && priorities.Count > MaximumVisibleAlerts;
            overflowText.gameObject.SetActive(hasOverflow);
            if (hasOverflow)
            {
                overflowText.text = string.Format("그 외 이상현상 {0}개 · M 지도에서 위치 확인",
                    priorities.Count - MaximumVisibleAlerts);
            }
        }

        private AlertCard AcquireCard(int instanceId)
        {
            AlertCard card;
            if (pooledCards.Count > 0) card = pooledCards.Pop();
            else
            {
                GameObject instance = Instantiate(anomalyCardTemplate, anomalyCardList, false);
                instance.SetActive(true);
                card = new AlertCard(instance,
                    instance.transform.Find("Details").GetComponent<TMP_Text>(),
                    instance.transform.Find("SeverityAccent").GetComponent<Image>());
            }
            card.Anomaly = null;
            card.Root.name = "AnomalyCard_" + instanceId;
            return card;
        }

        private void RefreshCard(AlertCard card, AnomalyBehaviour anomaly)
        {
            int stage = Mathf.Clamp(anomaly.DangerStage, 1, 3);
            Color stageColor = StageColor(stage);
            float remaining = anomaly.SecondsUntilRupture;
            string timingText = stage < 3
                ? string.Format("{0}단계까지 {1} · 파열까지 {2}", stage + 1,
                    DisplayDuration(anomaly.SecondsUntilNextStage), DisplayDuration(remaining))
                : "파열까지 " + DisplayDuration(remaining);
            string details = string.Format(
                "<size=20><color=#{0}>{1} · {2}</color></size>\n<size=17>위험 {3}단계</size>\n<size=16.5>{4}</size>\n<size=16.5>{5}</size>",
                ColorUtility.ToHtmlStringRGB(stageColor), Location(anomaly), anomaly.DisplayName,
                stage, timingText, ProgressSummary(anomaly));
            if (card.Details.text != details) card.Details.text = details;
            card.Accent.color = stageColor;
            card.Anomaly = anomaly;
        }

        private static string DisplayDuration(float seconds)
        {
            return float.IsInfinity(seconds) || float.IsNaN(seconds)
                ? "시간 정보 없음" : Mathf.CeilToInt(Mathf.Max(0f, seconds)) + "초";
        }

        private void FitAlertPanel(bool gameplayActive)
        {
            if (statusText == null || warningText == null || statusText.transform.parent.name != "AnomalyAlerts") return;
            RectTransform panel = (RectTransform)statusText.transform.parent;
            int visibleCount = gameplayActive ? Mathf.Min(MaximumVisibleAlerts, priorities.Count) : 0;
            bool showEmpty = visibleCount == 0 && gameplayActive;
            float contentBottom;
            if (visibleCount > 0)
            {
                float listHeight = visibleCount * AlertCardHeight + (visibleCount - 1) * AlertCardSpacing;
                anomalyCardList.sizeDelta = new Vector2(-40f, listHeight);
                contentBottom = AlertListTop + listHeight;
                if (overflowText.gameObject.activeSelf)
                {
                    float overflowTop = contentBottom + 6f;
                    overflowText.rectTransform.anchoredPosition = new Vector2(20f, -overflowTop);
                    contentBottom = overflowTop + 26f;
                }
            }
            else contentBottom = showEmpty ? AlertListTop + 64f : 0f;

            bool showWarning = !string.IsNullOrEmpty(warningText.text);
            float warningTop = contentBottom + (contentBottom > 0f ? 12f : 0f);
            warningText.rectTransform.anchorMin = new Vector2(0f, 1f);
            warningText.rectTransform.anchorMax = new Vector2(0f, 1f);
            warningText.rectTransform.pivot = new Vector2(0f, 1f);
            warningText.rectTransform.anchoredPosition = new Vector2(20f, -warningTop);
            warningText.rectTransform.sizeDelta = new Vector2(440f, 70f);
            warningText.gameObject.SetActive(showWarning);

            float panelHeight = showWarning ? warningTop + 70f + 14f
                : contentBottom + 16f;
            panel.sizeDelta = new Vector2(panel.sizeDelta.x,
                Mathf.Clamp(panelHeight, 100f, MaximumAlertPanelHeight));
        }

        private int CompareUrgency(AnomalyBehaviour a, AnomalyBehaviour b)
        {
            int stage = b.DangerStage.CompareTo(a.DangerStage);
            if (stage != 0) return stage;

            float aRupture = a.SecondsUntilRupture;
            float bRupture = b.SecondsUntilRupture;
            if (float.IsNaN(aRupture)) aRupture = float.PositiveInfinity;
            if (float.IsNaN(bRupture)) bRupture = float.PositiveInfinity;
            int time = aRupture.CompareTo(bRupture);
            if (time != 0) return time;

            long aOrder = spawnOrder.TryGetValue(a.GetInstanceID(), out long aValue) ? aValue : 0L;
            long bOrder = spawnOrder.TryGetValue(b.GetInstanceID(), out long bValue) ? bValue : 0L;
            return bOrder.CompareTo(aOrder); // 발생 순번이 큰 최신 현상이 먼저 옵니다.
        }

        private string Location(AnomalyBehaviour anomaly)
        {
            ZoneIdentity zone = anomaly.Zone != null ? anomaly.Zone
                : ZoneIdentity.FindNearest(anomaly.transform.position, zones);
            return zone != null ? zone.DisplayName : string.IsNullOrWhiteSpace(anomaly.SpawnLocationLabel)
                ? anomaly.DisplayName : anomaly.SpawnLocationLabel;
        }

        private static string ProgressSummary(AnomalyBehaviour anomaly)
        {
            if (anomaly is ManaRift rift)
            {
                return rift.IsSealCastInProgress
                    ? string.Format("봉인 진행 {0}% · 봉인점 {1}/{2}",
                        Mathf.FloorToInt(rift.SealCastProgress * 100f), rift.CompletedSealCount, rift.SealPointCount)
                    : string.Format("봉인점 {0}/{1} 완료", rift.CompletedSealCount, rift.SealPointCount);
            }

            if (anomaly is CurseAnomaly curse)
            {
                int purified = 0;
                int selected = curse.SelectedPatchCount;
                float activeProgress = 0f;
                CursePatch[] patches = curse.Patches;
                if (patches != null)
                {
                    foreach (CursePatch patch in patches)
                    {
                        if (patch == null) continue;
                        if (patch.IsPurified) purified++;
                        MagicTarget target = patch.PurificationTarget;
                        if (target != null && target.IsCasting)
                            activeProgress = Mathf.Max(activeProgress, target.Progress);
                    }
                }
                return activeProgress > 0f
                    ? string.Format("정화 진행 {0}% · 완료 {1}/{2}",
                        Mathf.FloorToInt(activeProgress * 100f), purified, selected)
                    : string.Format("정화 {0}/{1} 완료 · 활성 패치 {2}개",
                        purified, selected, curse.ActivePatchCount);
            }

            if (anomaly is ManaDeviceAnomaly device)
                return device.StabilizationProgress > 0f
                    ? string.Format("안정화 진행 {0}%", Mathf.FloorToInt(device.StabilizationProgress * 100f))
                    : "안정화 필요";

            return "대응 필요";
        }

        private static Color StageColor(int stage)
        {
            return stage >= 3 ? new Color(1f, 0.31f, 0.27f)
                : stage == 2 ? new Color(1f, 0.62f, 0.25f)
                : WardShiftHUDLayout.Gold;
        }

        private void ReleaseAllCards()
        {
            foreach (AlertCard card in activeCards.Values)
            {
                if (card.Root != null) card.Root.SetActive(false);
                card.Anomaly = null;
                pooledCards.Push(card);
            }
            activeCards.Clear();
            visibleAlerts.Clear();
        }

        public void ConfigureView(TMP_Text alerts, TMP_Text warning, TMP_Text stability, TMP_Text contamination,
            Image stabilityGauge, Image contaminationGauge, TMP_Text location)
        {
            statusText = alerts;
            warningText = warning;
            stabilityText = stability;
            contaminationText = contamination;
            stabilityFill = stabilityGauge;
            contaminationFill = contaminationGauge;
            locationText = location;
            EnsureAlertHierarchy();
        }

        public void ConfigureZoneTracker(FacilityZoneTracker tracker)
        {
            zoneTracker = tracker;
        }

        private sealed class AlertCard
        {
            public readonly GameObject Root;
            public readonly TMP_Text Details;
            public readonly Image Accent;
            public AnomalyBehaviour Anomaly;

            public AlertCard(GameObject root, TMP_Text details, Image accent)
            {
                Root = root;
                Details = details;
                Accent = accent;
            }
        }
    }
}
