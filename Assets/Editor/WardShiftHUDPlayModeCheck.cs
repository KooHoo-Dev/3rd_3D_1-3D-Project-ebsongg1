using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace WardShift.Editor
{
    /// <summary>실제 MainGame Play Mode의 표시/데이터 계약을 검사합니다. 반사로 만든 시전 상태는 입력 종단 테스트와 구분합니다.</summary>
    [InitializeOnLoad]
    public static class WardShiftHUDPlayModeCheck
    {
        private const string RunKey = "WardShift.HUDCheck.Running";
        private const string Output = "Logs/HUD";
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly StringBuilder results = new StringBuilder();
        private static int phase;
        private static double nextTick;
        private static DayFlowManager day;
        private static WardShiftHUDLayout layout;
        private static MagicCaster caster;
        private static MagicHUD magic;
        private static WardStatusHUD ward;
        private static Day5HUD dayHUD;
        private static AnomalySpawner spawner;
        private static int assertions;
        private static FloodCondition retryCondition;
        private static EditorWindow gameView;
        private static int originalSize;
        private static object sizeGroup;
        private static int customSizeIndex = -1;
        private static Mouse captureMouse;
        private static bool holdCaptureMouse;

        static WardShiftHUDPlayModeCheck()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false))
                {
                    phase = 0;
                    nextTick = EditorApplication.timeSinceStartup + 2;
                    EditorApplication.update -= Tick;
                    EditorApplication.update += Tick;
                }
            };
        }

        [MenuItem("Tools/WARD SHIFT/Run HUD Play Mode Check")]
        public static void RunFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Object.FindFirstObjectByType<DayFlowManager>() == null)
                throw new InvalidOperationException("Open MainGame before running HUD checks.");
            WardShiftHUDSetup.SetupActiveScene();
            results.Clear();
            assertions = 0;
            SessionState.SetBool(RunKey, true);
            Directory.CreateDirectory(Output);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextTick) return;
            nextTick = EditorApplication.timeSinceStartup + 0.75;
            try
            {
                switch (phase++)
                {
                    case 0:
                        day = Object.FindFirstObjectByType<DayFlowManager>();
                        layout = Object.FindFirstObjectByType<WardShiftHUDLayout>();
                        caster = Object.FindFirstObjectByType<MagicCaster>();
                        magic = Object.FindFirstObjectByType<MagicHUD>();
                        ward = Object.FindFirstObjectByType<WardStatusHUD>();
                        dayHUD = Object.FindFirstObjectByType<Day5HUD>();
                        spawner = Object.FindFirstObjectByType<AnomalySpawner>();
                        spawner.SetSpawningEnabled(false);
                        spawner.ClearActiveAnomalies();
                        Cursor.lockState = CursorLockMode.Locked;
                        Set(caster, "hasFocus", true);
                        caster.SetInputBlocked(false);
                        CheckCastingViews();
                        CheckDataAndModals();
                        PrepareMultipleAnomalies();
                        break;
                    case 1:
                        Refresh();
                        CheckBounds();
                        CheckFonts();
                        Capture("hud-anomaly-cards-1920x1080.png");
                        break;
                    case 2:
                        day.ToggleInventory();
                        Refresh();
                        Assert(!layout.HUDRoot.gameObject.activeSelf && Get<GameObject>(dayHUD, "inventoryPanel").activeSelf,
                            "Inventory modal hides gameplay HUD");
                        Capture("hud-inventory.png");
                        break;
                    case 3:
                        day.ToggleInventory();
                        day.ToggleMap();
                        Refresh();
                        Capture("hud-map.png");
                        break;
                    case 4:
                        day.ToggleMap();
                        day.ConfigureFloodConditionTestOverride(true, FloodCondition.EarlySealBonus);
                        day.CheatAdvanceDay();
                        day.DeferDaySupplyChoice();
                        Refresh();
                        Assert(Get<TMP_Text>(layout, "floodText").text.Contains(FloodConditionRules.GetName(day.ActiveFloodCondition)),
                            "DAY transition refreshes actual condition");
                        Assert(VisibleAnomalyCardNames().Length == 0, "Day transition clears anomaly alert cards");
                        retryCondition = day.ActiveFloodCondition;
                        Invoke(day.Status, "Fail", "HUD test failure");
                        Refresh();
                        Assert(!layout.HUDRoot.gameObject.activeSelf && Get<GameObject>(dayHUD, "failedPanel").activeSelf,
                            "FAILED hides gameplay/cast UI");
                        Capture("hud-failed.png");
                        break;
                    case 5:
                        day.RetryCurrentDay();
                        break;
                    case 6:
                        day.DeferDaySupplyChoice();
                        spawner.SetSpawningEnabled(false);
                        Refresh();
                        Assert(day.State == DayState.Playing && day.ActiveFloodCondition == retryCondition,
                            "Retry restores active condition");
                        Assert(Get<TMP_Text>(layout, "floodText").text.Contains(FloodConditionRules.GetName(retryCondition))
                            && !Get<GameObject>(magic, "castPanel").activeSelf, "Retry has current condition and no stale cast");
                        for (int i = day.CurrentDay; i < day.FinalDay; i++)
                        {
                            day.CheatAdvanceDay();
                            day.DeferDaySupplyChoice();
                            Refresh();
                            Assert(Get<TMP_Text>(layout, "floodText").text.Contains(FloodConditionRules.GetName(day.ActiveFloodCondition)),
                                "Late DAY condition refresh " + day.CurrentDay);
                        }
                        Invoke(day, "CompleteDay");
                        Refresh();
                        Assert(day.State == DayState.Ending && Get<GameObject>(dayHUD, "endingPanel").activeSelf
                            && !layout.HUDRoot.gameObject.activeSelf, "Ending panel without stale HUD");
                        Capture("hud-ending.png");
                        break;
                    case 7:
                        // 화면 검증용 상태는 Play Mode 내부에서만 변경하며 씬에 저장하지 않습니다.
                        day.SetDayAndStateForTest(4, DayState.Playing);
                        Invoke(day, "BeginDay", false);
                        day.DeferDaySupplyChoice();
                        spawner.SetSpawningEnabled(false);
                        SetGameSize(1920, 1200);
                        break;
                    case 8:
                        Refresh();
                        CheckBounds();
                        Capture("hud-1920x1200-idle.png");
                        Invoke(day, "CompleteDay");
                        Refresh();
                        Assert(day.State == DayState.DayClear && Get<GameObject>(dayHUD, "clearPanel").activeSelf,
                            "Clear panel is visible for 1080p layout check");
                        SetGameSize(1920, 1080);
                        break;
                    case 9:
                        Refresh();
                        CheckClearOverlayLayout(1920, 1080);
                        Capture("day-clear-1920x1080.png");
                        day.Inventory.AddManaCrystals(50);
                        int previousLevel = day.GetUpgradeLevel(DayUpgradeType.Mobility);
                        int purchasePrice = day.GetNextUpgradePrice(DayUpgradeType.Mobility);
                        int crystalsBeforePurchase = day.Inventory.ManaCrystals;
                        Button purchase = Get<GameObject>(dayHUD, "clearPanel").transform
                            .Find("Upgrade_Mobility/Purchase").GetComponent<Button>();
                        Assert(purchase.interactable, "Upgrade purchase button is enabled when affordable");
                        purchase.onClick.Invoke();
                        Refresh();
                        Assert(day.GetUpgradeLevel(DayUpgradeType.Mobility) == previousLevel + 1
                            && day.Inventory.ManaCrystals == crystalsBeforePurchase - purchasePrice,
                            "Upgrade button buys the level and spends its actual price");
                        Assert(day.UpgradePurchasedThisClear
                            && !Get<GameObject>(dayHUD, "clearPanel").transform.Find("Upgrade_ContaminationResistance/Purchase")
                                .GetComponent<Button>().interactable
                            && !Get<GameObject>(dayHUD, "clearPanel").transform.Find("Upgrade_FieldPurification/Purchase")
                                .GetComponent<Button>().interactable,
                            "One-purchase-per-clear limit remains active");
                        int clearDay = day.CurrentDay;
                        Get<GameObject>(dayHUD, "clearPanel").transform.Find("NextDay").GetComponent<Button>().onClick.Invoke();
                        day.DeferDaySupplyChoice();
                        Refresh();
                        Assert(day.State == DayState.Playing && day.CurrentDay == clearDay + 1,
                            "Next-day button advances the actual day");
                        Invoke(day, "CompleteDay");
                        Refresh();
                        SetGameSize(1280, 720);
                        break;
                    case 10:
                        Refresh();
                        CheckClearOverlayLayout(1280, 720);
                        Capture("day-clear-1280x720.png");
                        break;
                    case 11:
                        int secondClearDay = day.CurrentDay;
                        Button nextDayAt720p = Get<GameObject>(dayHUD, "clearPanel").transform
                            .Find("NextDay").GetComponent<Button>();
                        Assert(nextDayAt720p.interactable, "Next-day button is enabled at 720p");
                        nextDayAt720p.onClick.Invoke();
                        day.DeferDaySupplyChoice();
                        Refresh();
                        Assert(day.State == DayState.Playing && day.CurrentDay == secondClearDay + 1,
                            "Next-day button advances the actual day at 720p");
                        SetGameSize(1920, 1080);
                        if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState());
                        captureMouse = InputSystem.AddDevice<Mouse>("HUDCheckMouse");
                        InputSystem.QueueStateEvent(captureMouse, new MouseState());
                        break;
                    case 12:
                        PrepareCastScreenshot();
                        break;
                    case 13:
                        Assert(caster.CastingTarget != null && Get<GameObject>(magic, "castPanel").activeInHierarchy,
                            "Real mouse input and raycast drive visible stabilization gauge"
                            + $" (blocked={caster.InputBlocked}, waiting={caster.WaitingForRelease}, aimed={caster.AimedTarget?.DisplayName}, pressed={Get<InputAction>(caster, "castAction").IsPressed()})");
                        Capture("hud-stabilize-casting.png");
                        break;
                    default:
                        Finish(true, "All HUD presentation checks passed.");
                        break;
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void CheckCastingViews()
        {
            var targetObject = new GameObject("HUD_Check_Target");
            MagicTarget target = targetObject.AddComponent<MagicTarget>();
            PlayerInteractor interactor = Get<PlayerInteractor>(caster, "interactor");
            for (int i = 0; i < 3; i++)
            {
                target.ResetForNewUse();
                Set(target, "requiredMagic", (MagicType)i);
                Set(target, "useTimingCast", i == 0);
                Set(target, "preserveProgressOnCancel", i == 2);
                Set(target, "allowHeldPurifyChain", i == 1);
                Set(target, "deactivateOnComplete", false);
                caster.SetInputBlocked(false);
                caster.SelectMagic((MagicType)i);
                Set(interactor, "currentTarget", target);
                Set(caster, "castingTarget", null);
                Invoke(magic, "LateUpdate");
                Assert(!Get<GameObject>(magic, "castPanel").activeSelf, "Aim-only gauge hidden " + i);
                target.AdvanceCast((MagicType)i, target.CastDuration * 0.35f);
                Set(caster, "castingTarget", target);
                Invoke(magic, "LateUpdate");
                Assert(Get<GameObject>(magic, "castPanel").activeSelf, "Casting gauge shown " + i);
                Assert(Mathf.Approximately(Get<Image>(magic, "progressFill").fillAmount, target.Progress), "Real target progress " + i);
                Assert(Get<GameObject>(magic, "timingTrack").activeSelf == (i == 0), "Timing zones only for timing cast " + i);
                if (i == 0)
                {
                    Assert(Mathf.Approximately(Get<Image>(magic, "stableZone").rectTransform.anchorMin.x, target.StableStart),
                        "Seal timing interval uses target configuration");
                }
                if (i == 1)
                {
                    var secondObject = new GameObject("HUD_Check_SecondPatch");
                    MagicTarget second = secondObject.AddComponent<MagicTarget>();
                    Set(second, "requiredMagic", MagicType.Purify);
                    Set(second, "useTimingCast", false);
                    second.AdvanceCast(MagicType.Purify, second.CastDuration * 0.15f);
                    Set(caster, "castingTarget", second);
                    Invoke(magic, "LateUpdate");
                    Assert(Get<GameObject>(magic, "castPanel").activeSelf
                        && Mathf.Approximately(Get<Image>(magic, "progressFill").fillAmount, 0.15f), "Patch switch updates without gauge gap");
                    Object.DestroyImmediate(secondObject);
                    Set(caster, "castingTarget", target);
                }
                caster.SetInputBlocked(true); // 실제 취소 경로를 사용합니다.
                Invoke(magic, "LateUpdate");
                Assert(!Get<GameObject>(magic, "castPanel").activeSelf, "Cancel/block hides gauge " + i);
                if (i == 2)
                {
                    Assert(Mathf.Approximately(target.Progress, 0.35f), "Device accumulated progress survives cancellation");
                    caster.SetInputBlocked(false);
                    target.AdvanceCast(MagicType.Stabilize, target.CastDuration * 0.15f);
                    Set(caster, "castingTarget", target);
                    Invoke(magic, "LateUpdate");
                    Assert(Mathf.Approximately(Get<Image>(magic, "progressFill").fillAmount, 0.5f), "Resumed device gauge reads saved progress");
                    caster.SetInputBlocked(true);
                }
            }
            caster.SetInputBlocked(false);
            caster.ResetForNewDay();
            Set(interactor, "currentTarget", null);
            Object.DestroyImmediate(targetObject);
        }

        private static void CheckDataAndModals()
        {
            SetGameSize(1920, 1080);
            Refresh();
            CheckSupplyPanelLayout(false, 1920, 1080);
            Capture("day-supply-day1-1920x1080.png");
            SetGameSize(1280, 720);
            Refresh();
            CheckSupplyPanelLayout(false, 1280, 720);
            Capture("day-supply-day1-1280x720.png");
            SetGameSize(1920, 1080);
            day.DeferDaySupplyChoice();

            Set(day.Status, "stability", 64f);
            Set(day.Player, "contamination", 38f);
            day.Inventory.AddManaCrystals(9);
            day.Inventory.TryAddItem(ItemType.EmergencyRepairKit, 2);
            Refresh();
            Assert(Mathf.Approximately(Get<Image>(ward, "stabilityFill").fillAmount, 0.64f), "Stability updates from WardStatus");
            Assert(Mathf.Approximately(Get<Image>(ward, "contaminationFill").fillAmount, 0.38f), "Contamination updates from player");
            Assert(layout.DayStatusText.text.Contains(day.Inventory.ManaCrystals + "개"), "Crystals from actual inventory");
            TMP_Text[] slots = Get<TMP_Text[]>(layout, "itemLabels");
            for (int i = 0; i < slots.Length; i++)
                Assert(day.Inventory.GetQuantity(i) == 0 ? slots[i].text == "빈 칸"
                    : slots[i].text.Contains("x" + day.Inventory.GetQuantity(i)), "Inventory slot data " + i);
            float scale = Time.timeScale;
            CursorLockMode cursor = Cursor.lockState;
            day.ToggleMap();
            Refresh();
            Assert(day.IsMapOpen && Time.timeScale == 0 && caster.InputBlocked && !layout.HUDRoot.gameObject.activeSelf,
                "Map keeps pause/input behavior and hides HUD");
            day.ToggleMap();
            Refresh();
            Assert(!day.IsMapOpen && Time.timeScale == scale && Cursor.lockState == cursor, "Map restores cursor/time");
            day.ToggleInventory();
            Refresh();
            Assert(day.InventoryOpen && Time.timeScale == 0 && caster.InputBlocked, "Inventory keeps input blocking");
            day.ToggleInventory();
            for (int next = 2; next <= 4; next++)
            {
                Invoke(day, "CompleteDay");
                Refresh();
                Assert(day.State == DayState.DayClear && Get<GameObject>(dayHUD, "clearPanel").activeSelf
                    && !layout.HUDRoot.gameObject.activeSelf, "CLEAR has no stale gameplay HUD");
                day.BeginNextDay();
                spawner.SetSpawningEnabled(false);
                Refresh();
                GameObject conditionPanel = Get<TMP_Text>(layout, "floodText").transform.parent.gameObject;
                if (next < FloodConditionRules.FirstFloodConditionDay)
                    Assert(!conditionPanel.activeSelf, "DAY " + next + " hides the entire inactive flood panel");
                else
                {
                    Assert(conditionPanel.activeSelf
                        && Get<TMP_Text>(layout, "floodText").text.Contains(FloodConditionRules.GetName(day.ActiveFloodCondition)),
                        "Actual condition displayed DAY " + next);
                    string highlightedRule = FloodConditionRules.GetHighlightedRuleDescription(day.ActiveFloodCondition)
                        .Replace(" · ", "\n");
                    Assert(Get<TMP_Text>(layout, "floodText").text.Contains(highlightedRule),
                        "Flood HUD shows the active condition's current rule values with highlights");
                    CheckSupplyPanelLayout(true, 1920, 1080);
                    Capture("day-supply-day4-1920x1080.png");
                    SetGameSize(1280, 720);
                    Refresh();
                    CheckSupplyPanelLayout(true, 1280, 720);
                    Capture("day-supply-day4-1280x720.png");
                    SetGameSize(1920, 1080);
                }
                day.DeferDaySupplyChoice();
            }

            FloodCondition actualCondition = day.ActiveFloodCondition;
            day.SetActiveFloodConditionForTest(FloodCondition.None);
            Refresh();
            Assert(!Get<TMP_Text>(layout, "floodText").transform.parent.gameObject.activeSelf,
                "DAY 4 hides the panel when no condition is active");
            day.SetActiveFloodConditionForTest(actualCondition);
            Refresh();
        }

        private static void CheckSupplyPanelLayout(bool expectCondition, int width, int height)
        {
            Assert(Screen.width == width && Screen.height == height,
                "Supply panel rendered at " + width + "x" + height);
            GameObject panelObject = Get<GameObject>(dayHUD, "supplyPanel");
            Assert(panelObject.activeSelf, "DAY supply panel is open");
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            TMP_Text title = panel.Find("Title").GetComponent<TMP_Text>();
            TMP_Text heading = panel.Find("ConditionHeading").GetComponent<TMP_Text>();
            TMP_Text name = panel.Find("ConditionName").GetComponent<TMP_Text>();
            TMP_Text effect = panel.Find("ConditionEffect").GetComponent<TMP_Text>();
            TMP_Text hint = panel.Find("ConditionHint").GetComponent<TMP_Text>();
            TMP_Text guide = panel.Find("SupplyGuide").GetComponent<TMP_Text>();
            Assert(heading.gameObject.activeSelf == expectCondition
                && name.gameObject.activeSelf == expectCondition
                && effect.gameObject.activeSelf == expectCondition
                && hint.gameObject.activeSelf == expectCondition,
                "Supply condition fields are hidden when absent and shown when active");
            if (expectCondition)
            {
                AssertNoOverlap(title.rectTransform, heading.rectTransform, "Supply title and flood heading");
                AssertNoOverlap(heading.rectTransform, name.rectTransform, "Supply condition heading and name");
                AssertNoOverlap(name.rectTransform, effect.rectTransform, "Supply condition name and effect");
                AssertNoOverlap(effect.rectTransform, hint.rectTransform, "Supply effect and response hint");
                AssertNoOverlap(hint.rectTransform, guide.rectTransform, "Supply response hint and item guidance");
            }
            else AssertNoOverlap(title.rectTransform, guide.rectTransform, "Supply title and item guidance");

            Button potion = panel.Find("SupplyOptions/ChoosePotion").GetComponent<Button>();
            Button repair = panel.Find("SupplyOptions/ChooseRepairKit").GetComponent<Button>();
            Button defer = panel.Find("DeferSupply").GetComponent<Button>();
            Assert(!potion.transform.parent.gameObject.activeSelf || potion.gameObject.activeSelf,
                "Potion choice remains available in choice mode");
            if (potion.transform.parent.gameObject.activeSelf)
            {
                AssertNoOverlap(guide.rectTransform, potion.GetComponent<RectTransform>(), "Guidance and potion button");
                AssertNoOverlap(guide.rectTransform, repair.GetComponent<RectTransform>(), "Guidance and repair button");
            }
            Assert(RectContains(ScreenRect(panel), ScreenRect(defer.GetComponent<RectTransform>())),
                "Defer button remains inside the supply panel");

            foreach (TMP_Text text in panelObject.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
                text.ForceMeshUpdate();
                Assert(!text.isTextTruncated, "Supply text is not truncated: " + text.name);
            }
        }

        private static void PrepareMultipleAnomalies()
        {
            spawner.SetSpawningEnabled(false);
            spawner.ClearActiveAnomalies();
            string[] paths = { "Assets/Prefabs/ManaRift_Test.prefab", "Assets/Prefabs/ManaRift_Test.prefab",
                "Assets/Prefabs/WardShiftPrototype/Anomalies/CurseAnomaly.prefab", "Assets/Prefabs/WardShiftPrototype/Devices/ManaDevice_Overload.prefab" };
            for (int i = 0; i < paths.Length; i++)
            {
                AnomalyBehaviour prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]).GetComponent<AnomalyBehaviour>();
                AnomalySpawnPoint point = Object.FindObjectsByType<AnomalySpawnPoint>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => !p.IsOccupied && p.CanSpawnPrefab(prefab));
                AnomalyBehaviour spawnedAnomaly = null;
                Assert(point != null && point.TrySpawn(prefab, out spawnedAnomaly),
                    "Spawn presentation fixture " + prefab.DisplayName);
                Refresh();
                if (spawnedAnomaly != null)
                {
                    string cardText = VisibleAlertText(spawnedAnomaly.GetInstanceID());
                    Assert(cardText.Contains("2단계까지")
                        && cardText.Contains(DisplaySecondsForTest(spawnedAnomaly.SecondsUntilNextStage))
                        && cardText.Contains("파열까지")
                        && cardText.Contains(DisplaySecondsForTest(spawnedAnomaly.SecondsUntilRupture)),
                        "Stage-one card reads real next-stage and rupture timers for " + spawnedAnomaly.DisplayName);
                }
                int active = AnomalyBehaviour.ActiveAnomalies.Count(a => a != null && a.IsHazardActive);
                Assert(VisibleAnomalyCardNames().Length == Mathf.Min(3, active),
                    "One independent alert card per visible anomaly after " + active + " spawns");
                if (i == 1)
                {
                    ManaRift[] sameZoneRifts = AnomalyBehaviour.ActiveAnomalies.OfType<ManaRift>().ToArray();
                    if (sameZoneRifts.Length == 2 && sameZoneRifts[0].Zone != null)
                        sameZoneRifts[1].SetZoneIdentity(sameZoneRifts[0].Zone);
                    Refresh();
                    Assert(sameZoneRifts.Length == 2
                        && sameZoneRifts[0].Zone == sameZoneRifts[1].Zone
                        && VisibleAnomalyCardNames().Contains("AnomalyCard_" + sameZoneRifts[0].GetInstanceID())
                        && VisibleAnomalyCardNames().Contains("AnomalyCard_" + sameZoneRifts[1].GetInstanceID()),
                        "Two anomalies in one zone retain separate cards");
                }
            }

            ManaRift[] rifts = AnomalyBehaviour.ActiveAnomalies.OfType<ManaRift>().ToArray();
            Assert(rifts.Length == 2, "Two separate rift instances are represented");
            Refresh();
            Assert(rifts.Length == 2 && rifts[0].Zone == rifts[1].Zone,
                "Both presentation fixtures share one zone identity");

            ManaDeviceAnomaly device = AnomalyBehaviour.ActiveAnomalies.OfType<ManaDeviceAnomaly>().FirstOrDefault();
            Assert(device != null, "Actual device anomaly spawned");
            device.AdvanceHazard(Get<float>(device, "firstBurstDelaySeconds") + 0.1f);
            caster.SetInputBlocked(true);
            caster.SetInputBlocked(false);
            Refresh();
            Assert(device.IsBurstWarningActive && Get<TMP_Text>(ward, "warningText").text.Contains("방출 예고"),
                "Device warning survives stopping cast");
            Assert(Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None).Any(r => r.name.Contains("Warning") && r.enabled),
                "Device world warning ring remains visible");
            Assert(AnomalyBehaviour.ActiveAnomalies.Count(a => a != null && a.IsHazardActive) >= 4, "Multiple active anomalies for bounded HUD");
            RectTransform cards = Get<RectTransform>(ward, "anomalyCardList");
            Assert(cards.GetComponent<VerticalLayoutGroup>() != null, "Anomaly alerts use one reusable vertical card layout");
            Assert(VisibleAnomalyCardNames().Length == 3, "At most three full anomaly cards are visible");
            Assert(cards.parent.Find("OverflowCount").GetComponent<TMP_Text>().text
                .Contains("그 외 이상현상 1개"), "Overflow count points to the existing M map");

            if (rifts.Length == 2)
            {
                ManaRift older = rifts[0];
                ManaRift newer = rifts[1];
                older.AdvanceHazard(20f);
                newer.AdvanceHazard(45f);
                Refresh();
                Assert(VisibleAlertText(older.GetInstanceID()).Contains("3단계까지 ")
                    && VisibleAlertText(older.GetInstanceID()).Contains(DisplaySecondsForTest(older.SecondsUntilNextStage)),
                    "Stage-two card shows the real countdown to stage three");
                Assert(VisibleAnomalyCardNames()[0] == "AnomalyCard_" + newer.GetInstanceID(),
                    "New stage-three anomaly outranks an older stage-two anomaly");
                older.AdvanceHazard(25f);
                Refresh();
                Assert(VisibleAnomalyCardNames()[0] == "AnomalyCard_" + newer.GetInstanceID(),
                    "Equal stage and rupture time put the newer anomaly first");
                older.AdvanceHazard(7f);
                Refresh();
                Assert(VisibleAnomalyCardNames()[0] == "AnomalyCard_" + older.GetInstanceID(),
                    "Shorter rupture time reorders cards without changing their display size");
                Assert(VisibleAlertText(older.GetInstanceID()).Contains("위험 3단계")
                    && VisibleAlertText(older.GetInstanceID()).Contains("파열까지 8초")
                    && !VisibleAlertText(older.GetInstanceID()).Contains("단계까지"),
                    "Stage growth refreshes stage/rupture countdown and hides the next-stage countdown at stage three");
                older.AdvanceHazard(older.SecondsUntilRupture + 0.1f);
                Refresh();
                Assert(!VisibleAnomalyCardNames().Contains("AnomalyCard_" + older.GetInstanceID()),
                    "Ruptured anomaly card is removed");
            }

            Set(day.Status, "stability", 64f);
            Set(day.Player, "contamination", 38f);
            Refresh();
        }

        private static void PrepareCastScreenshot()
        {
            // 실제 Raycast와 InputAction을 통과하는 임시 봉인 대상. 런타임 안에서만 사용합니다.
            var targetObject = new GameObject("HUD_Check_CaptureTarget");
            MagicTarget target = targetObject.AddComponent<MagicTarget>();
            Set(target, "displayName", "안정화 대상");
            Set(target, "requiredMagic", MagicType.Stabilize);
            Set(target, "castDuration", 10f);
            targetObject.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 2;
            targetObject.AddComponent<SphereCollider>().radius = 0.15f;
            Physics.SyncTransforms();
            gameView.Focus();
            caster.SetInputBlocked(false);
            caster.ResetForNewDay();
            caster.SelectMagic(MagicType.Stabilize);
            Set(caster, "hasFocus", true);
            target.AdvanceCast(MagicType.Seal, target.CastDuration * 0.68f);
            holdCaptureMouse = true;
            InputSystem.onBeforeUpdate += FeedCaptureMouse;
            FeedCaptureMouse();
        }

        private static void FeedCaptureMouse()
        {
            if (holdCaptureMouse && captureMouse != null)
                InputSystem.QueueStateEvent(captureMouse, new MouseState().WithButton(MouseButton.Left, true));
        }

        private static void Refresh()
        {
            Invoke(ward, "LateUpdate");
            Invoke(dayHUD, "Update");
            Invoke(layout, "LateUpdate");
            Invoke(magic, "LateUpdate");
            Canvas.ForceUpdateCanvases();
        }

        private static void CheckBounds()
        {
            Canvas.ForceUpdateCanvases();
            RectTransform root = layout.HUDRoot;
            Vector3[] corners = new Vector3[4];
            foreach (string name in new[] { "AnomalyAlerts", "CoreStatus", "DayResources", "FloodCondition", "MagicAndInventory", "Controls" })
            {
                var rect = (RectTransform)root.Find(name);
                rect.GetWorldCorners(corners);
                foreach (Vector3 point in corners)
                    Assert(point.x >= 0 && point.y >= 0 && point.x <= Screen.width + 1 && point.y <= Screen.height + 1,
                        name + " inside " + Screen.width + "x" + Screen.height);
            }
            RectTransform[] top = { (RectTransform)root.Find("AnomalyAlerts"), (RectTransform)root.Find("CoreStatus"), (RectTransform)root.Find("DayResources") };
            for (int i = 0; i < top.Length - 1; i++)
            {
                top[i].GetWorldCorners(corners);
                float right = corners[2].x;
                top[i + 1].GetWorldCorners(corners);
                Assert(right < corners[0].x, "Top panels separated at " + Screen.width + "x" + Screen.height);
            }
        }

        private static void CheckFonts()
        {
            foreach (TMP_Text text in layout.HUDRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate(true);
                Assert(text.font != null && text.font.name.Contains("NotoSansKR"), "Korean font reused: " + text.name);
                Assert(!text.isTextTruncated, "HUD text fits: " + text.name);
            }
            TMP_Text controls = layout.HUDRoot.Find("Controls/Keys").GetComponent<TMP_Text>();
            Assert(controls.text.Contains("1/2/3") && controls.text.Contains("휠")
                && !controls.text.Contains("Shift")
                && controls.rectTransform.rect.height >= 176f,
                "Controls include spell selection hints and omit the removed Shift sprint hint");
        }

        private static void CheckClearOverlayLayout(int width, int height)
        {
            Assert(Screen.width == width && Screen.height == height,
                "Clear overlay rendered at " + width + "x" + height);
            Canvas.ForceUpdateCanvases();
            GameObject clearPanel = Get<GameObject>(dayHUD, "clearPanel");
            Transform panel = clearPanel.transform;
            RectTransform title = panel.Find("Title").GetComponent<RectTransform>();
            RectTransform summary = panel.Find("Summary").GetComponent<RectTransform>();
            RectTransform nextCondition = panel.Find("NextFloodCondition").GetComponent<RectTransform>();
            RectTransform mobility = panel.Find("Upgrade_Mobility").GetComponent<RectTransform>();
            RectTransform contamination = panel.Find("Upgrade_ContaminationResistance").GetComponent<RectTransform>();
            RectTransform fieldPurification = panel.Find("Upgrade_FieldPurification").GetComponent<RectTransform>();
            RectTransform nextDay = panel.Find("NextDay").GetComponent<RectTransform>();
            RectTransform result = panel.Find("UpgradeResult").GetComponent<RectTransform>();

            AssertNoOverlap(title, summary, "Title and reward summary");
            AssertNoOverlap(summary, nextCondition, "Reward summary and next-condition note");
            AssertNoOverlap(nextCondition, mobility, "Condition note and first card");
            AssertNoOverlap(nextCondition, contamination, "Condition note and second card");
            AssertNoOverlap(nextCondition, fieldPurification, "Condition note and third card");
            AssertNoOverlap(mobility, contamination, "First and second upgrade cards");
            AssertNoOverlap(contamination, fieldPurification, "Second and third upgrade cards");
            AssertNoOverlap(mobility, nextDay, "First card and next-day button");
            AssertNoOverlap(contamination, nextDay, "Second card and next-day button");
            AssertNoOverlap(fieldPurification, nextDay, "Third card and next-day button");
            AssertNoOverlap(result, nextDay, "Purchase result and next-day button");
            Assert(Mathf.Approximately(mobility.rect.width, contamination.rect.width)
                && Mathf.Approximately(mobility.rect.width, fieldPurification.rect.width)
                && Mathf.Approximately(mobility.rect.height, contamination.rect.height)
                && Mathf.Approximately(mobility.rect.height, fieldPurification.rect.height),
                "Upgrade cards share the same width and height");

            foreach (RectTransform card in new[] { mobility, contamination, fieldPurification })
            {
                RectTransform name = card.Find("Name").GetComponent<RectTransform>();
                RectTransform effect = card.Find("Effect").GetComponent<RectTransform>();
                RectTransform purchase = card.Find("Purchase").GetComponent<RectTransform>();
                AssertNoOverlap(name, effect, card.name + " title and effect text");
                AssertNoOverlap(effect, purchase, card.name + " effect text and purchase button");
                Assert(RectContains(ScreenRect(card), ScreenRect(purchase)), card.name + " purchase button remains inside card");
            }
            Assert(RectContains(ScreenRect(panel.GetComponent<RectTransform>()), ScreenRect(nextDay)),
                "Next-day button remains inside clear panel");
            Assert(panel.GetComponentInChildren<LayoutGroup>(true) == null,
                "Clear overlay uses its explicit RectTransform layout without layout-driven collisions");
            Assert(!Get<TMP_Text>(dayHUD, "clearSummaryText").enableAutoSizing
                && !Get<TMP_Text>(dayHUD, "clearConditionText").enableAutoSizing,
                "Clear header uses fixed readable TMP sizes");

            foreach (TMP_Text text in clearPanel.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate();
                Assert(!text.isTextTruncated, "Clear text is not truncated: " + text.name);
            }
        }

        private static Rect ScreenRect(RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static bool RectContains(Rect outer, Rect inner)
        {
            return outer.Contains(new Vector2(inner.xMin, inner.yMin))
                && outer.Contains(new Vector2(inner.xMax, inner.yMax));
        }

        private static void AssertNoOverlap(RectTransform first, RectTransform second, string label)
        {
            Assert(!ScreenRect(first).Overlaps(ScreenRect(second)),
                label + " do not overlap at " + Screen.width + "x" + Screen.height);
        }

        private static string[] VisibleAnomalyCardNames()
        {
            RectTransform cardList = Get<RectTransform>(ward, "anomalyCardList");
            return cardList.Cast<Transform>()
                .Where(child => child.gameObject.activeSelf && child.name.StartsWith("AnomalyCard_", StringComparison.Ordinal))
                .Select(child => child.name).ToArray();
        }

        private static string VisibleAlertText(int instanceId)
        {
            RectTransform cardList = Get<RectTransform>(ward, "anomalyCardList");
            Transform card = cardList.Find("AnomalyCard_" + instanceId);
            TMP_Text details = card != null ? card.Find("Details").GetComponent<TMP_Text>() : null;
            return details != null ? details.text : string.Empty;
        }

        private static string DisplaySecondsForTest(float seconds)
        {
            return float.IsInfinity(seconds) || float.IsNaN(seconds)
                ? "시간 정보 없음" : Mathf.CeilToInt(Mathf.Max(0f, seconds)) + "초";
        }

        private static void SetGameSize(int width, int height)
        {
            Type viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameView == null)
            {
                gameView = EditorWindow.GetWindow(viewType);
                originalSize = (int)viewType.GetProperty("selectedSizeIndex", Private | BindingFlags.Public).GetValue(gameView);
            }
            Type sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            Type groupType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeGroupType");
            sizeGroup = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            if (customSizeIndex >= 0) sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup, new object[] { customSizeIndex });
            Type sizeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize");
            Type typeEnum = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeType");
            object custom = Activator.CreateInstance(sizeType, new[] { Enum.Parse(typeEnum, "FixedResolution"), (object)width, height, "HUD validation" });
            sizeGroup.GetType().GetMethod("AddCustomSize").Invoke(sizeGroup, new[] { custom });
            customSizeIndex = (int)sizeGroup.GetType().GetMethod("GetBuiltinCount").Invoke(sizeGroup, null)
                + (int)sizeGroup.GetType().GetMethod("GetCustomCount").Invoke(sizeGroup, null) - 1;
            viewType.GetProperty("selectedSizeIndex", Private | BindingFlags.Public).SetValue(gameView, customSizeIndex);
            gameView.Repaint();
        }

        private static void Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "/" + name));
            results.AppendLine("CAPTURE " + name + " (" + Screen.width + "x" + Screen.height + ")");
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick;
            holdCaptureMouse = false;
            InputSystem.onBeforeUpdate -= FeedCaptureMouse;
            if (captureMouse != null) InputSystem.RemoveDevice(captureMouse);
            captureMouse = null;
            SessionState.SetBool(RunKey, false);
            results.AppendLine((success ? "PASS " : "FAIL ") + message);
            results.AppendLine("Assertions: " + assertions);
            File.WriteAllText(Output + "/results.txt", results.ToString());
            Debug.Log("[WARD SHIFT HUD CHECK] " + (success ? "PASS " : "FAIL ") + message + " Assertions: " + assertions);
            if (gameView != null)
            {
                gameView.GetType().GetProperty("selectedSizeIndex", Private | BindingFlags.Public).SetValue(gameView, originalSize);
                if (customSizeIndex >= 0) sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup, new object[] { customSizeIndex });
            }
            customSizeIndex = -1;
            gameView = null;
            EditorApplication.isPlaying = false;
        }

        private static void Assert(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
            results.AppendLine("PASS " + message);
        }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    }
}
