using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    // DAY UI가 이미 소유한 Canvas와 일시정지 경로를 재사용한다.
    public sealed partial class Day5HUD
    {
        [Serializable]
        private sealed class TutorialCopy
        {
            [TextArea(2, 3)] public string introduction = "당신은 제17 마력 제어소의 현장 조율사입니다. 앞으로 7일 동안 발생하는 이상현상을 진압하세요.";
            [TextArea(2, 3)] public string controls = "WASD로 이동하고 마우스로 시점을 돌리세요. 1/2/3 또는 마우스 휠로 마법을 선택하세요. 시설 안정도 0 또는 오염도 100이면 근무에 실패합니다.";
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
            [TextArea(2, 3)] public string supply = "DAY 클리어 뒤 마력 조각으로 필요한 정화 물약이나 시설 수리 키트를 구매하세요. 보급품은 하루 최대 2개까지 구매할 수 있습니다.";
            [TextArea(2, 3)] public string firstClear = "진압으로 얻은 마력 결정으로 강화를 구매하세요. 강화는 하루에 한 번만 구매할 수 있습니다.";
            [TextArea(2, 3)] public string inventory = "B로 인벤토리를 열고 필요한 아이템을 클릭해 사용하세요.";
            [TextArea(2, 3)] public string map = "TAB으로 시설 지도를 열어 이상현상 위치를 확인하세요. 지도는 간이도이며 이동 경로의 축척과 다릅니다.";
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
        private DayTutorialController tutorial;
        private MagicCaster tutorialCaster;
        private MagicDetector tutorialDetector;

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

        private void BuildTutorialUI()
        {
            if (overlayRoot == null) return;
            EnsureGrowthTextDefaults();
            tutorial = new DayTutorialController();
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
            if (tutorial == null || tutorial.ResetPending || tutorialPanel == null || dayFlow == null) return;
            // DAY 1 목표와 조작은 새 브리핑에서 안내하므로 기존 인트로 모달이 다시 일시정지하지 않게 합니다.
            if (tutorial.ShouldBeginIntroduction(dayFlow)) BeginIntroduction();

            DayTutorialController.Hint hint = tutorial.Tick(dayFlow, tutorialCaster, tutorialDetector);
            RefreshControlsReference();
            string message = GetTutorialMessage(hint);
            tutorialPanel.SetActive(!string.IsNullOrEmpty(message));
            if (message != null) tutorialText.text = message;
        }

        private string GetTutorialMessage(DayTutorialController.Hint hint)
        {
            switch (hint)
            {
                case DayTutorialController.Hint.SecondSeal: return tutorialCopy.secondSeal;
                case DayTutorialController.Hint.StageTwo: return tutorialCopy.stageTwo;
                case DayTutorialController.Hint.StageThree: return tutorialCopy.stageThree;
                case DayTutorialController.Hint.Rupture: return tutorialCopy.rupture;
                case DayTutorialController.Hint.SealComplete: return tutorialCopy.sealComplete;
                case DayTutorialController.Hint.Sense: return tutorialCopy.sense;
                case DayTutorialController.Hint.SelectSeal: return tutorialCopy.selectSeal;
                case DayTutorialController.Hint.FirstSeal: return tutorialCopy.firstSeal;
                case DayTutorialController.Hint.CurseSelect: return tutorialCopy.curseSelect;
                case DayTutorialController.Hint.CurseCast: return tutorialCopy.curseCast;
                case DayTutorialController.Hint.DeviceSelect: return tutorialCopy.deviceSelect;
                case DayTutorialController.Hint.DeviceCast: return tutorialCopy.deviceCast;
                case DayTutorialController.Hint.Inventory: return tutorialCopy.inventory;
                case DayTutorialController.Hint.Map: return tutorialCopy.map;
                default: return null;
            }
        }

        private void BeginIntroduction()
        {
            tutorial.BeginIntroduction();
            ShowIntroPage(0);
            introPanel.SetActive(true);
            dayFlow.Status.PauseGameForUI();
        }

        private void ShowIntroPage(int page)
        {
            introText.fontSize = page == 2 || page == 3 ? 22f : 27f;
            introText.text = page == 0 ? tutorialCopy.introduction
                : page == 1 ? tutorialCopy.controls
                : page == 2 ? tutorialCopy.stabilityOverview
                : page == 3 ? tutorialCopy.contaminationOverview : tutorialCopy.growthOverview;
            introButtonText.text = page == 4 ? "근무 시작" : "다음";
        }

        private void ApplyCombatHintPosition()
        {
            if (tutorialPanel != null)
                tutorialPanel.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(0f, -85f - combatHintDownPixels);
        }

        private void AdvanceIntroduction()
        {
            int page = tutorial.AdvanceIntroduction();
            if (page >= 0)
            {
                ShowIntroPage(page);
                return;
            }
            introPanel.SetActive(false);
            dayFlow.Status.ResumeGameFromUI();
        }

        private void RefreshControlsReference()
        {
            if (controlsReference == null) return;
            controlsReference.text = "WASD : 이동\n마법 선택 : 1/2/3 · 마우스 휠"
                + (tutorial.Learned(DayTutorialController.Seal) ? "\nQ : 감지" : "")
                + (tutorial.Learned(DayTutorialController.Map) || tutorial.Learned(DayTutorialController.Inventory)
                    ? "\nTAB : 지도\nB : 인벤토리" : "");
            FitControlsReference();
        }

        private void FitControlsReference()
        {
            RectTransform textRect = controlsReference.rectTransform;
            if (!(textRect.parent is RectTransform panelRect)) return;

            float preferredHeight = controlsReference.GetPreferredValues(
                controlsReference.text, Mathf.Max(1f, textRect.rect.width), 0f).y;
            float textHeight = Mathf.Ceil(preferredHeight + 8f);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight + 24f);
            controlsReference.overflowMode = TextOverflowModes.Overflow;
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
            if (tutorial != null) tutorial.ResetProgress();
            else DayTutorialController.ClearSavedProgress();
        }

    }
}
