using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>단계 상승, 특별 출현, 장치 공격의 비차단형 화면 경고를 관리합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10012)]
    public sealed class ThreatWarningHUD : MonoBehaviour
    {
        private readonly Dictionary<int, int> observedStages = new Dictionary<int, int>();
        private readonly Dictionary<int, int> pendingStageNotices = new Dictionary<int, int>();
        private readonly HashSet<int> activeStageNoticeIds = new HashSet<int>();
        private readonly HashSet<int> seenAnomalyIds = new HashSet<int>();
        private readonly Dictionary<int, ManaDeviceAnomaly.AttackKind> currentDeviceWarnings =
            new Dictionary<int, ManaDeviceAnomaly.AttackKind>();
        private readonly Dictionary<int, ManaDeviceAnomaly.AttackKind> previousDeviceWarnings =
            new Dictionary<int, ManaDeviceAnomaly.AttackKind>();
        private readonly List<int> staleIds = new List<int>();
        private readonly Image[] edgeImages = new Image[8];

        private WardShiftHUDLayout layout;
        private DayFlowManager dayFlow;
        private AnomalySpawner anomalySpawner;
        private ContaminationHitFeedback hitFeedback;
        private RectTransform uiRoot;
        private RectTransform edgeRoot;
        private RectTransform bannerRoot;
        private Image bannerBackground;
        private Image bannerAccent;
        private Outline bannerOutline;
        private TMP_Text bannerKicker;
        private TMP_Text bannerHeadline;
        private TMP_Text bannerDetail;
        private AudioSource audioSource;

        private int currentStage;
        private float stageNoticeRemaining;
        private float stageEdgeRemaining;
        private float specialSpawnNoticeRemaining;
        private int specialSpawnNoticeDay;
        private bool specialSpawnWarningSoundPlayed;
        private bool hasAttackWarning;
        private int activeRadialWarnings;
        private int activeProjectileWarnings;
        private Color activeColor = Color.white;

        public void Configure(WardShiftHUDLayout hudLayout, DayFlowManager manager)
        {
            layout = hudLayout;
            dayFlow = manager;
            hitFeedback = GetComponent<ContaminationHitFeedback>();
            EnsureSpawnerSubscription();
            EnsureUI();
        }

        private void OnEnable()
        {
            EnsureSpawnerSubscription();
        }

        private void OnDisable()
        {
            if (anomalySpawner != null)
                anomalySpawner.SpecialManaRiftSpawned -= OnSpecialManaRiftSpawned;
            anomalySpawner = null;
        }

        private void Update()
        {
            if (layout == null) layout = GetComponent<WardShiftHUDLayout>();
            if (layout == null || layout.HUDRoot == null) return;
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            EnsureSpawnerSubscription();
            if (hitFeedback == null) hitFeedback = GetComponent<ContaminationHitFeedback>();
            EnsureUI();

            bool gameplayActive = dayFlow == null || dayFlow.State == DayState.Playing;
            bool uiVisible = gameplayActive && (dayFlow == null
                || (!dayFlow.IsMapOpen && !dayFlow.InventoryOpen && !dayFlow.SupplyPanelOpen));
            seenAnomalyIds.Clear();
            currentDeviceWarnings.Clear();
            activeRadialWarnings = 0;
            activeProjectileWarnings = 0;
            bool newRadialWarning = false;
            bool newProjectileWarning = false;

            IReadOnlyList<AnomalyBehaviour> anomalies = AnomalyBehaviour.ActiveAnomalies;
            for (int i = 0; i < anomalies.Count; i++)
            {
                AnomalyBehaviour anomaly = anomalies[i];
                if (anomaly == null) continue;
                int instanceId = anomaly.GetInstanceID();
                if (!gameplayActive || !anomaly.IsHazardActive)
                {
                    RemoveStageNotice(instanceId);
                    observedStages.Remove(instanceId);
                    continue;
                }

                seenAnomalyIds.Add(instanceId);
                int stage = Mathf.Clamp(anomaly.DangerStage, 1, 3);
                if (observedStages.TryGetValue(instanceId, out int previousStage))
                {
                    if (stage > previousStage && stage >= 2)
                        QueueStageNotice(instanceId, stage);
                    observedStages[instanceId] = stage;
                }
                else observedStages.Add(instanceId, stage);

                if (!(anomaly is ManaDeviceAnomaly device)) continue;
                ManaDeviceAnomaly.AttackKind attack = device.NextAttack;
                float extraLead = attack == ManaDeviceAnomaly.AttackKind.TrackingBolt
                    ? layout.TrackingAttackAlertExtraLeadSeconds
                    : layout.RadialAttackAlertExtraLeadSeconds;
                if (!device.IsBurstWarningActive
                    && (extraLead <= 0f || device.SecondsUntilNextAttack > extraLead)) continue;
                currentDeviceWarnings[instanceId] = attack;
                if (attack == ManaDeviceAnomaly.AttackKind.TrackingBolt)
                    activeProjectileWarnings++;
                else activeRadialWarnings++;

                if (!previousDeviceWarnings.TryGetValue(instanceId, out ManaDeviceAnomaly.AttackKind oldAttack)
                    || oldAttack != attack)
                {
                    if (attack == ManaDeviceAnomaly.AttackKind.TrackingBolt) newProjectileWarning = true;
                    else newRadialWarning = true;
                }
            }

            PruneStaleNotices();
            UpdateAttackWarningHistory();
            hasAttackWarning = gameplayActive
                && (activeRadialWarnings > 0 || activeProjectileWarnings > 0);
            if ((newRadialWarning || newProjectileWarning) && gameplayActive)
                PlayWarningSound(newProjectileWarning
                    ? layout.TrackingBoltWarningSfx : layout.RadialBurstWarningSfx);

            if (hasAttackWarning)
            {
                PauseStageNotice();
                ConfigureAttackBanner();
            }
            else
            {
                if (specialSpawnNoticeRemaining > 0f && currentStage > 0) PauseStageNotice();
                if (specialSpawnNoticeRemaining > 0f && uiVisible)
                {
                    specialSpawnNoticeRemaining = Mathf.Max(0f,
                        specialSpawnNoticeRemaining - Time.unscaledDeltaTime);
                }
                if (specialSpawnNoticeRemaining > 0f)
                {
                    if (uiVisible) ConfigureSpecialSpawnBanner();
                }
                else
                {
                    if (currentStage == 2 && HasPendingStage(3)) PauseStageNotice();
                    if (currentStage > 0 && uiVisible)
                    {
                        stageNoticeRemaining = Mathf.Max(0f, stageNoticeRemaining - Time.unscaledDeltaTime);
                        if (stageNoticeRemaining <= 0f) ClearActiveStageNotice();
                    }
                    if (currentStage == 0 && uiVisible) StartNextStageNotice();
                    if (currentStage > 0) ConfigureStageBanner();
                }
            }

            bool showBanner = uiVisible
                && (hasAttackWarning || specialSpawnNoticeRemaining > 0f || currentStage > 0);
            if (bannerRoot != null) bannerRoot.gameObject.SetActive(showBanner);
            UpdateEdges(uiVisible, hasAttackWarning, currentStage);
        }

        private void EnsureSpawnerSubscription()
        {
            AnomalySpawner foundSpawner = FindFirstObjectByType<AnomalySpawner>();
            if (foundSpawner == anomalySpawner) return;
            if (anomalySpawner != null)
                anomalySpawner.SpecialManaRiftSpawned -= OnSpecialManaRiftSpawned;
            anomalySpawner = foundSpawner;
            if (anomalySpawner != null)
                anomalySpawner.SpecialManaRiftSpawned += OnSpecialManaRiftSpawned;
        }

        private void OnSpecialManaRiftSpawned(ManaRift rift, int day)
        {
            if (rift == null) return;
            specialSpawnNoticeDay = day;
            specialSpawnNoticeRemaining = layout != null
                ? layout.SpecialSpawnWarningDuration : 2.2f;
            specialSpawnWarningSoundPlayed = false;
        }

        private void ConfigureSpecialSpawnBanner()
        {
            if (bannerKicker == null || bannerHeadline == null || bannerDetail == null) return;
            bannerKicker.text = "특별 현상 출현";
            bannerHeadline.text = specialSpawnNoticeDay == 7
                ? "위험: 고농도 마력 균열 발생"
                : "고농도 마력 반응 발생";
            bannerDetail.text = "지도와 Q 마력 감응으로 위치를 확인할 수 있습니다.";
            ApplyBannerStyle(layout.SpecialSpawnWarningColor, false);
            if (specialSpawnWarningSoundPlayed) return;
            PlayWarningSound(layout.SpecialSpawnWarningSfx);
            specialSpawnWarningSoundPlayed = true;
        }

        private void EnsureUI()
        {
            if (layout == null || layout.HUDRoot == null) return;
            Transform parent = layout.transform;
            if (uiRoot != null && uiRoot.parent == parent)
            {
                uiRoot.SetAsLastSibling();
                return;
            }

            Transform existing = parent.Find("ThreatWarningOverlay");
            if (existing != null && RestoreUI(existing))
            {
                bannerRoot.gameObject.SetActive(false);
                SetEdgeColor(Color.clear);
                return;
            }
            if (existing != null) DestroyImmediateIfNeeded(existing.gameObject);

            GameObject rootObject = new GameObject("ThreatWarningOverlay", typeof(RectTransform), typeof(CanvasGroup));
            uiRoot = rootObject.GetComponent<RectTransform>();
            uiRoot.SetParent(parent, false);
            uiRoot.anchorMin = Vector2.zero;
            uiRoot.anchorMax = Vector2.one;
            uiRoot.offsetMin = Vector2.zero;
            uiRoot.offsetMax = Vector2.zero;
            uiRoot.SetAsLastSibling();
            CanvasGroup group = rootObject.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.ignoreParentGroups = false;

            BuildEdges();
            BuildBanner();
            bannerRoot.gameObject.SetActive(false);
        }

        private bool RestoreUI(Transform existing)
        {
            uiRoot = existing as RectTransform;
            if (uiRoot == null) return false;
            edgeRoot = uiRoot.Find("ThreatEdgeCorners") as RectTransform;
            Transform banner = uiRoot.Find("ThreatBanner");
            if (edgeRoot == null || banner == null) return false;
            bannerRoot = banner as RectTransform;
            bannerBackground = banner.GetComponent<Image>();
            bannerOutline = banner.GetComponent<Outline>();
            Transform accent = banner.Find("ThreatAccent");
            Transform kicker = banner.Find("ThreatKicker");
            Transform headline = banner.Find("ThreatHeadline");
            Transform detail = banner.Find("ThreatDetail");
            bannerAccent = accent != null ? accent.GetComponent<Image>() : null;
            bannerKicker = kicker != null ? kicker.GetComponent<TMP_Text>() : null;
            bannerHeadline = headline != null ? headline.GetComponent<TMP_Text>() : null;
            bannerDetail = detail != null ? detail.GetComponent<TMP_Text>() : null;
            string[] edgeNames =
            {
                "TopLeft_H", "TopRight_H", "BottomLeft_H", "BottomRight_H",
                "TopLeft_V", "TopRight_V", "BottomLeft_V", "BottomRight_V"
            };
            for (int i = 0; i < edgeNames.Length; i++)
            {
                Transform edge = edgeRoot.Find(edgeNames[i]);
                edgeImages[i] = edge != null ? edge.GetComponent<Image>() : null;
                if (edgeImages[i] == null) return false;
            }
            if (bannerRoot == null || bannerBackground == null || bannerOutline == null
                || bannerAccent == null || bannerKicker == null || bannerHeadline == null || bannerDetail == null)
                return false;

            CanvasGroup group = uiRoot.GetComponent<CanvasGroup>();
            if (group == null) group = uiRoot.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return true;
        }

        private static void DestroyImmediateIfNeeded(GameObject target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void BuildEdges()
        {
            GameObject edges = new GameObject("ThreatEdgeCorners", typeof(RectTransform));
            edgeRoot = edges.GetComponent<RectTransform>();
            edgeRoot.SetParent(uiRoot, false);
            edgeRoot.anchorMin = Vector2.zero;
            edgeRoot.anchorMax = Vector2.one;
            edgeRoot.offsetMin = Vector2.zero;
            edgeRoot.offsetMax = Vector2.zero;

            float t = layout.ThreatEdgeThickness;
            float length = layout.ThreatEdgeLength;
            edgeImages[0] = MakeEdge(edgeRoot, "TopLeft_H", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -22f - t), new Vector2(22f + length, -22f));
            edgeImages[1] = MakeEdge(edgeRoot, "TopRight_H", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-22f - length, -22f - t), new Vector2(-22f, -22f));
            edgeImages[2] = MakeEdge(edgeRoot, "BottomLeft_H", Vector2.zero, Vector2.zero,
                new Vector2(22f, 22f), new Vector2(22f + length, 22f + t));
            edgeImages[3] = MakeEdge(edgeRoot, "BottomRight_H", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-22f - length, 22f), new Vector2(-22f, 22f + t));
            edgeImages[4] = MakeEdge(edgeRoot, "TopLeft_V", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -22f - length), new Vector2(22f + t, -22f));
            edgeImages[5] = MakeEdge(edgeRoot, "TopRight_V", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-22f - t, -22f - length), new Vector2(-22f, -22f));
            edgeImages[6] = MakeEdge(edgeRoot, "BottomLeft_V", Vector2.zero, Vector2.zero,
                new Vector2(22f, 22f), new Vector2(22f + t, 22f + length));
            edgeImages[7] = MakeEdge(edgeRoot, "BottomRight_V", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-22f - t, 22f), new Vector2(-22f, 22f + length));
        }

        private void BuildBanner()
        {
            GameObject banner = new GameObject("ThreatBanner", typeof(RectTransform), typeof(Image), typeof(Outline));
            bannerRoot = banner.GetComponent<RectTransform>();
            bannerRoot.SetParent(uiRoot, false);
            bannerRoot.anchorMin = bannerRoot.anchorMax = new Vector2(0.5f, 1f);
            bannerRoot.pivot = new Vector2(0.5f, 1f);
            bannerBackground = banner.GetComponent<Image>();
            bannerBackground.color = new Color(0.025f, 0.035f, 0.052f, 0.96f);
            bannerBackground.raycastTarget = false;
            bannerOutline = banner.GetComponent<Outline>();
            bannerOutline.effectDistance = new Vector2(2f, -2f);
            bannerOutline.useGraphicAlpha = true;

            bannerAccent = MakePanel(bannerRoot, "ThreatAccent");
            RectTransform accentRect = (RectTransform)bannerAccent.transform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.offsetMin = new Vector2(13f, 13f);
            accentRect.offsetMax = new Vector2(19f, -13f);

            TMP_FontAsset font = layout.KoreanFont != null ? layout.KoreanFont : TMP_Settings.defaultFontAsset;
            bannerKicker = MakeText(bannerRoot, "ThreatKicker", font,
                layout.ThreatKickerFontSize, 12f, 26f);
            bannerHeadline = MakeText(bannerRoot, "ThreatHeadline", font,
                layout.ThreatHeadlineFontSize, 39f, 45f);
            bannerDetail = MakeText(bannerRoot, "ThreatDetail", font,
                layout.ThreatDetailFontSize, 86f, 31f);
            bannerKicker.color = Color.white;
            bannerHeadline.fontStyle = FontStyles.Bold;
            bannerHeadline.color = Color.white;
            bannerDetail.color = new Color(0.9f, 0.93f, 0.97f);
        }

        private static Image MakeEdge(RectTransform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = item.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            return image;
        }

        private static Image MakePanel(RectTransform parent, string name)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image image = item.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, TMP_FontAsset font,
            float fontSize, float top, float height)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            SetTextGeometry(rect, top, height);
            TMP_Text text = item.GetComponent<TMP_Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void SetTextGeometry(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(34f, -top - height);
            rect.offsetMax = new Vector2(-34f, -top);
        }

        private void QueueStageNotice(int instanceId, int stage)
        {
            pendingStageNotices.Remove(instanceId);
            activeStageNoticeIds.Remove(instanceId);
            if (activeStageNoticeIds.Count == 0) ClearActiveStageNotice();

            if (currentStage == stage)
            {
                activeStageNoticeIds.Add(instanceId);
                RefreshStageCopy();
            }
            else pendingStageNotices[instanceId] = stage;
        }

        private void RemoveStageNotice(int instanceId)
        {
            pendingStageNotices.Remove(instanceId);
            activeStageNoticeIds.Remove(instanceId);
            if (activeStageNoticeIds.Count == 0) ClearActiveStageNotice();
        }

        private void PruneStaleNotices()
        {
            staleIds.Clear();
            foreach (KeyValuePair<int, int> entry in observedStages)
                if (!seenAnomalyIds.Contains(entry.Key)) staleIds.Add(entry.Key);
            for (int i = 0; i < staleIds.Count; i++) observedStages.Remove(staleIds[i]);

            staleIds.Clear();
            foreach (KeyValuePair<int, int> entry in pendingStageNotices)
                if (!seenAnomalyIds.Contains(entry.Key)) staleIds.Add(entry.Key);
            for (int i = 0; i < staleIds.Count; i++) pendingStageNotices.Remove(staleIds[i]);

            staleIds.Clear();
            foreach (int id in activeStageNoticeIds)
                if (!seenAnomalyIds.Contains(id)) staleIds.Add(id);
            for (int i = 0; i < staleIds.Count; i++) activeStageNoticeIds.Remove(staleIds[i]);
            if (currentStage > 0 && activeStageNoticeIds.Count == 0) ClearActiveStageNotice();
        }

        private void UpdateAttackWarningHistory()
        {
            staleIds.Clear();
            foreach (KeyValuePair<int, ManaDeviceAnomaly.AttackKind> entry in previousDeviceWarnings)
                if (!currentDeviceWarnings.ContainsKey(entry.Key)) staleIds.Add(entry.Key);
            for (int i = 0; i < staleIds.Count; i++) previousDeviceWarnings.Remove(staleIds[i]);
            foreach (KeyValuePair<int, ManaDeviceAnomaly.AttackKind> entry in currentDeviceWarnings)
                previousDeviceWarnings[entry.Key] = entry.Value;
        }

        private bool HasPendingStage(int stage)
        {
            foreach (KeyValuePair<int, int> entry in pendingStageNotices)
                if (entry.Value == stage) return true;
            return false;
        }

        private void PauseStageNotice()
        {
            if (currentStage <= 0) return;
            foreach (int id in activeStageNoticeIds)
                pendingStageNotices[id] = currentStage;
            ClearActiveStageNotice();
        }

        private void ClearActiveStageNotice()
        {
            currentStage = 0;
            stageNoticeRemaining = 0f;
            activeStageNoticeIds.Clear();
        }

        private void StartNextStageNotice()
        {
            int nextStage = HasPendingStage(3) ? 3 : HasPendingStage(2) ? 2 : 0;
            if (nextStage == 0) return;
            staleIds.Clear();
            foreach (KeyValuePair<int, int> entry in pendingStageNotices)
                if (entry.Value == nextStage) staleIds.Add(entry.Key);
            for (int i = 0; i < staleIds.Count; i++)
            {
                pendingStageNotices.Remove(staleIds[i]);
                activeStageNoticeIds.Add(staleIds[i]);
            }
            currentStage = nextStage;
            stageNoticeRemaining = layout.StageWarningDuration;
            stageEdgeRemaining = layout.StageEdgeFlashDuration;
            RefreshStageCopy();
            PlayWarningSound(nextStage == 3 ? layout.StageThreeWarningSfx : layout.StageTwoWarningSfx);
        }

        private void RefreshStageCopy()
        {
            if (bannerKicker == null || currentStage < 2) return;
            Color color = currentStage == 3 ? layout.StageThreeWarningColor : layout.StageTwoWarningColor;
            int count = activeStageNoticeIds.Count;
            string countText = count > 1 ? " · " + count + "곳" : string.Empty;
            bannerKicker.text = "위험 단계 상승";
            bannerHeadline.text = currentStage == 3
                ? "이상현상 위험 3단계" + countText
                : "이상현상 위험 2단계" + countText;
            bannerDetail.text = currentStage == 3
                ? "파열까지 남은 시간을 확인하고 즉시 대응하세요."
                : "대응을 서둘러 시설 안정도와 오염 확산을 관리하세요.";
            ApplyBannerStyle(color, false);
        }

        private void ConfigureStageBanner()
        {
            RefreshStageCopy();
        }

        private void ConfigureAttackBanner()
        {
            int radial = activeRadialWarnings;
            int projectile = activeProjectileWarnings;
            if (radial > 0 && projectile > 0)
            {
                activeColor = layout.TrackingBoltWarningColor;
                bannerKicker.text = "장치 공격 · 즉시 회피";
                bannerHeadline.text = "추적 마력탄과 원형 방출 경고";
                bannerDetail.text = $"추적 마력탄 {projectile}곳 · 원형 방출 {radial}곳 — 방향과 위험 범위를 확인하세요.";
            }
            else if (projectile > 0)
            {
                activeColor = layout.TrackingBoltWarningColor;
                bannerKicker.text = "장치 공격 · 즉시 회피";
                bannerHeadline.text = "추적 마력탄 발사 임박";
                bannerDetail.text = projectile > 1
                    ? $"{projectile}곳에서 조준 중 · 발사체의 진행 방향에서 벗어나세요."
                    : "발사체의 조준 방향에서 벗어나세요.";
            }
            else
            {
                activeColor = layout.RadialBurstWarningColor;
                bannerKicker.text = "장치 공격 · 즉시 회피";
                bannerHeadline.text = "원형 방출 공격 임박";
                bannerDetail.text = radial > 1
                    ? $"{radial}곳에서 방출 준비 중 · 모든 위험 범위 밖으로 이동하세요."
                    : "장치 주위 위험 범위 밖으로 즉시 이동하세요.";
            }
            ApplyBannerStyle(activeColor, true);
        }

        private void ApplyBannerStyle(Color color, bool urgent)
        {
            activeColor = color;
            bannerAccent.color = color;
            bannerKicker.color = color;
            bannerHeadline.color = urgent ? Color.white : color;
            bannerDetail.color = new Color(0.92f, 0.94f, 0.98f);
            bannerBackground.color = new Color(0.025f, 0.035f, 0.052f, urgent ? 0.98f : 0.95f);
            float pulse = urgent ? 0.78f + 0.22f * (0.5f + 0.5f
                * Mathf.Sin(Time.unscaledTime * Mathf.Max(0f, layout.ThreatEdgePulseSpeed))) : 0.7f;
            bannerOutline.effectColor = new Color(color.r, color.g, color.b, pulse);
            bannerOutline.enabled = true;
        }

        private void UpdateEdges(bool uiVisible, bool attackWarning, int stage)
        {
            if (!uiVisible)
            {
                SetEdgeColor(Color.clear);
                return;
            }

            bool hitFlash = hitFeedback != null && hitFeedback.IsFlashActive;
            if (attackWarning && !hitFlash)
            {
                float pulse = 0.72f + 0.28f * (0.5f + 0.5f
                    * Mathf.Sin(Time.unscaledTime * Mathf.Max(0f, layout.ThreatEdgePulseSpeed)));
                SetEdgeColor(activeColor, layout.DeviceAttackEdgeIntensity * pulse);
                return;
            }

            if (stage > 0 && !hitFlash && stageEdgeRemaining > 0f)
            {
                stageEdgeRemaining = Mathf.Max(0f, stageEdgeRemaining - Time.unscaledDeltaTime);
                Color stageColor = stage == 3 ? layout.StageThreeWarningColor : layout.StageTwoWarningColor;
                float baseIntensity = stage == 3
                    ? layout.StageThreeEdgeIntensity : layout.StageTwoEdgeIntensity;
                float fade = Mathf.Clamp01(stageEdgeRemaining / Mathf.Max(0.01f, layout.StageEdgeFlashDuration));
                float pulse = 0.65f + 0.35f * (0.5f + 0.5f
                    * Mathf.Sin(Time.unscaledTime * Mathf.Max(0f, layout.ThreatEdgePulseSpeed)));
                SetEdgeColor(stageColor, baseIntensity * fade * pulse);
                return;
            }

            SetEdgeColor(Color.clear);
        }

        private void SetEdgeColor(Color color, float intensity = 0f)
        {
            color.a = Mathf.Clamp01(intensity);
            foreach (Image image in edgeImages)
                if (image != null) image.color = color;
        }

        private void PlayWarningSound(AudioClip clip)
        {
            if (clip == null) return;
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.PlayOneShot(clip);
        }

        private void LateUpdate()
        {
            if (bannerRoot == null) return;
            if (uiRoot != null) uiRoot.SetAsLastSibling();
            if (layout != null)
            {
                bannerKicker.fontSize = layout.ThreatKickerFontSize;
                bannerHeadline.fontSize = layout.ThreatHeadlineFontSize;
                bannerDetail.fontSize = layout.ThreatDetailFontSize;
                float kickerHeight = Mathf.Max(26f, bannerKicker.fontSize * 1.6f);
                float headlineTop = 13f + kickerHeight;
                float headlineHeight = Mathf.Max(45f, bannerHeadline.fontSize * 1.6f);
                float detailTop = headlineTop + headlineHeight + 2f;
                float detailHeight = Mathf.Max(31f, bannerDetail.fontSize * 1.6f);
                SetTextGeometry(bannerKicker.rectTransform, 12f, kickerHeight);
                SetTextGeometry(bannerHeadline.rectTransform, headlineTop, headlineHeight);
                SetTextGeometry(bannerDetail.rectTransform, detailTop, detailHeight);
                Vector2 size = layout.ThreatBannerSize;
                size.y = Mathf.Max(size.y, detailTop + detailHeight + 13f);
                bannerRoot.sizeDelta = size;
                bannerRoot.anchoredPosition = layout.ThreatBannerTopOffset;
            }
            if (edgeRoot != null && layout != null) RefreshEdgeGeometry();
        }

        private void RefreshEdgeGeometry()
        {
            float t = layout.ThreatEdgeThickness;
            float length = layout.ThreatEdgeLength;
            SetEdgeRect(0, new Vector2(0f, 1f), new Vector2(22f, -22f - t), new Vector2(22f + length, -22f));
            SetEdgeRect(1, new Vector2(1f, 1f), new Vector2(-22f - length, -22f - t), new Vector2(-22f, -22f));
            SetEdgeRect(2, Vector2.zero, new Vector2(22f, 22f), new Vector2(22f + length, 22f + t));
            SetEdgeRect(3, new Vector2(1f, 0f), new Vector2(-22f - length, 22f), new Vector2(-22f, 22f + t));
            SetEdgeRect(4, new Vector2(0f, 1f), new Vector2(22f, -22f - length), new Vector2(22f + t, -22f));
            SetEdgeRect(5, new Vector2(1f, 1f), new Vector2(-22f - t, -22f - length), new Vector2(-22f, -22f));
            SetEdgeRect(6, Vector2.zero, new Vector2(22f, 22f), new Vector2(22f + t, 22f + length));
            SetEdgeRect(7, new Vector2(1f, 0f), new Vector2(-22f - t, 22f), new Vector2(-22f, 22f + length));
        }

        private void SetEdgeRect(int index, Vector2 anchor, Vector2 min, Vector2 max)
        {
            if (edgeImages[index] == null) return;
            RectTransform rect = edgeImages[index].rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }
    }
}
