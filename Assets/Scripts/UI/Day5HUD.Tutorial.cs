using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    // DAY UI가 이미 소유한 Canvas와 일시정지 경로를 재사용한다.
    public sealed partial class Day5HUD
    {
        private const string TutorialSaveKey = "WardShift.Tutorial.Completed.v1";
        private const int Intro = 1 << 0, Seal = 1 << 1, Clear = 1 << 2,
            Supply = 1 << 3, Curse = 1 << 4, Inventory = 1 << 5,
            Map = 1 << 6, Device = 1 << 7, Radial = 1 << 8,
            Projectile = 1 << 10, FirstStageTwo = 1 << 12, FirstStageThree = 1 << 13,
            FirstRupture = 1 << 14;

        [Serializable]
        private sealed class TutorialCopy
        {
            [TextArea(2, 3)] public string introduction = "당신은 제17 마력 제어소의 현장 조율사입니다. 앞으로 7일 동안 발생하는 이상현상을 진압하세요.";
            [TextArea(2, 3)] public string controls = "WASD로 이동하고 마우스로 시점을 돌리세요. Shift로 달릴 수 있으며, 시설 안정도 0 또는 오염도 100이면 근무에 실패합니다.";
            [InspectorName("시설 안정도 안내"), TextArea(4, 6)] public string stabilityOverview =
                "시설 안정도\n이상현상이 2단계 이상으로 성장하면 시설 안정도가 지속적으로 감소합니다. 현상이 파열되면 큰 피해를 입습니다.\n\n시설 안정도가 0이 되면 임무 실패! 수리 키트로 회복할 수 있습니다.";
            [InspectorName("마력 오염도 안내"), TextArea(4, 6)] public string contaminationOverview =
                "마력 오염도\n2단계 이상 마력 균열 근처나 저주 오염 구역에 머무르면 오염도가 증가합니다. 마력 장치의 폭주 공격에 맞아도 오염됩니다.\n\n오염도가 최대치에 도달하면 임무 실패! 정화약으로 회복할 수 있습니다.";
            [TextArea(2, 3)] public string sense = "마력 균열이 발생했습니다. 주변의 봉인점을 찾아 Q를 눌러보세요.";
            [TextArea(2, 3)] public string selectSeal = "봉인점이 보이면 1번을 눌러 봉인 마법을 선택하세요.";
            [TextArea(2, 3)] public string firstSeal = "첫 번째 봉인점을 조준해 좌클릭하세요.";
            [TextArea(2, 3)] public string secondSeal = "첫 봉인점이 사라졌습니다. 2초 안에 두 번째 봉인점을 조준해 좌클릭하세요.";
            [TextArea(1, 2)] public string sealComplete = "봉인 완료! 균열을 진압했습니다.";
            [TextArea(2, 3)] public string curseSelect = "저주 오염이 퍼지고 있습니다. 2번을 눌러 정화 마법을 선택하세요.";
            [TextArea(2, 3)] public string curseCast = "오염 구역에 가까이 가서 좌클릭을 유지하세요. 누른채로 여러 구역을 연속 진압할 수 있습니다.";
            [TextArea(2, 3)] public string deviceSelect = "마력 장치 폭주가 일어났습니다. 3번을 눌러 안정화 마법을 선택하세요.";
            [TextArea(2, 3)] public string deviceCast = "장치를 조준해 좌클릭을 유지하세요. 중간에 멈춰도 안정화 진행도는 유지됩니다.";
            [TextArea(2, 3)] public string radial = "원형 방출 경고가 나타났습니다. 방출 전에 표시된 반경 밖으로 피하세요.";
            [TextArea(2, 3)] public string projectile = "마력탄 발사 경고입니다. 먼저 거리를 벌리고, 발사 뒤에는 방향을 바꿔 피하세요.";
            [TextArea(2, 3)] public string supply = "정화약은 오염도, 수리 키트는 시설 안정도를 회복합니다. 오늘 필요한 자원을 고르세요.";
            [TextArea(2, 3)] public string firstClear = "진압으로 얻은 마력 결정으로 강화를 구매하세요. 강화는 하루에 한 번만 구매할 수 있습니다.";
            [TextArea(2, 3)] public string inventory = "B로 인벤토리를 열고 필요한 아이템을 클릭해 사용하세요.";
            [TextArea(2, 3)] public string map = "M으로 시설 지도를 열어 이상현상 위치를 확인하세요. 지도는 간이도이며 이동 경로의 축척과 다릅니다.";
            [InspectorName("성장 기본 규칙"), TextArea(2, 3)] public string growthOverview = "이상현상은 시간이 지나면 최대 3단계까지 성장합니다. 늦게 진압할수록 결정 보상은 늘지만, 2단계부터 시설 안정도가 감소합니다.";
            [InspectorName("첫 2단계 알림"), TextArea(2, 3)] public string stageTwo = "이상현상이 2단계로 성장해 시설 안정도가 감소하기 시작합니다. 정상 진압 기본 보상은 결정 2개입니다.";
            [InspectorName("첫 3단계 알림"), TextArea(2, 3)] public string stageThree = "이상현상이 3단계로 성장해 시설 안정도가 더 빠르게 감소합니다. 정상 진압 기본 보상은 결정 3개입니다.";
            [InspectorName("첫 파열 알림"), TextArea(2, 3)] public string rupture = "이상현상이 파열되어 시설에 큰 피해를 주고 사라졌습니다. 파열된 이상현상에는 진압 보상이 없습니다.";
        }

        [Header("첫 플레이 안내 문구 (Inspector에서 수정)")]
        [Tooltip("문구 일부의 서식 예: <size=30><color=#62E9F4>시설 안정도</color></size>. 다른 글자는 그대로 유지됩니다.")]
        [SerializeField] private TutorialCopy tutorialCopy = new TutorialCopy();
        [Header("튜토리얼 UI 위치")]
        [InspectorName("전투 안내창 아래로 이동 (px)")]
        [Tooltip("숫자를 높일수록 전투 중 나타나는 안내창 전체가 아래로 이동합니다. Play Mode에서도 바로 미리 볼 수 있습니다.")]
        [SerializeField, Range(0f, 400f)] private float combatHintDownPixels = 180f;
        private GameObject tutorialPanel;
        private TMP_Text tutorialText;
        private GameObject introPanel;
        private TMP_Text introText;
        private TMP_Text introButtonText;
        private TMP_Text controlsReference;
        private int tutorialFlags;
        private int introPage = -1;
        private bool tutorialResetPending;
        private bool previousSupplyOpen;
        private bool previousRadialWarning;
        private bool previousProjectileWarning;
        private readonly List<ManaRift> watchedRifts = new List<ManaRift>();
        private readonly List<AnomalyBehaviour> watchedOtherAnomalies = new List<AnomalyBehaviour>();
        private readonly List<AnomalyBehaviour> watchedGrowthAnomalies = new List<AnomalyBehaviour>();
        private readonly Queue<int> growthNoticeQueue = new Queue<int>();
        private int queuedGrowthFlags;
        private int activeGrowthNotice;
        private float growthNoticeRemaining;
        private bool showRuptureOnFailure;
        private bool wasFailedLastFrame;
        private MagicCaster tutorialCaster;
        private MagicDetector tutorialDetector;
        private float completionUntil;

        private bool Learned(int flag) => (tutorialFlags & flag) != 0;

        private void OnValidate() => EnsureGrowthTextDefaults();

        private void EnsureGrowthTextDefaults()
        {
            if (tutorialCopy == null) tutorialCopy = new TutorialCopy();
            TutorialCopy defaults = new TutorialCopy();
            if (tutorialCopy.growthOverview == null) tutorialCopy.growthOverview = defaults.growthOverview;
            if (string.IsNullOrEmpty(tutorialCopy.stabilityOverview))
                tutorialCopy.stabilityOverview = defaults.stabilityOverview;
            if (string.IsNullOrEmpty(tutorialCopy.contaminationOverview))
                tutorialCopy.contaminationOverview = defaults.contaminationOverview;
            if (tutorialCopy.stageTwo == null) tutorialCopy.stageTwo = defaults.stageTwo;
            if (tutorialCopy.stageThree == null) tutorialCopy.stageThree = defaults.stageThree;
            if (tutorialCopy.rupture == null) tutorialCopy.rupture = defaults.rupture;
        }

        private void Learn(int flag)
        {
            if (Learned(flag)) return;
            tutorialFlags |= flag;
            PlayerPrefs.SetInt(TutorialSaveKey, tutorialFlags);
            PlayerPrefs.Save();
        }

        private void BuildTutorialUI()
        {
            if (overlayRoot == null) return;
            EnsureGrowthTextDefaults();
            tutorialFlags = PlayerPrefs.GetInt(TutorialSaveKey, 0);
            tutorialCaster = FindFirstObjectByType<MagicCaster>();
            tutorialDetector = FindFirstObjectByType<MagicDetector>();
            Transform parent = overlayRoot;
            GameObject banner = CreatePanel("TutorialBanner", parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -85f), new Vector2(850f, 84f),
                new Color(0.025f, 0.055f, 0.09f, 0.94f));
            banner.GetComponent<Image>().raycastTarget = false;
            tutorialText = CreateText("Message", banner.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-40f, -12f),
                22f, TextAlignmentOptions.Center, Color.white);
            tutorialText.richText = true;
            tutorialPanel = banner;
            ApplyCombatHintPosition();
            banner.SetActive(false);

            introPanel = CreatePanel("TutorialIntro", parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 315f),
                new Color(0.025f, 0.055f, 0.09f, 0.98f));
            introText = CreateText("Message", introPanel.transform,
                new Vector2(0f, 0.3f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -12f), new Vector2(-70f, -20f), 27f,
                TextAlignmentOptions.Center, Color.white);
            introText.richText = true;
            Button next = CreateActionButton("Continue", introPanel.transform,
                new Vector2(0f, 32f), new Vector2(250f, 56f), "다음",
                AdvanceIntroduction, true, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            introButtonText = next.GetComponentInChildren<TMP_Text>();
            introPanel.SetActive(false);

            WardShiftHUDLayout layout = WardShiftHUDLayout.Ensure(koreanFont);
            controlsReference = layout != null
                ? layout.transform.Find("HUD/Controls/Keys")?.GetComponent<TMP_Text>() : null;
            RefreshControlsReference();
        }

        private void UpdateTutorial()
        {
            ApplyCombatHintPosition();
            if (tutorialResetPending || tutorialPanel == null || dayFlow == null) return;
            if (dayFlow.State == DayState.Playing && dayFlow.CurrentDay == 1
                && !Learned(Intro) && introPage < 0 && AnomalyBehaviour.ActiveAnomalies.Count == 0
                && !dayFlow.SupplyPanelOpen && !dayFlow.InventoryOpen && !dayFlow.IsMapOpen)
                BeginIntroduction();

            if (dayFlow.CurrentDay > 1) Learn(Clear);
            if (dayFlow.State == DayState.DayFailed)
            {
                if (showRuptureOnFailure) Learn(FirstRupture);
                wasFailedLastFrame = true;
            }
            else if (wasFailedLastFrame)
            {
                showRuptureOnFailure = false;
                wasFailedLastFrame = false;
            }

            if (dayFlow.InventoryOpen) Learn(Inventory); // 기존 창에서 아이템 클릭 사용을 표시.
            if (dayFlow.IsMapOpen) Learn(Map); // 기존 지도 제목과 범례를 재사용.
            if (previousSupplyOpen && !dayFlow.SupplyPanelOpen) Learn(Supply);
            previousSupplyOpen = dayFlow.SupplyPanelOpen;

            RefreshControlsReference();
            if (introPage >= 0 || dayFlow.State != DayState.Playing || dayFlow.InventoryOpen
                || dayFlow.IsMapOpen || dayFlow.SupplyPanelOpen || Time.timeScale <= 0f)
            {
                tutorialPanel.SetActive(false);
                return;
            }

            ManaRift rift = null;
            CurseAnomaly curse = null;
            ManaDeviceAnomaly device = null;
            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
            {
                if (rift == null && anomaly is ManaRift activeRift && activeRift.IsHazardActive) rift = activeRift;
                if (curse == null && anomaly is CurseAnomaly activeCurse && activeCurse.IsHazardActive) curse = activeCurse;
                if (device == null && anomaly is ManaDeviceAnomaly activeDevice && activeDevice.IsHazardActive) device = activeDevice;
            }
            if (!Learned(Seal))
            {
                foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
                    if (anomaly is ManaRift activeRift && !watchedRifts.Contains(activeRift))
                    {
                        activeRift.Resolved += OnTutorialRiftResolved;
                        watchedRifts.Add(activeRift);
                    }
            }
            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
                if (((anomaly is CurseAnomaly && !Learned(Curse))
                    || (anomaly is ManaDeviceAnomaly && !Learned(Device)))
                    && !watchedOtherAnomalies.Contains(anomaly))
                {
                    anomaly.ResolutionCompleted += OnTutorialAnomalyResolved;
                    watchedOtherAnomalies.Add(anomaly);
                }
            ObserveGrowth();

            MagicCaster caster = tutorialCaster;
            MagicDetector detector = tutorialDetector;
            string message = null;
            bool radialWarning = device != null && device.IsBurstWarningActive
                && device.NextAttack == ManaDeviceAnomaly.AttackKind.RadialBurst;
            bool projectileWarning = device != null && device.IsBurstWarningActive
                && device.NextAttack == ManaDeviceAnomaly.AttackKind.TrackingBolt;
            bool sealChainUrgent = !Learned(Seal) && rift != null && rift.IsSealChainInProgress;
            string growthMessage = NextGrowthNotice(
                (!Learned(Projectile) && projectileWarning)
                || (!Learned(Radial) && radialWarning) || sealChainUrgent);
            if (!Learned(Projectile) && projectileWarning) message = tutorialCopy.projectile;
            else if (!Learned(Radial) && radialWarning) message = tutorialCopy.radial;
            else if (sealChainUrgent) message = tutorialCopy.secondSeal;
            else if (growthMessage != null) message = growthMessage;
            else if (Time.unscaledTime < completionUntil) message = tutorialCopy.sealComplete;
            else if (!Learned(Seal) && rift != null)
            {
                if (detector == null || !detector.IsSensing
                    || rift.FirstSealPoint == null || !rift.FirstSealPoint.IsDetected)
                    message = tutorialCopy.sense;
                else if (caster != null && caster.SelectedMagic != MagicType.Seal) message = tutorialCopy.selectSeal;
                else message = tutorialCopy.firstSeal;
            }
            else if (!Learned(Curse) && curse != null)
            {
                if (caster != null && caster.SelectedMagic != MagicType.Purify) message = tutorialCopy.curseSelect;
                else message = tutorialCopy.curseCast;
            }
            else if (!Learned(Device) && device != null)
            {
                if (caster != null && caster.SelectedMagic != MagicType.Stabilize) message = tutorialCopy.deviceSelect;
                else message = tutorialCopy.deviceCast;
            }
            if (previousRadialWarning && !radialWarning) Learn(Radial);
            if (previousProjectileWarning && !projectileWarning) Learn(Projectile);
            previousRadialWarning = radialWarning;
            previousProjectileWarning = projectileWarning;
            if (message == null && rift == null && curse == null && device == null)
            {
                if (!Learned(Inventory) && dayFlow.CurrentDay >= 2 && !dayFlow.DaySupplyPending)
                    message = tutorialCopy.inventory;
                else if (!Learned(Map) && Learned(Intro) && dayFlow.CurrentDay == 1)
                    message = tutorialCopy.map;
            }
            tutorialPanel.SetActive(!string.IsNullOrEmpty(message));
            if (message != null) tutorialText.text = message;
        }

        private void OnTutorialRiftResolved(ManaRift rift)
        {
            Learn(Seal);
            completionUntil = Time.unscaledTime + 2.2f;
            foreach (ManaRift watched in watchedRifts)
                if (watched != null) watched.Resolved -= OnTutorialRiftResolved;
            watchedRifts.Clear();
        }

        private void OnTutorialAnomalyResolved(AnomalyBehaviour anomaly)
        {
            if (anomaly is CurseAnomaly) Learn(Curse);
            else if (anomaly is ManaDeviceAnomaly) Learn(Device);
            if (anomaly != null) anomaly.ResolutionCompleted -= OnTutorialAnomalyResolved;
            watchedOtherAnomalies.Remove(anomaly);
        }

        private void ObserveGrowth()
        {
            IReadOnlyList<AnomalyBehaviour> anomalies = AnomalyBehaviour.ActiveAnomalies;
            foreach (AnomalyBehaviour anomaly in anomalies)
            {
                if (anomaly == null || !anomaly.IsHazardActive) continue;
                if (anomaly.DangerStage >= 2) QueueGrowthNotice(FirstStageTwo);
                if (anomaly.DangerStage >= 3) QueueGrowthNotice(FirstStageThree);
                if (!Learned(FirstRupture) && !watchedGrowthAnomalies.Contains(anomaly))
                {
                    anomaly.Ended += OnGrowthAnomalyEnded;
                    watchedGrowthAnomalies.Add(anomaly);
                }
            }
        }

        private void OnGrowthAnomalyEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            if (reason == AnomalyEndReason.Ruptured && !Learned(FirstRupture))
            {
                if (anomaly != null && anomaly.DangerStage >= 2) QueueGrowthNotice(FirstStageTwo);
                if (anomaly != null && anomaly.DangerStage >= 3) QueueGrowthNotice(FirstStageThree);
                showRuptureOnFailure = true;
                QueueGrowthNotice(FirstRupture);
            }
            if (anomaly != null) anomaly.Ended -= OnGrowthAnomalyEnded;
            watchedGrowthAnomalies.Remove(anomaly);
        }

        private void QueueGrowthNotice(int flag)
        {
            if (Learned(flag) || activeGrowthNotice == flag || (queuedGrowthFlags & flag) != 0) return;
            growthNoticeQueue.Enqueue(flag);
            queuedGrowthFlags |= flag;
        }

        private string NextGrowthNotice(bool urgentHintVisible)
        {
            if (urgentHintVisible) return null;
            while (activeGrowthNotice == 0 && growthNoticeQueue.Count > 0)
            {
                int flag = growthNoticeQueue.Dequeue();
                queuedGrowthFlags &= ~flag;
                if (Learned(flag)) continue;
                activeGrowthNotice = flag;
                growthNoticeRemaining = 3f;
                Learn(flag);
                if (flag == FirstRupture) showRuptureOnFailure = false;
            }
            if (activeGrowthNotice == 0) return null;
            string message = activeGrowthNotice == FirstStageTwo ? tutorialCopy.stageTwo
                : activeGrowthNotice == FirstStageThree ? tutorialCopy.stageThree
                : tutorialCopy.rupture;
            growthNoticeRemaining -= Time.unscaledDeltaTime;
            if (growthNoticeRemaining <= 0f) activeGrowthNotice = 0;
            return message;
        }

        private void BeginIntroduction()
        {
            SetIntroPage(0, tutorialCopy.introduction, "다음");
            introPanel.SetActive(true);
            dayFlow.Status.PauseGameForUI();
        }

        private void SetIntroPage(int page, string message, string buttonLabel)
        {
            introPage = page;
            introText.fontSize = page == 2 || page == 3 ? 22f : 27f;
            introText.text = message;
            introButtonText.text = buttonLabel;
        }

        private void ApplyCombatHintPosition()
        {
            if (tutorialPanel != null)
                tutorialPanel.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(0f, -85f - combatHintDownPixels);
        }

        private void AdvanceIntroduction()
        {
            if (introPage == 0)
            {
                SetIntroPage(1, tutorialCopy.controls, "다음");
                return;
            }
            if (introPage == 1)
            {
                SetIntroPage(2, tutorialCopy.stabilityOverview, "다음");
                return;
            }
            if (introPage == 2)
            {
                SetIntroPage(3, tutorialCopy.contaminationOverview, "다음");
                return;
            }
            if (introPage == 3)
            {
                SetIntroPage(4, tutorialCopy.growthOverview, "근무 시작");
                return;
            }
            introPage = -2;
            introPanel.SetActive(false);
            Learn(Intro);
            dayFlow.Status.ResumeGameFromUI();
        }

        private void RefreshControlsReference()
        {
            if (controlsReference == null) return;
            controlsReference.text = "WASD 이동 · 마우스 시점\nShift 달리기"
                + (Learned(Seal) ? "\nQ 감응" : "")
                + (Learned(Map) || Learned(Inventory) ? "\nM 지도 · B 인벤토리" : "");
        }

        private void FitOverlaysToScreen()
        {
            if (overlayRoot == null || !(overlayRoot.parent is RectTransform canvasRect)) return;
            float scale = Mathf.Min(1f, canvasRect.rect.width / 1200f, canvasRect.rect.height / 760f);
            overlayRoot.localScale = Vector3.one * Mathf.Max(0.45f, scale);
        }

        // 외부 테스트: B 인벤토리의 버튼을 누른 뒤 씬/게임을 재시작한다.
        private void ResetTutorialProgress()
        {
            ClearTutorialSave();
            Transform label = inventoryPanel.transform.Find("ResetTutorial/Label");
            if (label != null && label.TryGetComponent(out TMP_Text text))
                text.text = "초기화 완료 · 씬 재시작";
        }

        [ContextMenu("튜토리얼 기록 초기화 (다음 실행에 적용)")]
        private void ClearTutorialSave()
        {
            PlayerPrefs.DeleteKey(TutorialSaveKey);
            PlayerPrefs.Save();
            if (Application.isPlaying) tutorialResetPending = true;
        }

        private void OnDestroy()
        {
            foreach (ManaRift watched in watchedRifts)
                if (watched != null) watched.Resolved -= OnTutorialRiftResolved;
            foreach (AnomalyBehaviour watched in watchedOtherAnomalies)
                if (watched != null) watched.ResolutionCompleted -= OnTutorialAnomalyResolved;
            foreach (AnomalyBehaviour watched in watchedGrowthAnomalies)
                if (watched != null) watched.Ended -= OnGrowthAnomalyEnded;
        }
    }
}
